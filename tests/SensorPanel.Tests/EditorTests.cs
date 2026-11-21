using SensorPanel.Editor;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

public class EditorSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1:8737", true)]
    [InlineData("localhost:8737", true)]
    [InlineData("127.0.0.1:8738", false)]
    [InlineData("evil.com:8737", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData(null, false)]
    public void Host(string? host, bool ok) => Assert.Equal(ok, EditorSecurity.IsAllowedHost(host, 8737));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://127.0.0.1:8737", true)]
    [InlineData("http://localhost:8737", true)]
    [InlineData("http://127.0.0.1:9999", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("null", false)]
    public void Origin(string? origin, bool ok) => Assert.Equal(ok, EditorSecurity.IsAllowedOrigin(origin, 8737));

    [Fact]
    public void Token()
    {
        var t = EditorSecurity.NewToken();
        Assert.Equal(48, t.Length);
        Assert.NotEqual(t, EditorSecurity.NewToken());
        Assert.True(EditorSecurity.TokenMatches(t, t));
        Assert.False(EditorSecurity.TokenMatches(t[..^1], t));
        Assert.False(EditorSecurity.TokenMatches(null, t));
        Assert.False(EditorSecurity.TokenMatches("", t));
    }
}

public class SensorCatalogTests
{
    private static SensorCatalog Catalog() => new(
    [
        new("/gpu-nvidia/0/load/3", "GPU Bus", "Load", "%", "RTX 3070", "GpuNvidia", 2),
        new("/gpu-nvidia/0/load/3", "GPU Memory", "Load", "%", "RTX 3070", "GpuNvidia", 41),
        new("/amdcpu/0/clock/1", "Core #1", "Clock", "MHz", "Ryzen", "Cpu", 4200),
        new("drive.C.usedPct", "Used", "Load", "%", "Drive C:", "Drive", 96),
    ], DateTime.Now);

    private static PanelLayout Layout() => new()
    {
        Aliases =
        {
            ["vram.load"] = new SensorAlias { Sensor = "/gpu-nvidia/0/load/3", Name = "GPU Memory" },
            ["gpu.bus"] = new SensorAlias { Sensor = "/gpu-nvidia/0/load/3" },
            ["cpu.clock"] = new SensorAlias { Sensor = "/amdcpu/0/clock/1", Scale = 0.001, Offset = 0.5 },
            ["gone"] = new SensorAlias { Sensor = "/nope/0" },
        },
    };

    [Fact] public void AliasNameDisambiguates() => Assert.Equal(41, Catalog().Resolve(Layout(), "vram.load"));
    [Fact] public void AliasWithoutNameTakesFirst() => Assert.Equal(2, Catalog().Resolve(Layout(), "gpu.bus"));
    [Fact] public void AliasScaleOffset() => Assert.Equal(4.7, Catalog().Resolve(Layout(), "cpu.clock")!.Value, 6);
    [Fact] public void RawId() => Assert.Equal(4200, Catalog().Resolve(Layout(), "/amdcpu/0/clock/1"));
    [Fact] public void Builtin() => Assert.Equal(96, Catalog().Resolve(Layout(), "drive.C.usedPct"));
    [Fact] public void Missing() => Assert.Null(Catalog().Resolve(Layout(), "gone"));
    [Fact] public void UnknownNameFallsBack() => Assert.Equal(2, Catalog().Find("/gpu-nvidia/0/load/3", "nope")!.Value);
    [Fact] public void CleanNaN() => Assert.Null(SensorCatalog.Clean(double.NaN));
}

public class MeasureBoundsTests
{
    [Fact]
    public void ShapesLinesAndText()
    {
        var layout = new PanelLayout
        {
            Static =
            {
                new RectElement { X = 10, Y = 20, W = 100, H = 50 },
                new LineElement { X = 0, Y = 10, X2 = 100, Y2 = 10, Width = 4 },
            },
            Widgets =
            {
                new TextElement { X = 300, Y = 100, W = 200, Text = "Hello", Size = 20, Align = TextAlign.Right },
                new TextElement { X = 300, Y = 100, Text = "Hello", Size = 20 },
            },
        };
        using var r = new PanelRenderer();
        var (s, w) = r.MeasureBounds(layout, _ => null, DateTime.Now);

        Assert.Equal([10f, 20f, 100f, 50f], s[0]!);
        Assert.Equal([-2f, 8f, 104f, 4f], s[1]!); // inflated by width/2 on every side

        var boxed = w[0]!;
        Assert.Equal(300f, boxed[0]);
        Assert.Equal(200f, boxed[2]); // W > 0: spans the box
        Assert.True(boxed[3] > 10);

        var auto = w[1]!;
        Assert.Equal(300f, auto[0], 1);
        Assert.InRange(auto[2], 20f, 120f); // measured "Hello" at 20px
    }
}
