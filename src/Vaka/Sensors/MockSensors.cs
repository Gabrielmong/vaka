namespace Vaka.Sensors;

/// <summary>
/// A scripted stand-in for real hardware (the owner's rig: Ryzen 7 3800X, RTX 3070, drives C–F), used to render
/// the website's panel recordings and in tests. Values follow a 30 s loop — 10 s each of Idle, Gaming and
/// Rendering — easing between states, with small noise built from sines whose periods divide the loop, so the
/// output is deterministic and value(t) == value(t + 30) for every sensor.
/// </summary>
public sealed class MockSensors
{
    public const int LoopSeconds = 30;
    private const double EaseSeconds = 3;

    public static readonly IReadOnlyList<char> Drives = ['C', 'D', 'E', 'F'];

    public static readonly IReadOnlyList<(string Name, int From, int To)> States =
        [("Idle", 0, 10), ("Gaming", 10, 20), ("Rendering", 20, 30)];

    /// <summary>Loop start: 20:41:15, so a 30 s loop never changes an HH:mm clock.</summary>
    private static readonly DateTime Start = new(2026, 11, 20, 20, 41, 15, DateTimeKind.Local);

    private const string Cpu = "AMD Ryzen 7 3800X", Gpu = "NVIDIA GeForce RTX 3070", Board = "ITE IT8665E",
        Nvme = "XPG GAMMIX S50 Lite", Hdd = "ST5000DM000", Nic = "Ethernet", Fps = "Foreground app (PresentMon)";

    private sealed record Spec(string Id, string Name, string Type, string Hardware, string HardwareType, Func<double, double?> Value);

    private readonly List<Spec> _specs = [];

