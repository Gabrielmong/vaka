using SensorPanel.Layout;

namespace SensorPanel.Sensors;

public sealed record HardwareSummary(string? Cpu, string? Gpu, string? Board, int Fans, IReadOnlyList<char> Drives);

/// <summary>Detected aliases for the template vocabulary, plus the vocabulary keys nothing was found for.</summary>
public sealed record Detection(Dictionary<string, SensorAlias> Aliases, IReadOnlyList<string> Missing, HardwareSummary Summary)
{
    public IEnumerable<string> Found => SensorDetector.Vocabulary.Where(Aliases.ContainsKey);
}

/// <summary>
/// Maps this PC's sensors onto the standard keys templates bind to (cpu.temp, gpu.load, fan.1 …).
/// Rules prefer leaving a key out (the panel shows --) over guessing a sensor that may be wrong.
/// </summary>
public static partial class SensorDetector
{
    public const string SystemPowerExpr = "(cpu.power + gpu.power + 50) / 0.88";

    private const double MiB = 1 / 1024.0, BytesToMB = 1 / 1048576.0;

    /// <summary>Key → (sensor type it must have, scale applied to the raw value).</summary>
    private static readonly (string Key, string Type, double Scale)[] Keys =
    [
        ("cpu.temp", "Temperature", 1), ("cpu.load", "Load", 1), ("cpu.clock", "Clock", 0.001), ("cpu.power", "Power", 1),
        .. Enumerable.Range(1, 8).Select(i => ($"cpu.core{i}", "Load", 1.0)),
        ("gpu.temp", "Temperature", 1), ("gpu.hotspot", "Temperature", 1), ("gpu.load", "Load", 1),
        ("gpu.clock", "Clock", 0.001), ("gpu.power", "Power", 1), ("gpu.fan", "Fan", 1),
        ("vram.used", "SmallData", MiB), ("vram.total", "SmallData", MiB), ("vram.load", "Load", 1),
        ("ram.used", "Data", 1), ("ram.free", "Data", 1), ("ram.load", "Load", 1),
        .. Enumerable.Range(1, 6).Select(i => ($"fan.{i}", "Fan", 1.0)),
        ("pump", "Fan", 1), ("board.temp", "Temperature", 1),
        ("disk.temp", "Temperature", 1), ("disk.read", "Throughput", BytesToMB), ("disk.write", "Throughput", BytesToMB),
        ("net.down", "Throughput", BytesToMB), ("net.up", "Throughput", BytesToMB),
    ];

    private static readonly Dictionary<string, (string Type, double Scale)> KeyInfo =
        Keys.ToDictionary(k => k.Key, k => (k.Type, k.Scale), StringComparer.Ordinal);

    /// <summary>Every detectable key (not system.power, which is always a formula, nor built-ins).</summary>
    public static IReadOnlyList<string> Vocabulary { get; } = Keys.Select(k => k.Key).ToArray();

