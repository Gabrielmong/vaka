using LibreHardwareMonitor.Hardware;
using Vaka.Layout;

namespace Vaka.Sensors;

/// <summary>
/// Owns the LibreHardwareMonitor <see cref="Computer"/> and reads only the sensors the
/// current layout needs. Not thread-safe: call from a single polling thread.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private Computer? _computer;
    private readonly Dictionary<string, List<ISensor>> _byId = new(StringComparer.Ordinal);

    private PanelLayout? _resolvedFor;
    private bool _layoutWantsFps;

    /// <summary>FPS capture also runs until this time (UTC), e.g. while the editor's template gallery is open.</summary>
    public DateTime FpsLeaseUntil { get; set; }
    private Dictionary<string, Func<double?>> _readers = new();
    private List<IHardware> _toUpdate = new();
    private readonly HashSet<string> _warned = new();

    public bool IsOpen => _computer is not null;

    /// <summary>Foreground-app frame rate; runs PresentMon only while a resolved layout uses fps keys.</summary>
    public FpsMonitor Fps { get; }

    public SensorHub(FpsMonitor? fps = null) => Fps = fps ?? new FpsMonitor();
    public SensorGroups OpenGroups { get; private set; }

    /// <summary>Opens only the hardware groups needed — each group costs memory and startup time.</summary>
    public void Open(SensorGroups groups)
    {
        Close();
        var computer = new Computer
        {
            IsCpuEnabled = groups.HasFlag(SensorGroups.Cpu),
            IsGpuEnabled = groups.HasFlag(SensorGroups.Gpu),
            IsMemoryEnabled = groups.HasFlag(SensorGroups.Memory),
            IsMotherboardEnabled = groups.HasFlag(SensorGroups.Motherboard),
            IsStorageEnabled = groups.HasFlag(SensorGroups.Storage),
            IsNetworkEnabled = groups.HasFlag(SensorGroups.Network),
            IsControllerEnabled = groups.HasFlag(SensorGroups.Controller),
        };
        computer.Open();
        _computer = computer;
        OpenGroups = groups;
        _resolvedFor = null;

        // Some sensors (e.g. Super I/O fans) are only created after the first update.
        foreach (var hw in computer.Hardware) UpdateTree(hw);

        _byId.Clear();
        foreach (var hw in computer.Hardware) IndexHardware(hw);
    }

    private void UpdateTree(IHardware hw)
    {
        try { hw.Update(); }
        catch (Exception ex) { WarnOnce("update:" + hw.Identifier, $"Updating {hw.Name} failed: {ex.Message}"); }
        foreach (var sub in hw.SubHardware) UpdateTree(sub);
    }

    // Virtual network adapters (packet scheduler / filter layers) duplicate the real ones.
    private static bool IsNoiseAdapter(IHardware hw) =>
        hw.HardwareType == HardwareType.Network &&
        (hw.Name.Contains("-QoS", StringComparison.Ordinal) || hw.Name.Contains("-WFP", StringComparison.Ordinal) ||
         hw.Name.Contains("Kernel Debugger", StringComparison.Ordinal));

    /// <summary>Updates all open hardware and snapshots every sensor (plus built-in drives).</summary>
    public SensorCatalog BuildCatalog()
    {
        var list = new List<SensorInfo>();
        if (_computer is not null)
        {
            foreach (var hw in _computer.Hardware)
            {
                if (IsNoiseAdapter(hw)) continue;
                UpdateTree(hw);
                Collect(hw, list);
            }
        }
        list.AddRange(BuiltinSensors.CatalogEntries());
        list.AddRange(Fps.CatalogEntries());
        return new SensorCatalog(list, DateTime.Now);
    }

    private static void Collect(IHardware hw, List<SensorInfo> list)
    {
        foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            var type = s.SensorType.ToString();
            list.Add(new SensorInfo(s.Identifier.ToString(), s.Name, type, SensorCatalog.UnitFor(type),
                hw.Name, hw.HardwareType.ToString(), SensorCatalog.Clean(s.Value)));
        }
        foreach (var sub in hw.SubHardware) Collect(sub, list);
    }

    private void IndexHardware(IHardware hw)
    {
        foreach (var s in hw.Sensors) AddToIndex(s);
        // Sensors can still appear later (fan spinning up, etc.) — index them and re-resolve.
        hw.SensorAdded += s =>
        {
            AddToIndex(s);
            _resolvedFor = null;
        };
        foreach (var sub in hw.SubHardware) IndexHardware(sub);
    }

    private void AddToIndex(ISensor s)
    {
        var id = s.Identifier.ToString();
        if (!_byId.TryGetValue(id, out var list)) _byId[id] = list = new List<ISensor>();
        if (!list.Contains(s)) list.Add(s);
    }

    /// <summary>Updates the hardware behind <paramref name="keys"/> and returns their current values.</summary>
    public Dictionary<string, double?> Poll(PanelLayout layout, IReadOnlySet<string> keys)
    {
        if (!ReferenceEquals(_resolvedFor, layout)) Resolve(layout, keys);
        Fps.Wanted = _layoutWantsFps || DateTime.UtcNow < FpsLeaseUntil;
        Fps.Tick();

        foreach (var hw in _toUpdate)
        {
            try { hw.Update(); }
            catch (Exception ex) { WarnOnce("update:" + hw.Identifier, $"Updating {hw.Name} failed: {ex.Message}"); }
        }

        var result = new Dictionary<string, double?>(_readers.Count, StringComparer.Ordinal);
        foreach (var (key, read) in _readers)
        {
            try { result[key] = read(); }
            catch { result[key] = null; }
        }
        return result;
    }

    private void Resolve(PanelLayout layout, IReadOnlySet<string> keys)
    {
        var readers = new Dictionary<string, Func<double?>>(StringComparer.Ordinal);
        var built = new Dictionary<string, Func<double?>>(StringComparer.Ordinal);
        var building = new HashSet<string>(StringComparer.Ordinal);
        var hardware = new HashSet<IHardware>();
        bool wantsFps = false;

        foreach (var key in keys) readers[key] = Build(key);

        Func<double?> Build(string key)
        {
            if (built.TryGetValue(key, out var done)) return done;
            if (!building.Add(key))
            {
                WarnOnce("cycle:" + key, $"Formula alias '{key}' refers to itself");
                return static () => null;
            }
            Func<double?>? reader = null;
            if (layout.Aliases.TryGetValue(key, out var alias) && !string.IsNullOrWhiteSpace(alias.Expr))
            {
                if (Formula.TryParse(alias.Expr, out var f, out var error))
                {
                    var deps = f!.Keys.ToDictionary(k => k, Build, StringComparer.Ordinal);
                    reader = () => f.Evaluate(k => deps.TryGetValue(k, out var d) ? d() : null);
                }
                else
                {
                    WarnOnce("expr:" + key + alias.Expr, $"Formula alias '{key}': {error}");
                    reader = static () => null;
                }
            }
            else if (alias is not null)
            {
                if (Find(alias.Sensor, alias.Name) is { } s)
                {
                    double scale = alias.Scale, offset = alias.Offset;
                    reader = () => s.Value is { } v ? v * scale + offset : null;
                    hardware.Add(s.Hardware);
                }
            }
            else if (key.StartsWith('/'))
            {
                if (Find(key, null) is { } s)
                {
                    reader = () => s.Value;
                    hardware.Add(s.Hardware);
                }
            }
            else if (FpsMonitor.IsKey(key))
            {
                reader = Fps.CreateReader(key);
                wantsFps = true;
            }
            else
            {
                reader = BuiltinSensors.TryCreate(key);
            }

            if (reader is null)
            {
                WarnOnce("key:" + key, $"Unknown sensor key '{key}'");
                reader = static () => null;
            }
            building.Remove(key);
            built[key] = reader;
            return reader;
        }

        // Sub-hardware (e.g. the Super I/O chip under the motherboard) only reads fresh values
        // when its parent is updated first, so update parents before children.
        var ordered = new List<IHardware>();
        foreach (var hw in hardware)
        {
            if (hw.Parent is { } parent && !ordered.Contains(parent)) ordered.Add(parent);
        }
        foreach (var hw in hardware)
        {
            if (!ordered.Contains(hw)) ordered.Add(hw);
        }

        _readers = readers;
        _toUpdate = ordered;
        _layoutWantsFps = wantsFps;
        Fps.Wanted = wantsFps || DateTime.UtcNow < FpsLeaseUntil;
        _resolvedFor = layout;
    }

    private ISensor? Find(string id, string? name)
    {
        if (!_byId.TryGetValue(id, out var list) || list.Count == 0) return null;
        if (name is null) return list[0];
        return list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    private void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) Log.Write(message);
    }

    private void Close()
    {
        try { _computer?.Close(); } catch (Exception ex) { Log.Write($"Closing sensors failed: {ex.Message}"); }
        _computer = null;
        _byId.Clear();
        _readers = new();
        _toUpdate = new();
        _resolvedFor = null;
        OpenGroups = SensorGroups.None;
    }

    public void Dispose()
    {
        Close();
        Fps.Dispose();
    }
}