    public MockSensors()
    {
        // ── CPU ──
        Add("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "Temperature", Cpu, "Cpu", [38, 68, 84], 1.2, max: 95);
        Add("/amdcpu/0/temperature/4", "CCD1 (Tdie)", "Temperature", Cpu, "Cpu", [36, 66, 82], 1.2, max: 95);
        Add("/amdcpu/0/load/0", "CPU Total", "Load", Cpu, "Cpu", [4, 45, 100], 1.5);
        Add("/amdcpu/0/load/1", "CPU Core Max", "Load", Cpu, "Cpu", [16, 82, 100], 3);
        double[] idleCores = [6, 3, 9, 2, 5, 2, 4, 3], gameCores = [78, 64, 58, 51, 44, 38, 31, 26];
        for (int i = 0; i < 8; i++)
            Add($"/amdcpu/0/load/{i + 2}", $"CPU Core #{i + 1}", "Load", Cpu, "Cpu", [idleCores[i], gameCores[i], 99], 2.5);
        Add("/amdcpu/0/clock/1", "Cores (Average)", "Clock", Cpu, "Cpu", [3620, 4310, 4140], 35);
        Add("/amdcpu/0/power/0", "Package", "Power", Cpu, "Cpu", [27, 71, 117], 2);

        // ── GPU ──
        Add("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", Gpu, "GpuNvidia", [36, 72, 45], 0.8);
        Add("/gpu-nvidia/0/temperature/2", "GPU Hot Spot", "Temperature", Gpu, "GpuNvidia", [47, 85, 56], 1);
        Add("/gpu-nvidia/0/load/0", "GPU Core", "Load", Gpu, "GpuNvidia", [3, 97, 6], 1.5);
        Add("/gpu-nvidia/0/load/3", "GPU Memory", "Load", Gpu, "GpuNvidia", [8, 63, 11], 2);
        Add("/gpu-nvidia/0/clock/0", "GPU Core", "Clock", Gpu, "GpuNvidia", [210, 1905, 645], 15);
        Add("/gpu-nvidia/0/power/0", "GPU Package", "Power", Gpu, "GpuNvidia", [19, 206, 36], 4);
        Add("/gpu-nvidia/0/fan/1", "GPU Fan 1", "Fan", Gpu, "GpuNvidia", [980, 1680, 1090], 15);
        Add("/gpu-nvidia/0/smalldata/1", "GPU Memory Used", "SmallData", Gpu, "GpuNvidia", [1480, 6420, 1960], 30);
        Add("/gpu-nvidia/0/smalldata/2", "GPU Memory Total", "SmallData", Gpu, "GpuNvidia", [8192, 8192, 8192], 0);

        // ── RAM (32 GB) ──
        var ramUsed = Scripted("/ram/data/0", [10.4, 18.7, 22.9], 0.15, 0, 31.9);
        Sensor("/ram/load/0", "Memory", "Load", "Total Memory", "Memory", t => ramUsed(t) / 31.9 * 100);
        Sensor("/ram/data/0", "Memory Used", "Data", "Total Memory", "Memory", t => ramUsed(t));
        Sensor("/ram/data/1", "Memory Available", "Data", "Total Memory", "Memory", t => 31.9 - ramUsed(t));

        // ── Board: CPU fan, five case fans, the AIO pump and a system temperature ──
        Add("/lpc/it8665e/0/temperature/0", "System", "Temperature", Board, "SuperIO", [33, 37, 39], 0.3);
        Add("/lpc/it8665e/0/fan/0", "Fan #1", "Fan", Board, "SuperIO", [790, 1160, 1490], 12);
        double[][] caseFans = [[640, 830, 910], [655, 845, 925], [600, 760, 820], [720, 900, 980], [520, 610, 690]];
        for (int i = 0; i < caseFans.Length; i++)
            Add($"/lpc/it8665e/0/fan/{i + 1}", $"Fan #{i + 2}", "Fan", Board, "SuperIO", caseFans[i], 8);
        Add("/lpc/it8665e/0/fan/6", "Pump", "Fan", Board, "SuperIO", [2120, 2410, 2660], 10);

        // ── Storage: the NVMe system disk (C:) and a hard disk ──
        Add("/nvme/0/temperature/0", "Composite Temperature", "Temperature", Nvme, "Storage", [41, 46, 52], 0.4);
        Add("/nvme/0/data/32", "Total Space", "Data", Nvme, "Storage", [1024.21, 1024.21, 1024.21], 0);
        Add("/nvme/0/throughput/54", "Read Rate", "Throughput", Nvme, "Storage", [380e3, 36e6, 4.2e6], 0.25, relative: true);
        Add("/nvme/0/throughput/55", "Write Rate", "Throughput", Nvme, "Storage", [240e3, 3.1e6, 21e6], 0.25, relative: true);
        Add("/hdd/0/temperature/0", "Temperature", "Temperature", Hdd, "Storage", [37, 38, 38], 0.2);
        Add("/hdd/0/data/32", "Total Space", "Data", Hdd, "Storage", [5000.9, 5000.9, 5000.9], 0);

        // ── Network ──
        Add("/nic/%7BA%7D/data/3", "Data Downloaded", "Data", Nic, "Network", [42.6, 42.6, 42.6], 0);
        Add("/nic/%7BA%7D/throughput/8", "Download Speed", "Throughput", Nic, "Network", [62e3, 2.4e6, 175e3], 0.3, relative: true);
        Add("/nic/%7BA%7D/throughput/7", "Upload Speed", "Throughput", Nic, "Network", [21e3, 410e3, 38e3], 0.3, relative: true);

        // ── Built-in drive sensors (GiB, like Explorer) ──
        AddDrive('C', "Windows", 953.7, 412.3);
        AddDrive('D', "Games", 1863.0, 1121.6);
        AddDrive('E', "Media", 4657.4, 2380.2);
        AddDrive('F', "Backup", 931.5, 214.8);

        // ── FPS: only while gaming ──
        var fps = Scripted("fps", [140, 140, 140], 5, 0, 1000);
        bool Gaming(double t) => StateIndex(t) == 1;
        Sensor("fps", "Frame rate", "Framerate", Fps, "Fps", t => Gaming(t) ? fps(t) : null);
        Sensor("fps.low1", "1% low", "Framerate", Fps, "Fps", t => Gaming(t) ? fps(t) * 0.74 : null);
        Sensor("fps.frametime", "Frame time", "Timing", Fps, "Fps", t => Gaming(t) ? 1000 / fps(t) : null);
    }

    public static string StateAt(double t) => States[StateIndex(t)].Name;

    public DateTime ClockAt(double t) => Start.AddSeconds(t);

    /// <summary>Every mock sensor's value at simulated second <paramref name="t"/> (negative t is fine: pre-roll).</summary>
    public SensorCatalog CatalogAt(double t)
    {
        var list = new List<SensorInfo>(_specs.Count);
        foreach (var s in _specs)
            list.Add(new SensorInfo(s.Id, s.Name, s.Type, UnitFor(s), s.Hardware, s.HardwareType, s.Value(t)));
        return new SensorCatalog(list, ClockAt(t));
    }

    private static string UnitFor(Spec s) => s.HardwareType switch
    {
        "Drive" => s.Id.EndsWith(".usedPct", StringComparison.Ordinal) ? "%" : "GB",
        "Fps" => s.Type == "Timing" ? "ms" : "FPS",
        _ => SensorCatalog.UnitFor(s.Type),
    };

    private void Sensor(string id, string name, string type, string hw, string hwType, Func<double, double?> value) =>
        _specs.Add(new Spec(id, name, type, hw, hwType, value));

    private void Add(string id, string name, string type, string hw, string hwType, double[] targets, double noise,
        double max = double.MaxValue, bool relative = false)
    {
        double hi = type == "Load" ? 100 : max;
        var f = Scripted(id + "|" + name, targets, noise, 0, hi, relative);
        Sensor(id, name, type, hw, hwType, t => f(t));
    }

    private void AddDrive(char letter, string label, double totalGB, double usedGB)
    {
        var hw = $"Drive {letter}: ({label})";
        Sensor($"drive.{letter}.usedPct", "Used space", "Drive", hw, "Drive", _ => usedGB / totalGB * 100);
        Sensor($"drive.{letter}.usedGB", "Used", "Drive", hw, "Drive", _ => usedGB);
        Sensor($"drive.{letter}.freeGB", "Free", "Drive", hw, "Drive", _ => totalGB - usedGB);
        Sensor($"drive.{letter}.totalGB", "Total", "Drive", hw, "Drive", _ => totalGB);
    }

    /// <summary>
    /// Eases from the previous state's target to the current one over <see cref="EaseSeconds"/>, then adds noise
    /// (absolute, or a fraction of the eased value when <paramref name="relative"/>), clamped to [lo, hi].
    /// </summary>
    private static Func<double, double> Scripted(string seed, double[] targets, double noise, double lo, double hi,
        bool relative = false)
    {
        double phase = Hash(seed) % 10_000 / 10_000.0 * 2 * Math.PI;
        return t =>
        {
            double tm = Wrap(t);
            int s = StateIndex(tm);
            double into = tm - States[s].From;
            double x = Math.Clamp(into / EaseSeconds, 0, 1);
            double eased = targets[(s + 2) % 3] + (targets[s] - targets[(s + 2) % 3]) * x * x * (3 - 2 * x);
            double w = 2 * Math.PI * tm / LoopSeconds;
            double n = 0.55 * Math.Sin(3 * w + phase) + 0.3 * Math.Sin(7 * w + 1.7 * phase) + 0.15 * Math.Sin(13 * w + 2.3 * phase);
            double v = eased + n * (relative ? noise * eased : noise);
            return Math.Clamp(v, lo, hi);
        };
    }

    private static double Wrap(double t) => ((t % LoopSeconds) + LoopSeconds) % LoopSeconds;

    private static int StateIndex(double t) => Math.Min((int)(Wrap(t) / 10), 2);

    /// <summary>FNV-1a: stable across processes, unlike string.GetHashCode.</summary>
    private static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }
}
