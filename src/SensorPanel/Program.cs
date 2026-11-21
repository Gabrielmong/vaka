using SensorPanel.App;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SensorPanel.Sensors;
using SkiaSharp;

namespace SensorPanel;

internal static class Program
{
    /// <summary>
    /// Usage:
    ///   SensorPanel.exe                         run the panel
    ///   SensorPanel.exe --install-startup [--all-users]
    ///                                           register logon task (elevated, no UAC prompt);
    ///                                           --all-users: for every user (used by the installer)
    ///   SensorPanel.exe --uninstall-startup
    ///   SensorPanel.exe --list-sensors out.tsv  dump every sensor (dev aid for detection rules)
    ///   SensorPanel.exe --snapshot out.png [--layout file.json] [--size 800x480] [--record seconds]
    ///                                           render one frame with live sensors to a PNG
    ///                                           (--record samples history first so graphs have data)
    ///   SensorPanel.exe --snapshot out.png --template duo [--size 1024x600]
    ///                                           build a built-in template for this PC and render it
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--install-startup"))
        {
            var ok = StartupTask.Install(Environment.ProcessPath!, args.Contains("--all-users"), out var output);
            Log.Write($"--install-startup: ok={ok} {output.Trim()}");
            return ok ? 0 : 1;
        }
        if (args.Contains("--uninstall-startup"))
        {
            var ok = StartupTask.Uninstall(out var output);
            Log.Write($"--uninstall-startup: ok={ok} {output.Trim()}");
            return ok ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--list-sensors")
            return ListSensors(args[1]);
        if (args.Length >= 2 && args[0] == "--snapshot")
            return Snapshot(args[1], Arg(args, "--layout"), Arg(args, "--size"),
                int.TryParse(Arg(args, "--record"), out var rec) ? Math.Clamp(rec, 0, 600) : 0, Arg(args, "--template"));

        using var mutex = new Mutex(true, @"Global\SensorPanel.SingleInstance", out var created);
        if (!created) return 1;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("UI exception: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal: " + e.ExceptionObject);

        ApplicationConfiguration.Initialize();
        Application.Run(new PanelApp());
        return 0;
    }

    /// <summary>Dev aid: writes every sensor (all hardware groups) as tab-separated lines.</summary>
    private static int ListSensors(string outPath)
    {
        using var hub = new SensorHub();
        hub.Open(SensorGroups.All);
        hub.BuildCatalog();
        Thread.Sleep(1000);
        var lines = hub.BuildCatalog().Sensors.Select(s =>
            string.Join('	', s.Id, s.Name, s.Type, s.Unit, s.Hardware, s.HardwareType, s.Value?.ToString("0.###") ?? "null"));
        File.WriteAllLines(outPath, lines);
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Snapshot(string outPath, string? layoutPath, string? size, int recordSeconds, string? templateId = null)
    {
        try
        {
            (int Width, int Height)? requested =
                size?.Split('x') is [var sw, var sh] && int.TryParse(sw, out var pw) && int.TryParse(sh, out var ph) ? (pw, ph) : null;
            using var hub = new SensorHub();
            PanelLayout layout;
            if (templateId is not null)
            {
                // Build the template for this PC the way the editor gallery does (all sensors, fitted to --size).
                hub.Open(SensorGroups.All);
                hub.BuildCatalog();
                Thread.Sleep(1000);
                var target = requested is { } r ? new System.Drawing.Size(r.Width, r.Height) : new System.Drawing.Size(800, 480);
                (layout, var detection) = TemplateLibrary.Build(templateId, hub.BuildCatalog(), TemplateLibrary.SystemDrives(), target, null);
                File.WriteAllText(Path.ChangeExtension(outPath, ".detect.txt"),
                    $"{detection.Summary}{Environment.NewLine}missing: {string.Join(", ", detection.Missing)}{Environment.NewLine}" +
                    string.Join(Environment.NewLine, layout.Aliases.Select(kv => $"{kv.Key} -> {kv.Value.Expr ?? kv.Value.Sensor} {kv.Value.Name}")));
                requested = null;
            }
            else
            {
                LayoutStore.EnsureDefaults();
                layout = LayoutStore.Load(layoutPath ?? AppSettings.Load().Layout);
            }
            int w = requested?.Width ?? layout.Canvas.Width, h = requested?.Height ?? layout.Canvas.Height;

            var keys = LayoutSerializer.GetSensorKeys(layout);
            if (templateId is null) hub.Open(SensorGroupResolver.ForKeys(layout, keys));
            hub.Poll(layout, keys);
            Thread.Sleep(1000); // load sensors need two samples
            var values = hub.Poll(layout, keys);
            var history = new SensorHistory { Retention = TimeSpan.FromSeconds(Math.Max(recordSeconds, 1) + 5) };
            for (int i = 0; i < recordSeconds; i++)
            {
                history.Record(DateTime.Now, keys.ToList(), k => values.TryGetValue(k, out var v) ? v : null);
                Thread.Sleep(1000);
                values = hub.Poll(layout, keys);
            }

            using var bitmap = new SKBitmap(w, h);
            using (var canvas = new SKCanvas(bitmap))
            using (var renderer = new PanelRenderer())
                renderer.Render(canvas, w, h, layout, k => values.TryGetValue(k, out var v) ? v : null, DateTime.Now,
                    history: recordSeconds > 0 ? history : null);

            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            File.WriteAllLines(Path.ChangeExtension(outPath, ".values.txt"),
                values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} = {kv.Value?.ToString("0.###") ?? "null"}"));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("Snapshot failed: " + ex);
            File.WriteAllText(Path.ChangeExtension(outPath, ".error.txt"), ex.ToString());
            return 2;
        }
    }
}
