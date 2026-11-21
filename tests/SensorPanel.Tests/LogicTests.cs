using System.Drawing;
using System.Globalization;
using SensorPanel.Display;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SkiaSharp;

namespace SensorPanel.Tests;

public class TemplateTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly DateTime Now = new(2026, 11, 20, 15, 22, 7);

    private static string Fmt(string template, Dictionary<string, double?> values) =>
        Template.Parse(template).Format(k => values.TryGetValue(k, out var v) ? v : null, Now, Inv);

    [Fact]
    public void Formats_value_with_format_string() =>
        Assert.Equal("76°C", Fmt("{cpu.temp:0}°C", new() { ["cpu.temp"] = 75.9 }));

    [Fact]
    public void Default_format_trims_decimals() =>
        Assert.Equal("4.13 GHz", Fmt("{cpu.clock} GHz", new() { ["cpu.clock"] = 4.1250001 }));

    [Fact]
    public void Missing_and_nan_values_render_dashes()
    {
        Assert.Equal("-- / --", Fmt("{a} / {b}", new() { ["b"] = double.NaN }));
    }

    [Fact]
    public void Time_and_date_use_clock()
    {
        Assert.Equal("15:22", Fmt("{time:HH:mm}", new()));
        Assert.Equal("11/20/26", Fmt("{date:MM/dd/yy}", new()));
    }

    [Fact]
    public void Escaped_braces_and_unclosed_brace_are_literal()
    {
        Assert.Equal("{x} 5", Fmt("{{x}} {v}", new() { ["v"] = 5 }));
        Assert.Equal("a {b", Fmt("a {b", new()));
    }

    [Fact]
    public void Keys_exclude_clock_and_are_distinct() =>
        Assert.Equal(new[] { "cpu.temp", "gpu.temp" },
            Template.Parse("{time} {cpu.temp:0} {gpu.temp} {cpu.temp}").Keys);
}

public class ThresholdTests
{
    private static readonly List<Threshold> T = new()
    {
        new() { Gte = 90, Color = "red" },
        new() { Gte = 80, Color = "orange" },
    };

    [Theory]
    [InlineData(50, "white")]
    [InlineData(80, "orange")]
    [InlineData(89.9, "orange")]
    [InlineData(95, "red")]
    public void Highest_matching_threshold_wins_regardless_of_order(double v, string expected) =>
        Assert.Equal(expected, Thresholds.Resolve("white", T, v));

    [Fact]
    public void Unknown_value_keeps_base_color() =>
        Assert.Equal("white", Thresholds.Resolve("white", T, null));

    [Theory]
    [InlineData(50, 0, 100, 0.5f)]
    [InlineData(-5, 0, 100, 0f)]
    [InlineData(150, 0, 100, 1f)]
    [InlineData(3, 2, 4, 0.5f)]
    public void Fraction_is_clamped(double v, double min, double max, float expected) =>
        Assert.Equal(expected, Thresholds.Fraction(v, min, max), 3);

    [Fact]
    public void Fraction_handles_bad_range_and_null()
    {
        Assert.Equal(0f, Thresholds.Fraction(5, 10, 10));
        Assert.Equal(0f, Thresholds.Fraction(null, 0, 100));
    }
}

