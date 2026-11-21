using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SensorPanel.Sensors;
using SkiaSharp;

namespace SensorPanel.Editor;

/// <summary>Layouts shown in rotation, seconds per page, and the layout on screen right now.</summary>
public sealed record PagesInfo(IReadOnlyList<string> Pages, int Seconds, string Showing);

/// <summary>What the editor server needs from the running app.</summary>
public interface IEditorHost
{
    LayoutLibrary Library { get; }
    ImageStore Images { get; }
    PagesInfo Pages { get; }
    /// <summary>Replaces the rotation (names already validated to exist).</summary>
    void SetPages(IReadOnlyList<string> pages, int seconds);
    /// <summary>Jumps the panel to a page in the rotation.</summary>
    void ShowLayout(string name);
    /// <summary>A layout file was written by the editor.</summary>
    void LayoutSaved(string name);
    SensorCatalog? Catalog { get; }
    IReadOnlyDictionary<string, double?> LastValues { get; }
    SensorHistory History { get; }
    void MarkEditorActivity();
    /// <summary>Size of the panel monitor, when it was found.</summary>
    System.Drawing.Size? PanelSize { get; }
    /// <summary>Keeps FPS capture running for a while (gallery previews), even if no layout uses fps.</summary>
    void LeaseFps(TimeSpan duration);
}

/// <summary>
/// The app runs elevated, so the editor API must only be reachable by the editor page:
/// loopback only, Host header pinned (DNS rebinding), same-origin only (CSRF), and a random
/// per-run token in a custom header on every API call.
/// </summary>
public static class EditorSecurity
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public static bool IsAllowedHost(string? host, int port) =>
        host is not null && (host == $"127.0.0.1:{port}" || host == $"localhost:{port}");

    public static bool IsAllowedOrigin(string? origin, int port) =>
        string.IsNullOrEmpty(origin) || origin == $"http://127.0.0.1:{port}" || origin == $"http://localhost:{port}";

    public static bool TokenMatches(string? provided, string expected) =>
        provided is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}

public sealed class EditorServer : IDisposable
{
    private const int BasePort = 8737;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IEditorHost _host;
    private readonly string _token = EditorSecurity.NewToken();
    private readonly PanelRenderer _renderer = new();
    private HttpListener? _listener;
    private Thread? _thread;
    private string[]? _fonts;

    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/?t={_token}";

    // Graph preview for sensors the panel isn't recording yet (a graph that was just bound).
    private readonly SensorHistory _previewHistory = new() { Retention = TimeSpan.FromHours(1) };
    private readonly PreviewRecorder _previewRecorder = new();
    private readonly Dictionary<string, (DateTime Stamp, string Thumb)> _thumbs = new(StringComparer.OrdinalIgnoreCase);

    public EditorServer(IEditorHost host)
    {
        _host = host;
        _renderer.Images = host.Images;
    }

    private sealed class PreviewHistory(SensorHistory live, SensorHistory preview) : IHistorySource
    {
        public void Read(string key, DateTime since, List<HistorySample> into) =>
            (live.Has(key) ? live : preview).Read(key, since, into);
    }

