using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

public class LayoutTemplateTests
{
    private static readonly HashSet<string> Fonts =
        ["Segoe UI", "Segoe UI Variable", "Bahnschrift", "Consolas", "Cascadia Mono"];

    private static readonly HashSet<string> Builtins = ["time", "date", "fps", "fps.low1", "fps.frametime", "system.power"];

    public static TheoryData<string> Ids() => new(TemplateLibrary.All.Select(t => t.Id));

    [Fact]
    public void ManifestMatchesFiles()
    {
        var ids = TemplateLibrary.All.Select(t => t.Id).ToList();
        Assert.Equal(12, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var t in TemplateLibrary.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.NotEmpty(t.Shows);
        }
        var resources = typeof(TemplateLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("templates/", StringComparison.Ordinal) && n != "templates/manifest.json")
            .Select(n => n["templates/".Length..^".json".Length]).Order().ToList();
        Assert.Equal(ids.Order().ToList(), resources);
        Assert.Throws<KeyNotFoundException>(() => TemplateLibrary.LoadRaw("nope"));
    }

    [Fact]
    public void TemplateNamesAreValidLayoutNames()
    {
        // The gallery suggests the template's name for the new layout file.
        foreach (var t in TemplateLibrary.All)
            Assert.True(LayoutLibrary.IsValidName(t.Name + ".json"), $"{t.Id}: “{t.Name}” can't be a layout name");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void TemplatesAreValid(string id)
    {
        var raw = TemplateLibrary.LoadRaw(id);
        Assert.Equal((800, 480), (raw.Canvas.Width, raw.Canvas.Height));
        Assert.Empty(raw.Aliases);

        foreach (var el in raw.Static.Concat(raw.Widgets))
        {
            var where = $"{id}: {el.GetType().Name} '{el.Id ?? (el as TextElement)?.Text}'";
            Assert.False(el is ImageElement, where + " — templates can't use images");
            if (el is TextElement t) Assert.True(Fonts.Contains(t.Font), $"{where} uses font {t.Font}");
            if (el is LineElement l)
            {
                Assert.True(l.X is >= 0 and <= 800 && l.X2 is >= 0 and <= 800 && l.Y is >= 0 and <= 480 && l.Y2 is >= 0 and <= 480, where);
                continue;
            }
            Assert.True(el.X >= 0 && el.Y >= 0 && el.X + el.W <= 800.5 && el.Y + el.H <= 480.5, $"{where} at ({el.X},{el.Y},{el.W},{el.H}) is off canvas");
        }

        TemplateFitter.ApplyPlaceholders(raw, new HardwareSummary("CPU", "GPU", null, 0, ['C', 'D', 'E', 'F']));
        foreach (var key in LayoutSerializer.GetSensorKeys(raw))
            Assert.True(SensorDetector.Vocabulary.Contains(key) || Builtins.Contains(key) ||
                        Regex.IsMatch(key, @"^drive\.[C-F]\.(usedPct|freeGB|usedGB|totalGB)$"),
                $"{id} uses key '{key}' that detection can't provide");
    }

    [Fact]
    public void BuildFillsAliasesAndFits()
    {
        var (layout, detection) = TemplateLibrary.Build("duo", SensorDetectorTests.Catalog(SensorDetectorTests.AmdNvidia()),
            ['C', 'D'], new Size(1024, 600), existing: null);
        Assert.Equal((1024, 600), (layout.Canvas.Width, layout.Canvas.Height));
        Assert.True(layout.Aliases.ContainsKey("cpu.temp"));
        Assert.Contains("cpu.temp", detection.Found);
        Assert.DoesNotContain(layout.Static.Concat(layout.Widgets).OfType<TextElement>(), t => t.Text.Contains('$'));
        Assert.Contains(layout.Static.Concat(layout.Widgets).OfType<TextElement>(), t => t.Text.Contains("RTX 3070"));
    }

    [Fact]
    public void BuildKeepsLookWhenNothingDetected()
    {
        var raw = TemplateLibrary.LoadRaw("duo");
        var (layout, detection) = TemplateLibrary.Build("duo", SensorDetectorTests.Catalog([]), [], new Size(800, 480), null);
        Assert.Equal(raw.Static.Count + raw.Widgets.Count, layout.Static.Count + layout.Widgets.Count);
        Assert.Contains("cpu.temp", detection.Missing);
    }
}
