using System.Diagnostics;
using SensorPanel.Editor;
using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

/// <summary>
/// Opt-in: set SP_EDITOR_SCREENSHOT to an output .png path to load the editor in headless Edge
/// and capture it. Also dumps the DOM next to it so JS errors/empty panes are visible.
/// </summary>
public class EditorBrowserSmoke
{
    private static readonly SensorCatalog Catalog = new(
        [
            new("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "Temperature", "°C", "AMD Ryzen 7 3800X", "Cpu", 54.5),
            new("/amdcpu/0/load/0", "CPU Total", "Load", "%", "AMD Ryzen 7 3800X", "Cpu", 12.3),
            new("/amdcpu/0/clock/1", "Core #1", "Clock", "MHz", "AMD Ryzen 7 3800X", "Cpu", 4192),
            new("/amdcpu/0/power/0", "Package", "Power", "W", "AMD Ryzen 7 3800X", "Cpu", 61.2),
            new("/lpc/it8665e/0/fan/0", "Fan #1", "Fan", "RPM", "ITE IT8665E", "SuperIO", 1012),
            new("/lpc/it8665e/0/fan/5", "Fan #6", "Fan", "RPM", "ITE IT8665E", "SuperIO", 4891),
            new("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", "°C", "NVIDIA GeForce RTX 3070", "GpuNvidia", 47),
            new("/gpu-nvidia/0/power/0", "GPU Package", "Power", "W", "NVIDIA GeForce RTX 3070", "GpuNvidia", 38.4),
            new("drive.C.usedPct", "Used", "Load", "%", "Drive C: (System)", "Drive", 96),
        ], DateTime.Now);

    /// <summary>A second page with the phase-3 elements (#layout=Showcase.json), plus graph history.</summary>
    private static void AddShowcase(TestEditorHost host)
    {
        using (var bmp = new SkiaSharp.SKBitmap(64, 64))
        {
            using var c = new SkiaSharp.SKCanvas(bmp);
            c.Clear(SkiaSharp.SKColors.Transparent);
            using var p = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0x76, 0xb9, 0x00), IsAntialias = true };
            c.DrawCircle(32, 32, 28, p);
            using var png = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            host.Images.Import(png.ToArray(), "logo.png");
        }
        const string json = """
            { "canvas": { "width": 800, "height": 480, "background": "#000000" },
              "aliases": { "cpu.load": { "sensor": "/amdcpu/0/load/0" }, "cpu.temp": { "sensor": "/amdcpu/0/temperature/2" } },
              "static": [
                { "type": "rect", "x": 10, "y": 10, "w": 780, "h": 460, "radius": 12,
                  "gradient": { "angle": 90, "colors": ["#1d2533", "#07080a"] } },
                { "type": "image", "group": "CPU header", "src": "logo.png", "x": 30, "y": 30, "w": 64, "h": 64 },
                { "type": "text", "group": "CPU header", "text": "CPU", "x": 110, "y": 48, "size": 28, "weight": 700, "color": "#ffffff" } ],
              "widgets": [
                { "type": "graph", "value": "cpu.load", "x": 30, "y": 130, "w": 740, "h": 180, "duration": 120, "min": 0, "max": 100,
                  "color": "#5aa9ff", "width": 2, "gridLines": 3, "gridColor": "#ffffff1f",
                  "gradient": { "angle": 90, "colors": ["#5aa9ff88", "#5aa9ff00"] } },
                { "type": "bar", "group": "CPU header", "value": "cpu.load", "x": 30, "y": 340, "w": 480, "h": 16, "radius": 8, "track": "#2a2a2a",
                  "gradient": { "colors": ["#3ddc84", "#ffd23f", "#ff4a4a"] } },
                { "type": "ring", "value": "cpu.temp", "x": 580, "y": 320, "w": 140, "h": 140, "thickness": 14, "startAngle": 135, "sweep": 270,
                  "roundCap": true, "min": 20, "max": 100, "track": "#2a2a2a", "gradient": { "colors": ["#5aa9ff", "#ff4a4a"] } } ] }
            """;
        host.Library.Create("Showcase.json", LayoutSerializer.Parse(json));
        host.PageList = ["default.json", "Showcase.json"];

        var now = DateTime.Now;
        var rng = new Random(3);
        double v = 30;
        for (int i = 130; i >= 0; i--)
        {
            v = Math.Clamp(v + rng.NextDouble() * 16 - 8, 3, 97);
            double sample = v;
            host.History.Record(now.AddSeconds(-i), new HashSet<string> { "cpu.load" }, _ => sample);
        }
    }

    [Fact]
    public void Screenshot()
    {
        var output = Environment.GetEnvironmentVariable("SP_EDITOR_SCREENSHOT");
        if (string.IsNullOrEmpty(output)) return;
        Capture(output, Environment.GetEnvironmentVariable("SP_EDITOR_HASH") ?? "");
    }

    /// <summary>Template gallery (#gallery=1): cards and the detection summary render.</summary>
    [Fact]
    public void GalleryScreenshot()
    {
        var output = Environment.GetEnvironmentVariable("SP_EDITOR_SCREENSHOT");
        if (string.IsNullOrEmpty(output)) return;
        // --dump-dom prints nothing on some machines (Edge hands off to a child), so check the server side:
        // the open gallery builds every template for this PC (each build leases FPS; the preview loop renews it too).
        var leases = 0;
        Capture(Path.ChangeExtension(output, null) + "-gallery.png", "#gallery=1", host => leases = host.FpsLeases.Count);
        Assert.True(leases > TemplateLibrary.All.Count, $"{leases} FPS leases: expected one per template build plus renewals");
    }

    /// <summary>Screenshots the editor at <paramref name="hash"/> and returns its DOM.</summary>
    private static string Capture(string output, string hash, Action<TestEditorHost>? inspect = null)
    {

        var edge = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }
            .First(File.Exists);
        using var host = new TestEditorHost(Catalog);
        AddShowcase(host);
        using var server = new EditorServer(host);
        server.Start();

        var profile = Directory.CreateTempSubdirectory("sp-edge-").FullName;
        string Run(string args)
        {
            var psi = new ProcessStartInfo(edge,
                $"--headless=new --disable-gpu --no-sandbox --no-first-run --user-data-dir=\"{profile}\" --window-size=1600,900 " +
                $"--virtual-time-budget=8000 {args} \"{server.Url}{hash}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            p.WaitForExit(60_000);
            return stdout.Result;
        }
        File.Delete(output);
        Run($"--screenshot=\"{output}\"");
        // msedge.exe hands off to a child process and may return before the capture is written.
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!File.Exists(output) && DateTime.UtcNow < deadline) Thread.Sleep(250);
        Thread.Sleep(500);
        Assert.True(File.Exists(output));
        inspect?.Invoke(host);
        var dom = Run("--dump-dom");
        File.WriteAllText(Path.ChangeExtension(output, ".html"), dom);
        return dom;
    }
}
