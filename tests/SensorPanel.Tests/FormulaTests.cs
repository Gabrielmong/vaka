using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

public class FormulaTests
{
    private static readonly Dictionary<string, double?> Values = new()
    {
        ["cpu.power"] = 60, ["gpu.power"] = 24, ["fps"] = null, ["/amdcpu/0/power/0"] = 61, ["my-key"] = 5,
    };

    private static double? Eval(string text) =>
        Formula.Parse(text).Evaluate(k => Values.TryGetValue(k, out var v) ? v : null);

    [Theory]
    [InlineData("cpu.power + gpu.power + 50", 134)]
    [InlineData("(cpu.power + gpu.power + 50) / 0.8", 167.5)]
    [InlineData("2 + 3 * 4 - 1", 13)]
    [InlineData("-gpu.power + 30", 6)]
    [InlineData("[/amdcpu/0/power/0] - [my-key]", 56)]
    [InlineData("max(cpu.power, gpu.power, 70)", 70)]
    [InlineData("min(cpu.power, gpu.power)", 24)]
    [InlineData("abs(gpu.power - cpu.power)", 36)]
    [InlineData("fps ?? 0", 0)]
    [InlineData("fps ?? cpu.power ?? 1", 60)]
    [InlineData(" 1.5e0 ", double.NaN)] // exponents aren't supported -> parse error
    public void Evaluates(string text, double expected)
    {
        if (double.IsNaN(expected)) { Assert.Throws<FormatException>(() => Formula.Parse(text)); return; }
        Assert.Equal(expected, Eval(text)!.Value, 6);
    }

    [Theory]
    [InlineData("cpu.power + fps")]
    [InlineData("cpu.power / 0")]
    [InlineData("missing.key * 2")]
    [InlineData("max(fps, 1)")]
    public void MissingValuesGiveNull(string text) => Assert.Null(Eval(text));

    [Theory]
    [InlineData("")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("[unclosed")]
    [InlineData("foo(1)")]
    [InlineData("abs(1, 2)")]
    [InlineData("1 2")]
    [InlineData("cpu.power $ 2")]
    public void RejectsBadFormulas(string text)
    {
        Assert.False(Formula.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ListsKeys()
    {
        var f = Formula.Parse("(cpu.power + gpu.power) * [my-key] ?? fps");
        Assert.Equal(new[] { "cpu.power", "fps", "gpu.power", "my-key" }, f.Keys.Order());
    }

    private static PanelLayout Layout() => LayoutSerializer.Parse("""
        { "aliases": {
            "cpu.power": { "sensor": "/amdcpu/0/power/0" },
            "gpu.power": { "sensor": "/gpu-nvidia/0/power/0" },
            "system.power": { "expr": "(cpu.power + gpu.power + 50) / 0.88" },
            "loop.a": { "expr": "loop.b + 1" }, "loop.b": { "expr": "loop.a + 1" },
            "frames": { "expr": "fps ?? 0" } } }
        """);

    [Fact]
    public void ExpandsDependenciesForHardwareGroups()
    {
        var layout = Layout();
        var keys = Formula.ExpandKeys(layout, ["system.power"]);
        Assert.Contains("cpu.power", keys);
        Assert.Contains("gpu.power", keys);
        Assert.Equal(SensorGroups.Cpu | SensorGroups.Gpu, SensorGroupResolver.ForKeys(layout, ["system.power"]));
        Assert.Contains("loop.a", Formula.ExpandKeys(layout, ["loop.b"])); // cycles terminate
    }

    [Fact]
    public void CatalogResolvesFormulaAliases()
    {
        var catalog = new SensorCatalog(
            [
                new("/amdcpu/0/power/0", "Package", "Power", "W", "CPU", "Cpu", 60),
                new("/gpu-nvidia/0/power/0", "GPU Package", "Power", "W", "GPU", "GpuNvidia", 28),
            ], DateTime.Now);
        var layout = Layout();
        Assert.Equal(138 / 0.88, catalog.Resolve(layout, "system.power")!.Value, 6);
        Assert.Null(catalog.Resolve(layout, "loop.a"));
    }

    [Fact]
    public void HubResolvesFormulasOverBuiltins()
    {
        using var hub = new SensorHub(new FpsMonitor(() => null, () => 0, () => 0));
        hub.Open(SensorGroups.None);
        var layout = LayoutSerializer.Parse("""
            { "aliases": {
                "c.free": { "expr": "100 - drive.C.usedPct" },
                "frames": { "expr": "fps ?? -1" },
                "loop.a": { "expr": "loop.a + 1" } } }
            """);
        var values = hub.Poll(layout, new HashSet<string> { "c.free", "drive.C.usedPct", "frames", "loop.a" });
        Assert.Equal(100 - values["drive.C.usedPct"]!.Value, values["c.free"]!.Value, 6);
        Assert.Equal(-1, values["frames"]);
        Assert.Null(values["loop.a"]);
        Assert.True(hub.Fps.Wanted); // fps used only inside a formula still starts PresentMon
    }

    [Fact]
    public void ExprRoundTrips()
    {
        var again = LayoutSerializer.Parse(LayoutSerializer.Serialize(Layout()));
        Assert.Equal("(cpu.power + gpu.power + 50) / 0.88", again.Aliases["system.power"].Expr);
        Assert.Null(again.Aliases["cpu.power"].Expr);
    }
}