    /// <param name="keepCalibration">Re-detect on a hand-made layout: a kept alias keeps its own scale/offset
    /// (its text was written for those units). Template builds use the vocabulary's scale instead.</param>
    public static Detection Detect(SensorCatalog catalog, IReadOnlyList<char> drives, PanelLayout? existing = null,
        bool keepCalibration = false)
    {
        var all = catalog.Sensors;
        var found = new Dictionary<string, SensorInfo>(StringComparer.Ordinal);
        void Put(string key, SensorInfo? s) { if (s is not null) found[key] = s; }

        // ── CPU ──
        var cpu = all.Where(s => s.HardwareType == "Cpu").ToList();
        Put("cpu.temp", First(cpu, "Temperature", "Core (Tctl/Tdie)", "CPU Package", "Core Max"));
        Put("cpu.load", First(cpu, "Load", "CPU Total"));
        Put("cpu.clock", First(cpu, "Clock", "Cores (Average)", "Core #1"));
        Put("cpu.power", First(cpu, "Power", "Package", "CPU Package"));
        for (int i = 1; i <= 8; i++)
            Put($"cpu.core{i}", First(cpu, "Load", $"CPU Core #{i}", $"CPU Core #{i} Thread #1"));

        // ── GPU: NVIDIA, then discrete AMD, then the rest (APU "Radeon Graphics" is GpuAmd too), then the most memory.
        // Sleeping laptop dGPUs report no values, so the rank must not depend on them. ──
        var gpuName = all.Where(s => s.HardwareType.StartsWith("Gpu", StringComparison.Ordinal))
            .GroupBy(s => s.Hardware)
            .OrderByDescending(g => GpuRank(g.First()))
            .ThenByDescending(g => First(g, "SmallData", "GPU Memory Total")?.Value ?? 0)
            .Select(g => g.Key).FirstOrDefault();
        var gpu = all.Where(s => s.Hardware == gpuName && s.HardwareType.StartsWith("Gpu", StringComparison.Ordinal)).ToList();
        Put("gpu.temp", First(gpu, "Temperature", "GPU Core"));
        Put("gpu.hotspot", First(gpu, "Temperature", "GPU Hot Spot"));
        Put("gpu.load", First(gpu, "Load", "GPU Core"));
        Put("gpu.clock", First(gpu, "Clock", "GPU Core"));
        Put("gpu.power", First(gpu, "Power", "GPU Package", "GPU Power"));
        Put("gpu.fan", gpu.FirstOrDefault(s => s.Type == "Fan"));
        Put("vram.used", First(gpu, "SmallData", "GPU Memory Used"));
        Put("vram.total", First(gpu, "SmallData", "GPU Memory Total"));
        Put("vram.load", First(gpu, "Load", "GPU Memory"));

        // ── RAM ("Total Memory", not "Virtual Memory") ──
        var ram = all.Where(s => s.HardwareType == "Memory" && s.Id.StartsWith("/ram/", StringComparison.Ordinal)).ToList();
        Put("ram.used", First(ram, "Data", "Memory Used"));
        Put("ram.free", First(ram, "Data", "Memory Available"));
        Put("ram.load", First(ram, "Load", "Memory"));

        // ── Board: fans with a reading, a pump by (renamed) name, a temperature only when clearly named ──
        var board = all.Where(s => s.HardwareType is "SuperIO" or "Motherboard" or "EmbeddedController").ToList();
        string Label(SensorInfo s) => existing?.SensorNames.GetValueOrDefault(s.Id) ?? s.Name;
        var boardFans = board.Where(s => s.Type == "Fan").OrderBy(s => Index(s.Id)).ToList();
        var pump = boardFans.FirstOrDefault(s =>
            Label(s).Contains("pump", StringComparison.OrdinalIgnoreCase) || Label(s).Contains("aio", StringComparison.OrdinalIgnoreCase));
        Put("pump", pump);
        int n = 0;
        foreach (var f in boardFans.Where(f => f != pump && f.Value > 0).Take(6)) Put($"fan.{++n}", f);
        Put("board.temp", board.FirstOrDefault(s => s.Type == "Temperature" &&
            (s.Name.Contains("System", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Motherboard", StringComparison.OrdinalIgnoreCase) ||
             s.Name.Contains("Mainboard", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("PCH", StringComparison.OrdinalIgnoreCase))));

        // ── System disk: the storage device whose size matches the Windows drive (within 3%) ──
        var disk = SystemDisk(all, drives);
        if (disk is not null)
        {
            var dev = all.Where(s => s.Hardware == disk && s.HardwareType == "Storage").ToList();
            Put("disk.temp", First(dev, "Temperature", "Composite Temperature", "Temperature") ?? dev.FirstOrDefault(s => s.Type == "Temperature"));
            Put("disk.read", First(dev, "Throughput", "Read Rate"));
            Put("disk.write", First(dev, "Throughput", "Write Rate"));
        }

        // ── Network: the adapter that has downloaded the most ──
        var nic = all.Where(s => s.HardwareType == "Network" && s.Name == "Data Downloaded")
            .OrderByDescending(s => s.Value ?? 0).Select(s => s.Hardware).FirstOrDefault();
        var net = all.Where(s => s.HardwareType == "Network" && s.Hardware == nic).ToList();
        Put("net.down", First(net, "Throughput", "Download Speed"));
        Put("net.up", First(net, "Throughput", "Upload Speed"));

        var aliases = new Dictionary<string, SensorAlias>(StringComparer.Ordinal);
        foreach (var (key, type, scale) in Keys)
            if (found.TryGetValue(key, out var s))
                aliases[key] = new SensorAlias { Sensor = s.Id, Name = IsDuplicateId(all, s.Id) ? s.Name : null, Scale = scale };
        aliases["system.power"] = new SensorAlias { Expr = SystemPowerExpr };

        var fans = Enumerable.Range(1, 6).Count(i => aliases.ContainsKey($"fan.{i}")) + (aliases.ContainsKey("pump") ? 1 : 0);
        var summary = new HardwareSummary(
            cpu.FirstOrDefault()?.Hardware, gpuName,
            all.FirstOrDefault(s => s.HardwareType == "Motherboard")?.Hardware, fans, drives.ToArray());
        var detection = new Detection(aliases, Vocabulary.Where(k => !aliases.ContainsKey(k)).ToList(), summary);
        return existing is null ? detection : Merge(detection, existing, catalog, keepCalibration);
    }

    /// <summary>
    /// The user's own aliases win when they still work: same sensor type for vocabulary keys (scale reset to
    /// the vocabulary's so units match), any formula that parses. Everything else comes from detection.
    /// </summary>
    public static Detection Merge(Detection detected, PanelLayout existing, SensorCatalog catalog, bool keepCalibration = false)
    {
        var aliases = new Dictionary<string, SensorAlias>(detected.Aliases, StringComparer.Ordinal);
        foreach (var (key, mine) in existing.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(mine.Expr))
            {
                if ((KeyInfo.ContainsKey(key) || key == "system.power") && Formula.TryParse(mine.Expr, out _, out _))
                    aliases[key] = new SensorAlias { Sensor = "", Expr = mine.Expr };
                continue;
            }
            if (!KeyInfo.TryGetValue(key, out var info)) continue;
            var s = catalog.Find(mine.Sensor, mine.Name);
            if (s is null || s.Type != info.Type) continue;
            aliases[key] = keepCalibration
                ? new SensorAlias { Sensor = mine.Sensor, Name = mine.Name, Scale = mine.Scale, Offset = mine.Offset }
                : new SensorAlias { Sensor = mine.Sensor, Name = mine.Name, Scale = info.Scale };
        }
        var summary = detected.Summary with
        {
            Fans = Enumerable.Range(1, 6).Count(i => aliases.ContainsKey($"fan.{i}")) + (aliases.ContainsKey("pump") ? 1 : 0),
        };
        return new Detection(aliases, Vocabulary.Where(k => !aliases.ContainsKey(k)).ToList(), summary);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Radeon(\(TM\))?(\s+\d+M)?\s+Graphics", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AmdIntegratedName();

    private static int GpuRank(SensorInfo s) => s.HardwareType switch
    {
        "GpuNvidia" => 2,
        "GpuAmd" when !AmdIntegratedName().IsMatch(s.Hardware) => 1,
        _ => 0,
    };

    private static SensorInfo? First(IEnumerable<SensorInfo> sensors, string type, params string[] names)
    {
        var list = sensors as IList<SensorInfo> ?? sensors.ToList();
        foreach (var name in names)
            if (list.FirstOrDefault(s => s.Type == type && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } hit)
                return hit;
        return null;
    }

    private static string? SystemDisk(IReadOnlyList<SensorInfo> all, IReadOnlyList<char> drives)
    {
        if (drives.Count == 0) return null;
        var gib = all.FirstOrDefault(s => s.Id == $"drive.{drives[0]}.totalGB")?.Value;
        if (gib is not > 0) return null;
        double gb = gib.Value * 1.073741824; // LHM reports decimal GB
        return all.Where(s => s.HardwareType == "Storage" && s.Name == "Total Space" && s.Value > 0)
            .Select(s => (s.Hardware, Diff: Math.Abs(s.Value!.Value - gb) / s.Value.Value))
            .Where(x => x.Diff <= 0.03)
            .OrderBy(x => x.Diff).Select(x => x.Hardware).FirstOrDefault();
    }

    private static int Index(string id) =>
        int.TryParse(id[(id.LastIndexOf('/') + 1)..], out var i) ? i : int.MaxValue;

    private static bool IsDuplicateId(IReadOnlyList<SensorInfo> all, string id) => all.Count(s => s.Id == id) > 1;
}
