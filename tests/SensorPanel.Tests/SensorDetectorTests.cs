using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

/// <summary>Sensor names below are copied from real LibreHardwareMonitor 0.9.6 output.</summary>
public class SensorDetectorTests
{
    internal static SensorInfo S(string id, string name, string type, string hw, string hwType, double? value) =>
        new(id, name, type, SensorCatalog.UnitFor(type), hw, hwType, value);

    private static readonly char[] Drives = ['C', 'D'];

    /// <summary>The owner's PC: Ryzen 7 3800X, RTX 3070, ITE IT8665E, NVMe system disk.</summary>
    internal static List<SensorInfo> AmdNvidia() =>
    [
        S("/amdcpu/0/power/0", "Package", "Power", "AMD Ryzen 7 3800X", "Cpu", 56.6),
        S("/amdcpu/0/power/1", "Core #1 (SMU)", "Power", "AMD Ryzen 7 3800X", "Cpu", 6),
        S("/amdcpu/0/clock/1", "Cores (Average)", "Clock", "AMD Ryzen 7 3800X", "Cpu", 4250),
        S("/amdcpu/0/clock/6", "Core #1", "Clock", "AMD Ryzen 7 3800X", "Cpu", 4250),
        S("/amdcpu/0/temperature/3", "SoC", "Temperature", "AMD Ryzen 7 3800X", "Cpu", 41),
        S("/amdcpu/0/temperature/4", "CCD1 (Tdie)", "Temperature", "AMD Ryzen 7 3800X", "Cpu", 68.5),
        S("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "Temperature", "AMD Ryzen 7 3800X", "Cpu", 69.75),
        S("/amdcpu/0/load/0", "CPU Total", "Load", "AMD Ryzen 7 3800X", "Cpu", 29),
        S("/amdcpu/0/load/1", "CPU Core Max", "Load", "AMD Ryzen 7 3800X", "Cpu", 58),
        .. Enumerable.Range(1, 16).Select(i =>
            S($"/amdcpu/0/load/{i + 1}", $"CPU Core #{i}", "Load", "AMD Ryzen 7 3800X", "Cpu", 10 + i)),
        S("/ram/load/0", "Memory", "Load", "Total Memory", "Memory", 70.8),
        S("/ram/data/0", "Memory Used", "Data", "Total Memory", "Memory", 22.6),
        S("/ram/data/1", "Memory Available", "Data", "Total Memory", "Memory", 9.3),
        S("/vram/data/2", "Memory Used", "Data", "Virtual Memory", "Memory", 38.8),
        S("/gpu-nvidia/0/power/0", "GPU Package", "Power", "NVIDIA GeForce RTX 3070", "GpuNvidia", 22),
        S("/gpu-nvidia/0/clock/0", "GPU Core", "Clock", "NVIDIA GeForce RTX 3070", "GpuNvidia", 210),
        S("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "NVIDIA GeForce RTX 3070", "GpuNvidia", 42),
        S("/gpu-nvidia/0/temperature/2", "GPU Hot Spot", "Temperature", "NVIDIA GeForce RTX 3070", "GpuNvidia", 53.4),
        S("/gpu-nvidia/0/load/0", "GPU Core", "Load", "NVIDIA GeForce RTX 3070", "GpuNvidia", 19),
        S("/gpu-nvidia/0/load/3", "GPU Bus", "Load", "NVIDIA GeForce RTX 3070", "GpuNvidia", 2),
        S("/gpu-nvidia/0/load/3", "GPU Memory", "Load", "NVIDIA GeForce RTX 3070", "GpuNvidia", 28),
        S("/gpu-nvidia/0/fan/1", "GPU Fan 1", "Fan", "NVIDIA GeForce RTX 3070", "GpuNvidia", 0),
        S("/gpu-nvidia/0/control/1", "GPU Fan 1", "Control", "NVIDIA GeForce RTX 3070", "GpuNvidia", 0),
        S("/gpu-nvidia/0/smalldata/1", "GPU Memory Used", "SmallData", "NVIDIA GeForce RTX 3070", "GpuNvidia", 2293),
        S("/gpu-nvidia/0/smalldata/2", "GPU Memory Total", "SmallData", "NVIDIA GeForce RTX 3070", "GpuNvidia", 8192),
        S("/lpc/it8665e/0/temperature/0", "Temperature #1", "Temperature", "ITE IT8665E", "SuperIO", 69),
        S("/lpc/it8665e/0/fan/0", "Fan #1", "Fan", "ITE IT8665E", "SuperIO", 1121),
        S("/lpc/it8665e/0/fan/1", "Fan #2", "Fan", "ITE IT8665E", "SuperIO", 900),
        S("/lpc/it8665e/0/fan/2", "Fan #3", "Fan", "ITE IT8665E", "SuperIO", 0),
        S("/lpc/it8665e/0/fan/3", "Fan #4", "Fan", "ITE IT8665E", "SuperIO", 800),
        S("/lpc/it8665e/0/fan/4", "Fan #5", "Fan", "ITE IT8665E", "SuperIO", 0),
        S("/lpc/it8665e/0/fan/5", "Fan #6", "Fan", "ITE IT8665E", "SuperIO", 4891),
        S("/hdd/0/temperature/0", "Temperature", "Temperature", "ST5000DM000", "Storage", 47),
        S("/hdd/0/data/32", "Total Space", "Data", "ST5000DM000", "Storage", 5000.9),
        S("/nvme/2/temperature/0", "Composite Temperature", "Temperature", "XPG GAMMIX S50 Lite", "Storage", 49),
        S("/nvme/2/data/32", "Total Space", "Data", "XPG GAMMIX S50 Lite", "Storage", 1024.21),
        S("/nvme/2/throughput/54", "Read Rate", "Throughput", "XPG GAMMIX S50 Lite", "Storage", 903744),
        S("/nvme/2/throughput/55", "Write Rate", "Throughput", "XPG GAMMIX S50 Lite", "Storage", 476894),
        S("/nic/%7BA%7D/data/3", "Data Downloaded", "Data", "Ethernet", "Network", 5.5),
        S("/nic/%7BA%7D/throughput/7", "Upload Speed", "Throughput", "Ethernet", "Network", 11660),
        S("/nic/%7BA%7D/throughput/8", "Download Speed", "Throughput", "Ethernet", "Network", 4724),
        S("/nic/%7BB%7D/data/3", "Data Downloaded", "Data", "Ethernet 2", "Network", 0),
        S("/nic/%7BB%7D/throughput/8", "Download Speed", "Throughput", "Ethernet 2", "Network", 0),
        S("drive.C.totalGB", "Total", "Drive", "Drive C:", "Drive", 952.89),
        S("drive.D.totalGB", "Total", "Drive", "Drive D:", "Drive", 1863.0),
    ];

    internal static SensorCatalog Catalog(IEnumerable<SensorInfo> sensors) => new(sensors.ToList(), DateTime.Now);

    private static Detection Detect(IEnumerable<SensorInfo> sensors, PanelLayout? existing = null) =>
        SensorDetector.Detect(Catalog(sensors), Drives, existing);

    [Fact]
    public void AmdNvidiaDesktop()
    {
        var d = Detect(AmdNvidia());
        var a = d.Aliases;
        Assert.Equal("/amdcpu/0/temperature/2", a["cpu.temp"].Sensor);
        Assert.Equal("/amdcpu/0/load/0", a["cpu.load"].Sensor);
        Assert.Equal("/amdcpu/0/clock/1", a["cpu.clock"].Sensor);
        Assert.Equal(0.001, a["cpu.clock"].Scale);
        Assert.Equal("/amdcpu/0/power/0", a["cpu.power"].Sensor);
        Assert.Equal("/amdcpu/0/load/2", a["cpu.core1"].Sensor);
        Assert.Equal("/amdcpu/0/load/9", a["cpu.core8"].Sensor);

        Assert.Equal("/gpu-nvidia/0/temperature/0", a["gpu.temp"].Sensor);
        Assert.Equal("/gpu-nvidia/0/temperature/2", a["gpu.hotspot"].Sensor);
        Assert.Equal(0.001, a["gpu.clock"].Scale);
        Assert.Equal("/gpu-nvidia/0/power/0", a["gpu.power"].Sensor);
        Assert.Equal(("/gpu-nvidia/0/load/3", "GPU Memory"), (a["vram.load"].Sensor, a["vram.load"].Name)); // duplicate id
        Assert.Equal(1 / 1024.0, a["vram.used"].Scale, 9);
        Assert.Equal("/gpu-nvidia/0/smalldata/2", a["vram.total"].Sensor);

        Assert.Equal("/ram/data/0", a["ram.used"].Sensor); // Total Memory, not Virtual Memory
        Assert.Equal("/ram/data/1", a["ram.free"].Sensor);
        Assert.Equal("/ram/load/0", a["ram.load"].Sensor);

        Assert.Equal("/lpc/it8665e/0/fan/0", a["fan.1"].Sensor);
        Assert.Equal("/lpc/it8665e/0/fan/1", a["fan.2"].Sensor);
        Assert.Equal("/lpc/it8665e/0/fan/3", a["fan.3"].Sensor);
        Assert.Equal("/lpc/it8665e/0/fan/5", a["fan.4"].Sensor);
        Assert.Contains("fan.5", d.Missing);
        Assert.Equal(4, d.Summary.Fans);

        // System disk matched by size to drive C (952.89 GiB ≈ 1023 GB), not the lowest-numbered disk.
        Assert.Equal("/nvme/2/temperature/0", a["disk.temp"].Sensor);
        Assert.Equal(1 / 1048576.0, a["disk.read"].Scale, 12);
        Assert.Equal("/nic/%7BA%7D/throughput/8", a["net.down"].Sensor);
        Assert.Equal("/nic/%7BA%7D/throughput/7", a["net.up"].Sensor);

        Assert.Contains("board.temp", d.Missing); // "Temperature #1" is ambiguous — better -- than wrong
        Assert.Equal("(cpu.power + gpu.power + 50) / 0.88", a["system.power"].Expr);
        Assert.Equal("AMD Ryzen 7 3800X", d.Summary.Cpu);
        Assert.Equal("NVIDIA GeForce RTX 3070", d.Summary.Gpu);
        Assert.Equal(Drives, d.Summary.Drives);
    }

    [Fact]
    public void IntelIgpuOnly()
    {
        var d = Detect(
        [
            S("/intelcpu/0/temperature/8", "Core Max", "Temperature", "Intel Core i5-12400", "Cpu", 50),
            S("/intelcpu/0/temperature/7", "CPU Package", "Temperature", "Intel Core i5-12400", "Cpu", 52),
            S("/intelcpu/0/power/0", "CPU Package", "Power", "Intel Core i5-12400", "Cpu", 30),
            S("/intelcpu/0/load/1", "CPU Core #1 Thread #1", "Load", "Intel Core i5-12400", "Cpu", 20),
            S("/intelcpu/0/load/2", "CPU Core #1 Thread #2", "Load", "Intel Core i5-12400", "Cpu", 25),
            S("/intelcpu/0/clock/1", "Core #1", "Clock", "Intel Core i5-12400", "Cpu", 4000),
            S("/gpu-intel-integrated/0/load/0", "D3D 3D", "Load", "Intel UHD Graphics 730", "GpuIntel", 3),
            S("/gpu-intel-integrated/0/power/0", "GPU Power", "Power", "Intel UHD Graphics 730", "GpuIntel", 2),
        ]);
        Assert.Equal("/intelcpu/0/temperature/7", d.Aliases["cpu.temp"].Sensor);
        Assert.Equal("/intelcpu/0/power/0", d.Aliases["cpu.power"].Sensor);
        Assert.Equal("/intelcpu/0/load/1", d.Aliases["cpu.core1"].Sensor);
        Assert.Equal("/intelcpu/0/clock/1", d.Aliases["cpu.clock"].Sensor);
        Assert.Equal("/gpu-intel-integrated/0/power/0", d.Aliases["gpu.power"].Sensor);
        Assert.Contains("gpu.temp", d.Missing);
        Assert.Contains("vram.used", d.Missing);
        Assert.Contains("cpu.core2", d.Missing);
        Assert.Equal("Intel UHD Graphics 730", d.Summary.Gpu);
    }

    [Fact]
    public void PrefersDiscreteGpuEvenWhenAsleep()
    {
        var d = Detect(
        [
            S("/gpu-intel-integrated/0/power/0", "GPU Power", "Power", "Intel Iris Xe", "GpuIntel", 3),
            S("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "NVIDIA GeForce RTX 4060 Laptop GPU", "GpuNvidia", null),
            S("/gpu-nvidia/0/power/0", "GPU Package", "Power", "NVIDIA GeForce RTX 4060 Laptop GPU", "GpuNvidia", null),
        ]);
        Assert.Equal("/gpu-nvidia/0/power/0", d.Aliases["gpu.power"].Sensor);
        Assert.Equal("/gpu-nvidia/0/temperature/0", d.Aliases["gpu.temp"].Sensor);
    }

    [Fact]
    public void AmdApuLosesToSleepingNvidia()
    {
        // Ryzen laptop: the APU's iGPU is also "GpuAmd" and reports a memory carve-out; the dGPU sleeps (no values).
        var d = Detect(
        [
            S("/gpu-amd/0/temperature/0", "GPU Core", "Temperature", "AMD Radeon(TM) Graphics", "GpuAmd", 50),
            S("/gpu-amd/0/smalldata/1", "GPU Memory Total", "SmallData", "AMD Radeon(TM) Graphics", "GpuAmd", 512),
            S("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "NVIDIA GeForce RTX 4060 Laptop GPU", "GpuNvidia", null),
        ]);
        Assert.Equal("/gpu-nvidia/0/temperature/0", d.Aliases["gpu.temp"].Sensor);
    }

    [Fact]
    public void IgpuPlusAmdDgpu()
    {
        var d = Detect(
        [
            S("/gpu-intel-integrated/0/temperature/0", "GPU Core", "Temperature", "Intel UHD 770", "GpuIntel", 40),
            S("/gpu-amd/0/temperature/0", "GPU Core", "Temperature", "AMD Radeon RX 7800 XT", "GpuAmd", 45),
            S("/gpu-amd/0/temperature/1", "GPU Hot Spot", "Temperature", "AMD Radeon RX 7800 XT", "GpuAmd", 60),
            S("/gpu-amd/0/power/0", "GPU Package", "Power", "AMD Radeon RX 7800 XT", "GpuAmd", 40),
        ]);
        Assert.Equal("/gpu-amd/0/temperature/0", d.Aliases["gpu.temp"].Sensor);
        Assert.Equal("/gpu-amd/0/temperature/1", d.Aliases["gpu.hotspot"].Sensor);
        Assert.Equal("AMD Radeon RX 7800 XT", d.Summary.Gpu);
    }

    [Fact]
    public void LargestDiscreteGpuWins()
    {
        var d = Detect(
        [
            S("/gpu-nvidia/0/smalldata/2", "GPU Memory Total", "SmallData", "GTX 1050", "GpuNvidia", 2048),
            S("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "GTX 1050", "GpuNvidia", 40),
            S("/gpu-nvidia/1/smalldata/2", "GPU Memory Total", "SmallData", "RTX 4090", "GpuNvidia", 24576),
            S("/gpu-nvidia/1/temperature/0", "GPU Core", "Temperature", "RTX 4090", "GpuNvidia", 50),
        ]);
        Assert.Equal("/gpu-nvidia/1/temperature/0", d.Aliases["gpu.temp"].Sensor);
    }

    [Fact]
    public void GpuFanDetectedAtZeroRpm()
    {
        var d = Detect(AmdNvidia());
        Assert.Equal("/gpu-nvidia/0/fan/1", d.Aliases["gpu.fan"].Sensor);
    }

    [Fact]
    public void LaptopNoFans()
    {
        var d = Detect(AmdNvidia().Where(s => s.HardwareType != "SuperIO"));
        foreach (var k in new[] { "fan.1", "fan.6", "pump" }) Assert.Contains(k, d.Missing);
        Assert.Equal(0, d.Summary.Fans);
    }

    [Fact]
    public void NoNetwork()
    {
        var d = Detect(AmdNvidia().Where(s => s.HardwareType != "Network"));
        Assert.Contains("net.down", d.Missing);
        Assert.Contains("net.up", d.Missing);
    }

    [Fact]
    public void EmptyCatalogStillGivesSystemPower()
    {
        var d = Detect([]);
        Assert.Equal(SensorDetector.Vocabulary.Count, d.Missing.Count);
        Assert.True(d.Aliases.ContainsKey("system.power"));
        Assert.Null(d.Summary.Cpu);
    }

    [Fact]
    public void BoardTempOnlyWhenNamed()
    {
        var d = Detect([.. AmdNvidia(), S("/lpc/nct6798d/0/temperature/1", "Motherboard", "Temperature", "Nuvoton NCT6798D", "SuperIO", 33)]);
        Assert.Equal("/lpc/nct6798d/0/temperature/1", d.Aliases["board.temp"].Sensor);
    }

    [Fact]
    public void PumpFromRename()
    {
        var existing = new PanelLayout { SensorNames = { ["/lpc/it8665e/0/fan/5"] = "AIO Pump" } };
        var d = Detect(AmdNvidia(), existing);
        Assert.Equal("/lpc/it8665e/0/fan/5", d.Aliases["pump"].Sensor);
        Assert.DoesNotContain(d.Aliases, kv => kv.Key.StartsWith("fan.") && kv.Value.Sensor == "/lpc/it8665e/0/fan/5");
        Assert.Equal(4, d.Summary.Fans); // 3 fans + pump
    }

    [Fact]
    public void MergeKeepsSameTypeUserAlias()
    {
        var existing = new PanelLayout
        {
            Aliases =
            {
                ["cpu.temp"] = new SensorAlias { Sensor = "/amdcpu/0/temperature/4" },   // CCD1, also a Temperature
                ["gpu.clock"] = new SensorAlias { Sensor = "/gpu-nvidia/0/clock/0" },    // MHz, scale 1
            },
        };
        var d = Detect(AmdNvidia(), existing);
        Assert.Equal("/amdcpu/0/temperature/4", d.Aliases["cpu.temp"].Sensor);
        Assert.Equal("/gpu-nvidia/0/clock/0", d.Aliases["gpu.clock"].Sensor);
        Assert.Equal(0.001, d.Aliases["gpu.clock"].Scale);
    }

    [Theory]
    [InlineData("/amdcpu/0/load/0")]       // wrong type (Load)
    [InlineData("/not/on/this/pc")]        // not in catalog
    public void MergeReplacesWrongTypeOrMissing(string userSensor)
    {
        var existing = new PanelLayout { Aliases = { ["cpu.temp"] = new SensorAlias { Sensor = userSensor } } };
        Assert.Equal("/amdcpu/0/temperature/2", Detect(AmdNvidia(), existing).Aliases["cpu.temp"].Sensor);
    }

    [Fact]
    public void MergeKeepsUserFormula()
    {
        var existing = new PanelLayout { Aliases = { ["system.power"] = new SensorAlias { Expr = "cpu.power + 100" } } };
        Assert.Equal("cpu.power + 100", Detect(AmdNvidia(), existing).Aliases["system.power"].Expr);
    }

    [Fact]
    public void MergeFillsKeyDetectionMissed()
    {
        // The user pointed board.temp at a sensor by hand; detection can't name one, so theirs stays.
        var existing = new PanelLayout { Aliases = { ["board.temp"] = new SensorAlias { Sensor = "/lpc/it8665e/0/temperature/0" } } };
        var d = Detect(AmdNvidia(), existing);
        Assert.Equal("/lpc/it8665e/0/temperature/0", d.Aliases["board.temp"].Sensor);
        Assert.DoesNotContain("board.temp", d.Missing);
    }
}
