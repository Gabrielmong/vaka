using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Vaka.Layout;
using Vaka.Rendering;
using Vaka.Sensors;
using SkiaSharp;

namespace Vaka.App;

/// <summary>
/// Renders templates driven by <see cref="MockSensors"/> to WebP frames for the website: one frame per simulated
/// second (00.webp …), plus a still per state (idle/gaming/rendering.webp, copies of frames 5, 15 and 25).
/// </summary>
public static class FrameRecorder
{
    public sealed record TemplateFrames(string Id, string Name, string Description, IReadOnlyList<string> Shows, int Frames);

    private static readonly (string File, int Second)[] Stills = [("idle", 5), ("gaming", 15), ("rendering", 25)];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static TemplateFrames Record(string outDir, string templateId, Size size, int seconds = MockSensors.LoopSeconds,
        int prerollSeconds = 120, int quality = 80)
    {
        var info = TemplateLibrary.All.FirstOrDefault(t => t.Id == templateId)
                   ?? throw new KeyNotFoundException($"Unknown template “{templateId}”.");
        var mock = new MockSensors();
        var layout = TemplateLibrary.Build(templateId, mock.CatalogAt(0), MockSensors.Drives, size, null).Layout;
        var keys = LayoutSerializer.GetSensorKeys(layout).ToList();
        var history = new SensorHistory { Retention = TimeSpan.FromSeconds(prerollSeconds + seconds + 5) };

        void Sample(int t)
        {
            var catalog = mock.CatalogAt(t);
            history.Record(mock.ClockAt(t), keys, k => catalog.Resolve(layout, k));
        }
        for (int t = -prerollSeconds; t < 0; t++) Sample(t);

        var folder = Path.Combine(outDir, templateId);
        Directory.CreateDirectory(folder);
        using var renderer = new PanelRenderer { Culture = CultureInfo.GetCultureInfo("en-US") };
        using var bitmap = new SKBitmap(size.Width, size.Height);
        using var canvas = new SKCanvas(bitmap);

        // Render far enough for the stills even when fewer frames are asked for.
        int last = Math.Max(seconds, Stills.Max(s => s.Second) + 1);
        for (int t = 0; t < last; t++)
        {
            Sample(t);
            var catalog = mock.CatalogAt(t);
            canvas.Clear(SKColors.Black);
            renderer.Render(canvas, size.Width, size.Height, layout, k => catalog.Resolve(layout, k), mock.ClockAt(t),
                history: history);
            canvas.Flush();
            byte[]? bytes = null;
            byte[] Encode() => bytes ??= bitmap.Encode(SKEncodedImageFormat.Webp, quality).ToArray();
            if (t < seconds) File.WriteAllBytes(Path.Combine(folder, $"{t:00}.webp"), Encode());
            foreach (var (name, second) in Stills)
                if (second == t) File.WriteAllBytes(Path.Combine(folder, name + ".webp"), Encode());
        }
        return new TemplateFrames(info.Id, info.Name, info.Description, info.Shows, seconds);
    }

    /// <summary>Records every built-in template (manifest order) and writes manifest.json for the site.</summary>
    public static void RecordAll(string outDir, Size size, int seconds = MockSensors.LoopSeconds)
    {
        var templates = TemplateLibrary.All.Select(t => Record(outDir, t.Id, size, seconds)).ToList();
        var manifest = new
        {
            LoopSeconds = MockSensors.LoopSeconds,
            Size = new { W = size.Width, H = size.Height },
            Hero = "duo",
            States = MockSensors.States.Select(s => new { s.Name, s.From, s.To }),
            Templates = templates,
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
    }
}
