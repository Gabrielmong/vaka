using SensorPanel.Layout;

namespace SensorPanel.Sensors;

[Flags]
public enum SensorGroups
{
    None = 0,
    Cpu = 1,
    Gpu = 2,
    Memory = 4,
    Motherboard = 8,
    Storage = 16,
    Network = 32,
    Controller = 64,
    All = Cpu | Gpu | Memory | Motherboard | Storage | Network | Controller,
}

public static class SensorGroupResolver
{
    /// <summary>Which LibreHardwareMonitor groups must be opened to serve these keys.</summary>
    public static SensorGroups ForKeys(PanelLayout layout, IEnumerable<string> keys)
    {
        var groups = SensorGroups.None;
        foreach (var key in Formula.ExpandKeys(layout, keys))
        {
            var id = layout.Aliases.TryGetValue(key, out var alias) ? alias.Sensor : key;
            groups |= ForIdentifier(id);
        }
        return groups;
    }

    /// <summary>Maps an LHM identifier like "/amdcpu/0/temperature/2" to its group.</summary>
    public static SensorGroups ForIdentifier(string id)
    {
        if (!id.StartsWith('/')) return SensorGroups.None;
        var root = id.AsSpan(1);
        int slash = root.IndexOf('/');
        var kind = (slash < 0 ? root : root[..slash]).ToString();

        return kind switch
        {
            "amdcpu" or "intelcpu" or "genericcpu" => SensorGroups.Cpu,
            _ when kind.StartsWith("gpu-") => SensorGroups.Gpu,
            "ram" or "vram" or "memory" => SensorGroups.Memory,
            "lpc" or "motherboard" or "ec" => SensorGroups.Motherboard,
            "hdd" or "nvme" or "ssd" or "storage" => SensorGroups.Storage,
            "nic" => SensorGroups.Network,
            _ => SensorGroups.Controller,
        };
    }
}
