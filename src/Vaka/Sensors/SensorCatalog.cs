using Vaka.Layout;

namespace Vaka.Sensors;

public sealed record SensorInfo(
    string Id, string Name, string Type, string Unit, string Hardware, string HardwareType, double? Value);

/// <summary>
/// Immutable snapshot of every known sensor and its value. Used by the editor to browse
/// sensors and to render previews of unsaved layouts.
/// </summary>
public sealed class SensorCatalog
{
    private readonly Dictionary<string, List<SensorInfo>> _byId;

    public IReadOnlyList<SensorInfo> Sensors { get; }
    public DateTime Time { get; }

    public SensorCatalog(IReadOnlyList<SensorInfo> sensors, DateTime time)
    {
        Sensors = sensors;
        Time = time;
        _byId = new Dictionary<string, List<SensorInfo>>(StringComparer.Ordinal);
        foreach (var s in sensors)
        {
            if (!_byId.TryGetValue(s.Id, out var list)) _byId[s.Id] = list = new List<SensorInfo>();
            list.Add(s);
        }
    }

    public SensorInfo? Find(string id, string? name = null)
    {
        if (!_byId.TryGetValue(id, out var list) || list.Count == 0) return null;
        if (name is null) return list[0];
        return list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    /// <summary>Resolves a layout key (alias, raw id or built-in) against this snapshot.</summary>
    public double? Resolve(PanelLayout layout, string key) => Resolve(layout, key, 0);

    private double? Resolve(PanelLayout layout, string key, int depth)
    {
        if (layout.Aliases.TryGetValue(key, out var alias))
        {
            if (!string.IsNullOrWhiteSpace(alias.Expr))
                return depth < 8 && Formula.TryParse(alias.Expr, out var f, out _)
                    ? f!.Evaluate(k => Resolve(layout, k, depth + 1))
                    : null;
            return Find(alias.Sensor, alias.Name)?.Value is { } v ? v * alias.Scale + alias.Offset : null;
        }
        return Find(key)?.Value;
    }

    public static string UnitFor(string sensorType) => sensorType switch
    {
        "Voltage" => "V",
        "Current" => "A",
        "Power" => "W",
        "Clock" => "MHz",
        "Temperature" => "°C",
        "Load" or "Control" or "Level" or "Humidity" => "%",
        "Frequency" => "Hz",
        "Fan" => "RPM",
        "Flow" => "L/h",
        "Data" => "GB",
        "SmallData" => "MB",
        "Throughput" => "B/s",
        "TimeSpan" => "s",
        "Energy" => "mWh",
        "Noise" => "dBA",
        _ => "",
    };

    /// <summary>JSON can't carry NaN/Infinity.</summary>
    public static double? Clean(double? v) => v is { } d && double.IsFinite(d) ? d : null;
}