public class ColorParserTests
{
    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("#1b1b1b", 0x1b, 0x1b, 0x1b, 255)]
    [InlineData("#ff000080", 255, 0, 0, 0x80)]
    [InlineData("00ff00", 0, 255, 0, 255)]
    public void Parses_css_hex(string s, byte r, byte g, byte b, byte a)
    {
        Assert.True(ColorParser.TryParse(s, out var c));
        Assert.Equal(new SKColor(r, g, b, a), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#zzzzzz")]
    public void Rejects_invalid(string? s) => Assert.False(ColorParser.TryParse(s, out _));
}

public class ScaleTransformTests
{
    [Fact]
    public void Same_size_is_identity() =>
        Assert.Equal(new ScaleResult(1, 1, 0, 0), ScaleTransform.Compute(800, 480, 800, 480, ScaleMode.Fit));

    [Fact]
    public void Fit_letterboxes_wider_target()
    {
        // 800x480 into 1920x480 → scale 1, centred horizontally.
        Assert.Equal(new ScaleResult(1, 1, 560, 0), ScaleTransform.Compute(800, 480, 1920, 480, ScaleMode.Fit));
    }

    [Fact]
    public void Fill_crops_and_stretch_distorts()
    {
        AssertClose(new ScaleResult(2.4f, 2.4f, 0, -336), ScaleTransform.Compute(800, 480, 1920, 480, ScaleMode.Fill));
        AssertClose(new ScaleResult(2.4f, 1, 0, 0), ScaleTransform.Compute(800, 480, 1920, 480, ScaleMode.Stretch));
    }

    private static void AssertClose(ScaleResult expected, ScaleResult actual)
    {
        Assert.Equal(expected.ScaleX, actual.ScaleX, 3);
        Assert.Equal(expected.ScaleY, actual.ScaleY, 3);
        Assert.Equal(expected.OffsetX, actual.OffsetX, 2);
        Assert.Equal(expected.OffsetY, actual.OffsetY, 2);
    }

    [Fact]
    public void Fit_scales_up_uniformly() =>
        Assert.Equal(new ScaleResult(2, 2, 0, 0), ScaleTransform.Compute(800, 480, 1600, 960, ScaleMode.Fit));
}

public class MonitorMatcherTests
{
    private const string PanelId = @"\\?\DISPLAY#ABC0800#5&1a2b&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string MainId = @"\\?\DISPLAY#GSM5B7F#5&9f9f&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static MonitorInfo Mon(string id, int w, int h, bool primary, string dev = @"\\.\DISPLAY1") =>
        new(dev, id, "Monitor", new Rectangle(0, 0, w, h), primary);

    private static readonly MonitorInfo Main = Mon(MainId, 2560, 1440, true);
    private static readonly MonitorInfo Panel = Mon(PanelId, 800, 480, false, @"\\.\DISPLAY3");

    [Fact]
    public void Extracts_hardware_id()
    {
        Assert.Equal("ABC0800", MonitorMatcher.HardwareId(PanelId));
        Assert.Null(MonitorMatcher.HardwareId(@"\\.\DISPLAY1"));
        Assert.Null(MonitorMatcher.HardwareId(null));
    }

    [Fact]
    public void Exact_id_wins() =>
        Assert.Same(Panel, MonitorMatcher.Select(new[] { Main, Panel }, PanelId, 800, 480, 800, 480));

    [Fact]
    public void Same_model_on_different_port_is_found()
    {
        var moved = Mon(PanelId.Replace("UID4353", "UID999"), 800, 480, false);
        Assert.Same(moved, MonitorMatcher.Select(new[] { Main, moved }, PanelId, 800, 480, 800, 480));
    }

    [Fact]
    public void Falls_back_to_saved_resolution_on_non_primary()
    {
        var other = Mon(@"\\?\DISPLAY#XYZ1234#1#{g}", 800, 480, false);
        Assert.Same(other, MonitorMatcher.Select(new[] { Main, other }, PanelId, 800, 480, 800, 480));
    }

    [Fact]
    public void Missing_panel_never_falls_back_to_primary()
    {
        var mainAt800 = Mon(MainId, 800, 480, true);
        Assert.Null(MonitorMatcher.Select(new[] { mainAt800 }, PanelId, 800, 480, 800, 480));
    }

    [Fact]
    public void First_run_picks_unique_non_primary_matching_canvas()
    {
        Assert.Same(Panel, MonitorMatcher.Select(new[] { Main, Panel }, null, 0, 0, 800, 480));
        Assert.Null(MonitorMatcher.Select(new[] { Main }, null, 0, 0, 800, 480));
    }
}

public class SensorGroupTests
{
    [Theory]
    [InlineData("/amdcpu/0/temperature/2", SensorPanel.Sensors.SensorGroups.Cpu)]
    [InlineData("/gpu-nvidia/0/load/0", SensorPanel.Sensors.SensorGroups.Gpu)]
    [InlineData("/ram/data/0", SensorPanel.Sensors.SensorGroups.Memory)]
    [InlineData("/lpc/it8665e/0/fan/0", SensorPanel.Sensors.SensorGroups.Motherboard)]
    [InlineData("/nvme/2/temperature/0", SensorPanel.Sensors.SensorGroups.Storage)]
    [InlineData("/nic/%7BAF4A%7D/throughput/7", SensorPanel.Sensors.SensorGroups.Network)]
    [InlineData("drive.C.usedPct", SensorPanel.Sensors.SensorGroups.None)]
    public void Maps_identifier_to_group(string id, SensorPanel.Sensors.SensorGroups expected) =>
        Assert.Equal(expected, SensorPanel.Sensors.SensorGroupResolver.ForIdentifier(id));

    [Fact]
    public void Default_layout_does_not_need_storage_or_network()
    {
        var layout = LayoutSerializer.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default.json")));
        var groups = SensorPanel.Sensors.SensorGroupResolver.ForKeys(layout, LayoutSerializer.GetSensorKeys(layout));
        Assert.Equal(SensorPanel.Sensors.SensorGroups.Cpu | SensorPanel.Sensors.SensorGroups.Gpu |
                     SensorPanel.Sensors.SensorGroups.Memory | SensorPanel.Sensors.SensorGroups.Motherboard, groups);
    }
}

public class LayoutTests
{
    private static PanelLayout LoadDefault() =>
        LayoutSerializer.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default.json")));

    [Fact]
    public void Default_layout_parses()
    {
        var layout = LoadDefault();
        Assert.Equal(800, layout.Canvas.Width);
        Assert.Equal(480, layout.Canvas.Height);
        Assert.NotEmpty(layout.Static);
        Assert.Contains(layout.Widgets, w => w is RingElement { Value: "cpu.temp" });
    }

    [Fact]
    public void Every_default_sensor_key_resolves_to_alias_or_builtin()
    {
        var layout = LoadDefault();
        foreach (var key in LayoutSerializer.GetSensorKeys(layout))
            Assert.True(layout.Aliases.ContainsKey(key) || key.StartsWith("drive.") || key.StartsWith('/'),
                $"Unresolved sensor key '{key}'");
    }

    [Fact]
    public void Round_trips_polymorphic_elements()
    {
        var layout = LoadDefault();
        var again = LayoutSerializer.Parse(LayoutSerializer.Serialize(layout));
        Assert.Equal(layout.Widgets.Count, again.Widgets.Count);
        Assert.Equal(layout.Widgets.Select(w => w.GetType()), again.Widgets.Select(w => w.GetType()));
        Assert.Contains("\"type\": \"ring\"", LayoutSerializer.Serialize(layout));
    }

    [Fact]
    public void Type_discriminator_may_appear_after_other_properties()
    {
        var layout = LayoutSerializer.Parse("""{ "widgets": [ { "x": 5, "type": "bar", "value": "a" } ] }""");
        var bar = Assert.IsType<BarElement>(Assert.Single(layout.Widgets));
        Assert.Equal(5, bar.X);
    }

    [Fact]
    public void Rejects_invalid_canvas() =>
        Assert.ThrowsAny<Exception>(() => LayoutSerializer.Parse("""{ "canvas": { "width": 0, "height": 10 } }"""));
}