    public void Start()
    {
        for (int port = BasePort; port < BasePort + 20 && _listener is null; port++)
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                l.Start();
                _listener = l;
                Port = port;
            }
            catch (HttpListenerException) { l.Close(); }
        }
        if (_listener is null) throw new InvalidOperationException("No free port for the editor server.");

        _thread = new Thread(Loop) { IsBackground = true, Name = "EditorServer" };
        _thread.Start();
        Log.Write($"Editor server listening on 127.0.0.1:{Port}");
    }

    private void Loop()
    {
        while (_listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { break; }

            try { Handle(ctx); }
            catch (Exception ex)
            {
                Log.Write($"Editor request {ctx.Request.HttpMethod} {ctx.Request.Url?.AbsolutePath} failed: {ex}");
                try { WriteJson(ctx, 500, new { error = ex.Message }); } catch { /* client gone */ }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url!.AbsolutePath;

        if (!EditorSecurity.IsAllowedHost(req.Headers["Host"], Port))
        {
            WriteJson(ctx, 403, new { error = "Forbidden host" });
            return;
        }

        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!EditorSecurity.IsAllowedOrigin(req.Headers["Origin"], Port) ||
                !EditorSecurity.TokenMatches(req.Headers["X-Panel-Token"], _token))
            {
                WriteJson(ctx, 403, new { error = "Session expired â€” reopen the editor from the tray icon." });
                return;
            }
            _host.MarkEditorActivity();

            try
            {
                switch (req.HttpMethod, path)
                {
                    case ("GET", "/api/layout"): GetLayout(ctx); break;
                    case ("PUT", "/api/layout"): PutLayout(ctx); break;
                    case ("GET", "/api/layouts"): ListLayouts(ctx); break;
                    case ("POST", "/api/layouts"): CreateLayout(ctx); break;
                    case ("POST", "/api/layouts/rename"): RenameLayout(ctx); break;
                    case ("DELETE", "/api/layouts"): DeleteLayout(ctx); break;
                    case ("PUT", "/api/pages"): PutPages(ctx); break;
                    case ("POST", "/api/show"): Show(ctx); break;
                    case ("POST", "/api/render"): Render(ctx); break;
                    case ("POST", "/api/background"): Background(ctx); break;
                    case ("GET", "/api/images"): ListImages(ctx); break;
                    case ("POST", "/api/images"): UploadImage(ctx); break;
                    case ("DELETE", "/api/images"): DeleteImage(ctx); break;
                    case ("GET", "/api/sensors"): Sensors(ctx); break;
                    case ("GET", "/api/fonts"): Fonts(ctx); break;
                    case ("GET", "/api/templates"): WriteJson(ctx, 200, new { templates = TemplateLibrary.All }); break;
                    case ("POST", "/api/templates/build"): BuildTemplate(ctx); break;
                    case ("POST", "/api/templates/redetect"): Redetect(ctx); break;
                    case ("POST", "/api/fps/lease"): _host.LeaseFps(TimeSpan.FromSeconds(30)); WriteJson(ctx, 200, new { ok = true }); break;
                    default: WriteJson(ctx, 404, new { error = "Not found" }); break;
                }
            }
            catch (RequestException ex)
            {
                WriteJson(ctx, ex.Status, new { error = ex.Message });
            }
            return;
        }

        if (req.HttpMethod != "GET")
        {
            WriteJson(ctx, 405, new { error = "Method not allowed" });
            return;
        }
        ServeStatic(ctx, path);
    }

    // â”€â”€ API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class RequestException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    private static RequestException BadRequest(string message) => new(400, message);

    /// <summary>The ?name= layout, or the one on screen when omitted.</summary>
    private string LayoutName(HttpListenerContext ctx)
    {
        var name = ctx.Request.QueryString["name"];
        if (string.IsNullOrEmpty(name)) return _host.Pages.Showing;
        if (!LayoutLibrary.IsValidName(name)) throw BadRequest($"Invalid layout name â€œ{name}â€.");
        return name;
    }

    private void GetLayout(HttpListenerContext ctx)
    {
        var name = LayoutName(ctx);
        PanelLayout layout;
        try { layout = _host.Library.Load(name); }
        catch (FileNotFoundException) { throw new RequestException(404, $"Layout â€œ{name}â€ not found."); }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException)
        {
            throw new RequestException(422, "Layout file can't be loaded: " + ex.Message);
        }
        ctx.Response.Headers["X-Layout-Name"] = Uri.EscapeDataString(name);
        WriteText(ctx, 200, "application/json", LayoutSerializer.Serialize(layout));
    }

    private void PutLayout(HttpListenerContext ctx)
    {
        var name = LayoutName(ctx);
        var layout = ReadLayout(ctx);
        _host.Library.Save(name, layout);
        _host.LayoutSaved(name);
        WriteJson(ctx, 200, new { ok = true });
    }

    private sealed record LayoutEntry(string Name, int Width, int Height, string? Error);

    private void ListLayouts(HttpListenerContext ctx)
    {
        var lib = _host.Library;
        var entries = lib.List().Select(n =>
        {
            try
            {
                var l = lib.Load(n);
                return new LayoutEntry(n, l.Canvas.Width, l.Canvas.Height, null);
            }
            catch (Exception ex) { return new LayoutEntry(n, 0, 0, ex.Message.Split('\n')[0].Trim()); }
        }).ToArray();
        var pages = _host.Pages;
        WriteJson(ctx, 200, new { layouts = entries, pages = pages.Pages, pageSeconds = pages.Seconds, showing = pages.Showing });
    }

    private sealed record CreateRequest(string? Name, string? CopyFrom, string? Template);

    private void CreateLayout(HttpListenerContext ctx)
    {
        var body = ReadJson<CreateRequest>(ctx);
        var name = LayoutLibrary.NormalizeName(body.Name)
                   ?? throw BadRequest("Use letters, digits, spaces, '-', '_' or '.' (up to 60 characters).");
        var lib = _host.Library;
        if (lib.Exists(name)) throw new RequestException(409, $"A layout named â€œ{name}â€ already exists.");

        PanelLayout layout;
        if (!string.IsNullOrEmpty(body.Template))
            layout = BuildForThisPc(body.Template).Layout;
        else if (!string.IsNullOrEmpty(body.CopyFrom))
        {
            if (!lib.Exists(body.CopyFrom)) throw new RequestException(404, $"Layout â€œ{body.CopyFrom}â€ not found.");
            layout = lib.Load(body.CopyFrom);
        }
        else
        {
            // A blank page that keeps the current canvas size and sensor aliases.
            layout = new PanelLayout();
            try
            {
                var current = lib.Load(_host.Pages.Showing);
                layout.Canvas = new CanvasSpec { Width = current.Canvas.Width, Height = current.Canvas.Height, Background = current.Canvas.Background };
                layout.Scale = current.Scale;
                layout.Aliases = current.Aliases;
                layout.SensorNames = current.SensorNames;
            }
            catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or InvalidOperationException) { /* defaults */ }
        }
        lib.Create(name, layout);
        WriteJson(ctx, 200, new { name });
    }

    // â”€â”€ Templates â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed record TemplateRequest(string? Id);

    private PanelLayout? ShowingLayout()
    {
        try { return _host.Library.Load(_host.Pages.Showing); }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or InvalidOperationException) { return null; }
    }

    /// <summary>Builds a template for this PC: detected sensors (merged with the showing layout's), fitted to the panel.</summary>
    private (PanelLayout Layout, Detection Detection) BuildForThisPc(string id)
    {
        if (!TemplateLibrary.All.Any(t => t.Id == id)) throw new RequestException(404, $"Template â€œ{id}â€ not found.");
        var catalog = _host.Catalog ?? throw new RequestException(503, "Sensors are still loading â€” try again in a moment.");
        var showing = ShowingLayout();
        _host.LeaseFps(TimeSpan.FromSeconds(30));
        return TemplateLibrary.Build(id, catalog, TemplateLibrary.SystemDrives(),
            TemplateFitter.TargetSize(_host.PanelSize, showing?.Canvas), showing);
    }

    private static object DetectionDto(Detection d) => new
    {
        found = d.Found.ToArray(),
        missing = d.Missing,
        summary = new { cpu = d.Summary.Cpu, gpu = d.Summary.Gpu, board = d.Summary.Board, fans = d.Summary.Fans,
                        drives = d.Summary.Drives.Select(c => c.ToString()).ToArray() },
    };

    private void BuildTemplate(HttpListenerContext ctx)
    {
        var id = ReadJson<TemplateRequest>(ctx).Id ?? throw BadRequest("Missing template id.");
        var (layout, detection) = BuildForThisPc(id);
        WriteJson(ctx, 200, new { layout = JsonNode.Parse(LayoutSerializer.Serialize(layout)), detection = DetectionDto(detection) });
    }

    /// <summary>Re-runs detection for the posted layout: vocabulary keys are refreshed, the user's other aliases kept.</summary>
    private void Redetect(HttpListenerContext ctx)
    {
        var layout = ReadLayout(ctx);
        var catalog = _host.Catalog ?? throw new RequestException(503, "Sensors are still loading â€” try again in a moment.");
        var detection = SensorDetector.Detect(catalog, TemplateLibrary.SystemDrives(), layout, keepCalibration: true);
        var aliases = new Dictionary<string, SensorAlias>(layout.Aliases, StringComparer.Ordinal);
        foreach (var (k, v) in detection.Aliases) aliases[k] = v;
        var json = JsonNode.Parse(LayoutSerializer.Serialize(new PanelLayout { Aliases = aliases }))!["aliases"];
        WriteJson(ctx, 200, new { aliases = json, detection = DetectionDto(detection) });
    }

    private sealed record RenameRequest(string? From, string? To);

    private void RenameLayout(HttpListenerContext ctx)
    {
        var body = ReadJson<RenameRequest>(ctx);
        var lib = _host.Library;
        if (body.From is null || !lib.Exists(body.From)) throw new RequestException(404, $"Layout â€œ{body.From}â€ not found.");
        var to = LayoutLibrary.NormalizeName(body.To)
                 ?? throw BadRequest("Use letters, digits, spaces, '-', '_' or '.' (up to 60 characters).");
        try { lib.Rename(body.From, to); }
        catch (IOException ex) { throw new RequestException(409, ex.Message); }

        var pages = _host.Pages;
        if (pages.Pages.Contains(body.From, StringComparer.OrdinalIgnoreCase))
            _host.SetPages(pages.Pages.Select(p => string.Equals(p, body.From, StringComparison.OrdinalIgnoreCase) ? to : p).ToList(), pages.Seconds);
        WriteJson(ctx, 200, new { name = to });
    }

    private void DeleteLayout(HttpListenerContext ctx)
    {
        var name = ctx.Request.QueryString["name"];
        var lib = _host.Library;
        if (name is null || !lib.Exists(name)) throw new RequestException(404, $"Layout â€œ{name}â€ not found.");
        var pages = _host.Pages;
        var remaining = pages.Pages.Where(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (remaining.Count == 0)
            throw new RequestException(409, "This is the only page on the panel â€” add another page to the rotation first.");

        lib.Delete(name);
        if (remaining.Count != pages.Pages.Count) _host.SetPages(remaining, pages.Seconds);
        WriteJson(ctx, 200, new { ok = true });
    }

    private sealed record PagesRequest(List<string>? Pages, int? Seconds);

    private void PutPages(HttpListenerContext ctx)
    {
        var body = ReadJson<PagesRequest>(ctx);
        var pages = (body.Pages ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pages.Count == 0) throw BadRequest("The panel needs at least one page.");
        foreach (var p in pages)
            if (!_host.Library.Exists(p)) throw new RequestException(404, $"Layout â€œ{p}â€ not found.");
        _host.SetPages(pages, Math.Clamp(body.Seconds ?? _host.Pages.Seconds, 3, 3600));
        WriteJson(ctx, 200, new { ok = true });
    }

    private void Show(HttpListenerContext ctx)
    {
        var name = ctx.Request.QueryString["name"];
        if (name is null || !_host.Pages.Pages.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new RequestException(409, $"â€œ{name}â€ isn't one of the panel's pages.");
        _host.ShowLayout(name);
        WriteJson(ctx, 200, new { ok = true });
    }

    private static float ScaleParam(HttpListenerContext ctx) =>
        float.TryParse(ctx.Request.QueryString["scale"], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 0.25f, 4f) : 1f;

    private void Render(HttpListenerContext ctx)
    {
        var layout = ReadLayout(ctx);
        float scale = ScaleParam(ctx);
        int w = Math.Max(1, (int)Math.Round(layout.Canvas.Width * scale));
        int h = Math.Max(1, (int)Math.Round(layout.Canvas.Height * scale));

        var catalog = _host.Catalog;
        var last = _host.LastValues;
        double? Value(string k) => catalog?.Resolve(layout, k) ?? (last.TryGetValue(k, out var v) ? v : null);

        var now = DateTime.Now;
        var live = _host.History;
        var missing = layout.Widgets.OfType<GraphElement>()
            .Select(g => g.Value).OfType<string>().Where(k => k.Length > 0 && !live.Has(k))
            .ToHashSet(StringComparer.Ordinal);
        var due = _previewRecorder.Due(missing, now);
        if (due.Count > 0) _previewHistory.Record(now, due.ToHashSet(StringComparer.Ordinal), Value);

        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap))
            _renderer.Render(canvas, w, h, layout, Value, now, history: new PreviewHistory(live, _previewHistory));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        var (staticBounds, widgetBounds) = _renderer.MeasureBounds(layout, Value, now);
        WriteJson(ctx, 200, new
        {
            image = Convert.ToBase64String(png.AsSpan()),
            width = w,
            height = h,
            sensorsReady = catalog is not null,
            bounds = new { @static = staticBounds, widgets = widgetBounds },
        });
    }

    /// <summary>The baked background (canvas color + static layer) as a PNG download.</summary>
    private void Background(HttpListenerContext ctx)
    {
        var layout = ReadLayout(ctx);
        float scale = ScaleParam(ctx);
        int w = Math.Max(1, (int)Math.Round(layout.Canvas.Width * scale));
        int h = Math.Max(1, (int)Math.Round(layout.Canvas.Height * scale));
        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap))
            _renderer.RenderStatic(canvas, w, h, layout);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Write(ctx, 200, "image/png", png.ToArray());
    }

    private void ListImages(HttpListenerContext ctx)
    {
        var images = _host.Images;
        var list = images.List().Select(i => new { i.Name, i.Width, i.Height, i.Bytes, thumb = Thumb(images, i.Name) }).ToArray();
        WriteJson(ctx, 200, list);
    }

    /// <summary>Small PNG data URL for the editor's image list, cached per file version.</summary>
    private string Thumb(ImageStore images, string name)
    {
        var path = images.PathFor(name)!;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_thumbs.TryGetValue(name, out var t) && t.Stamp == stamp) return t.Thumb;

        using var src = SKBitmap.Decode(path);
        if (src is null) return "";
        float s = Math.Min(1f, 96f / Math.Max(src.Width, src.Height));
        using var small = src.Resize(new SKImageInfo(Math.Max(1, (int)(src.Width * s)), Math.Max(1, (int)(src.Height * s))),
            new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? src.Copy();
        using var png = small.Encode(SKEncodedImageFormat.Png, 100);
        var url = "data:image/png;base64," + Convert.ToBase64String(png.AsSpan());
        _thumbs[name] = (stamp, url);
        return url;
    }

    private void UploadImage(HttpListenerContext ctx)
    {
        var data = ReadBody(ctx, ImageStore.MaxUploadBytes);
        try
        {
            var name = _host.Images.Import(data, ctx.Request.QueryString["name"]);
            WriteJson(ctx, 200, new { name });
        }
        catch (InvalidDataException ex) { throw BadRequest(ex.Message); }
    }

    private void DeleteImage(HttpListenerContext ctx)
    {
        if (!_host.Images.Delete(ctx.Request.QueryString["name"] ?? ""))
            throw new RequestException(404, "Image not found.");
        WriteJson(ctx, 200, new { ok = true });
    }

    private void Sensors(HttpListenerContext ctx)
    {
        var catalog = _host.Catalog;
        WriteJson(ctx, 200, catalog is null
            ? new { ready = false, sensors = Array.Empty<SensorInfo>() }
            : new { ready = true, sensors = catalog.Sensors.ToArray() });
    }

    private void Fonts(HttpListenerContext ctx)
    {
        _fonts ??= SKFontManager.Default.GetFontFamilies()
            .Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WriteJson(ctx, 200, _fonts);
    }

    private static byte[] ReadBody(HttpListenerContext ctx, int maxBytes)
    {
        var req = ctx.Request;
        if (req.ContentLength64 > maxBytes) throw new RequestException(413, "Request too large.");
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = req.InputStream.Read(buffer)) > 0)
        {
            if (ms.Length + n > maxBytes) throw new RequestException(413, "Request too large.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static T ReadJson<T>(HttpListenerContext ctx) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ReadBody(ctx, 64 * 1024), Json) ?? throw BadRequest("Empty request.");
        }
        catch (JsonException ex) { throw BadRequest("Invalid request: " + ex.Message); }
    }

    private static PanelLayout ReadLayout(HttpListenerContext ctx)
    {
        var body = Encoding.UTF8.GetString(ReadBody(ctx, MaxBodyBytes));
        try { return LayoutSerializer.Parse(body); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw BadRequest("Invalid layout: " + ex.Message);
        }
    }

    // â”€â”€ Static files â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
    };

    /// <summary>Embedded editor files ("wwwroot/assets/x.js"), keyed with forward slashes.</summary>
    private static readonly Lazy<Dictionary<string, string>> Resources = new(() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("wwwroot", StringComparison.Ordinal))
            .ToDictionary(n => n.Replace('\\', '/')["wwwroot/".Length..], n => n, StringComparer.Ordinal));

    internal const string NoncePlaceholder = "__CSP_NONCE__";

    private static void ServeStatic(HttpListenerContext ctx, string path)
    {
        var name = path == "/" ? "index.html" : path.TrimStart('/');
        if (name.Contains("..") || name.Contains('\\') || name.Contains(':') ||
            !ContentTypes.TryGetValue(Path.GetExtension(name), out var type))
        {
            WriteJson(ctx, 404, new { error = "Not found" });
            return;
        }

        byte[]? data = null;
        // Dev mode: serve the editor build output from disk (npm run watch) so a reload picks up changes.
        var devRoot = Environment.GetEnvironmentVariable("SENSORPANEL_WWWROOT");
        if (!string.IsNullOrEmpty(devRoot))
        {
            var file = Path.GetFullPath(Path.Combine(devRoot, name));
            if (file.StartsWith(Path.GetFullPath(devRoot), StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                data = File.ReadAllBytes(file);
        }
        if (data is null && Resources.Value.TryGetValue(name, out var resource))
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            data = ms.ToArray();
        }
        if (data is null)
        {
            WriteJson(ctx, 404, new { error = "Not found" });
            return;
        }

        if (name == "index.html")
        {
            // MUI (emotion) injects <style> tags at runtime; they carry this per-response nonce.
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            data = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace(NoncePlaceholder, nonce));
            ctx.Response.Headers["Content-Security-Policy"] =
                $"default-src 'self'; img-src 'self' data: blob:; font-src 'self'; style-src 'self' 'nonce-{nonce}'; " +
                "script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        }
        Write(ctx, 200, type, data);
    }

    // â”€â”€ Response helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void WriteJson(HttpListenerContext ctx, int status, object body) =>
        Write(ctx, status, "application/json", JsonSerializer.SerializeToUtf8Bytes(body, Json));

    private static void WriteText(HttpListenerContext ctx, int status, string type, string body) =>
        Write(ctx, status, type, Encoding.UTF8.GetBytes(body));

    private static void Write(HttpListenerContext ctx, int status, string type, byte[] body)
    {
        var res = ctx.Response;
        res.StatusCode = status;
        res.ContentType = type;
        res.Headers["Cache-Control"] = "no-store";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.ContentLength64 = body.Length;
        res.OutputStream.Write(body);
        res.Close();
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { /* ignore */ }
        _listener = null;
        _renderer.Dispose();
    }
}
