using System.Net;
using System.Text;
using System.Text.Json;
using SensorPanel.Editor;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SensorPanel.Sensors;
using SkiaSharp;
using Xunit;

namespace SensorPanel.Tests;

public class RendererPhase3Tests : IDisposable
{
    private readonly string _images = Directory.CreateTempSubdirectory("sp-img-").FullName;

    private static PanelLayout Layout(string json) => LayoutSerializer.Parse(
        "{ \"canvas\": { \"width\": 100, \"height\": 50, \"background\": \"#000000\" }, " + json + " }");

    private SKBitmap Render(PanelLayout layout, IHistorySource? history = null, Func<string, double?>? values = null, DateTime? now = null)
    {
        var bmp = new SKBitmap(layout.Canvas.Width, layout.Canvas.Height);
        using var canvas = new SKCanvas(bmp);
        using var r = new PanelRenderer { Images = new ImageStore(_images) };
        r.Render(canvas, bmp.Width, bmp.Height, layout, values ?? (_ => null), now ?? DateTime.Now, history: history);
        return bmp;
    }

    [Fact]
    public void LinearGradientRunsLeftToRight()
    {
        var layout = Layout("\"static\": [ { \"type\": \"rect\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50, " +
                            "\"gradient\": { \"angle\": 0, \"colors\": [\"#ff0000\", \"#0000ff\"] } } ]");
        using var bmp = Render(layout);
        var left = bmp.GetPixel(1, 25);
        var right = bmp.GetPixel(98, 25);
        Assert.True(left.Red > 240 && left.Blue < 15, left.ToString());
        Assert.True(right.Blue > 240 && right.Red < 15, right.ToString());
    }

    [Fact]
    public void BarGradientSpansTrackAndThresholdWins()
    {
        const string bar = "{ \"type\": \"bar\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50, \"value\": \"v\", \"track\": \"#000000\", " +
                           "\"gradient\": { \"colors\": [\"#00ff00\", \"#ff0000\"] }, \"thresholds\": [ { \"gte\": 90, \"color\": \"#ffffff\" } ] }";
        var layout = Layout("\"widgets\": [ " + bar + " ]");

        using var half = Render(layout, values: _ => 50);
        var start = half.GetPixel(2, 25);
        var mid = half.GetPixel(48, 25);
        Assert.True(start.Green > 230 && start.Red < 30, start.ToString());
        Assert.True(mid.Red > 90 && mid.Green > 90, mid.ToString()); // halfway through the gradient
        Assert.Equal(SKColors.Black, half.GetPixel(80, 25));         // not filled

        using var hot = Render(layout, values: _ => 95);
        Assert.Equal(SKColors.White, hot.GetPixel(10, 25));
    }

    [Fact]
    public void RingSweepGradientRenders()
    {
        var layout = Layout("\"widgets\": [ { \"type\": \"ring\", \"x\": 25, \"y\": 0, \"w\": 50, \"h\": 50, \"value\": \"v\", " +
                            "\"thickness\": 8, \"startAngle\": 0, \"sweep\": -270, \"track\": \"#000000\", " +
                            "\"gradient\": { \"colors\": [\"#ff0000\", \"#0000ff\"] } } ]");
        using var bmp = Render(layout, values: _ => 100);
        // Arc starts at 3 o'clock and runs counter-clockwise: red near the start.
        var startPx = bmp.GetPixel(71, 24);
        Assert.True(startPx.Red > 180 && startPx.Blue < 80, startPx.ToString());
    }

    [Fact]
    public void RingRoundCapKeepsStartColor()
    {
        // Arc from 3 o'clock clockwise through 270°; the round cap sticks out just above the start.
        var layout = Layout("\"widgets\": [ { \"type\": \"ring\", \"x\": 25, \"y\": 0, \"w\": 50, \"h\": 50, \"value\": \"v\", " +
                            "\"thickness\": 10, \"startAngle\": 0, \"sweep\": 270, \"roundCap\": true, \"track\": \"#000000\", " +
                            "\"gradient\": { \"colors\": [\"#0000ff\", \"#ff0000\"] } } ]");
        using var bmp = Render(layout, values: _ => 100);
        var cap = bmp.GetPixel(70, 22); // just counter-clockwise of the start angle
        Assert.True(cap.Blue > 180 && cap.Red < 80, cap.ToString());
    }

    [Fact]
    public void GraphDrawsHistory()
    {
        var history = new SensorHistory();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        for (int i = 60; i >= 0; i--)
            history.Record(now.AddSeconds(-i), new HashSet<string> { "v" }, _ => 50);

        var layout = Layout("\"widgets\": [ { \"type\": \"graph\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50, \"value\": \"v\", " +
                            "\"min\": 0, \"max\": 100, \"duration\": 60, \"color\": \"#ffffff\", \"width\": 2, \"fill\": \"#ff0000\" } ]");
        using var bmp = Render(layout, history, now: now);
        Assert.True(bmp.GetPixel(50, 25).Red > 200 && bmp.GetPixel(50, 25).Green > 200); // line at 50%
        Assert.Equal(new SKColor(255, 0, 0), bmp.GetPixel(50, 45));                       // area below
        Assert.Equal(SKColors.Black, bmp.GetPixel(50, 10));                               // nothing above
    }

    [Fact]
    public void GraphGapsBreakTheLine()
    {
        var history = new SensorHistory();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        for (int i = 60; i >= 0; i--)
            history.Record(now.AddSeconds(-i), new HashSet<string> { "v" }, _ => i is > 20 and < 40 ? null : 50);

        var layout = Layout("\"widgets\": [ { \"type\": \"graph\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50, \"value\": \"v\", " +
                            "\"duration\": 60, \"fill\": \"#ff0000\", \"width\": 0 } ]");
        using var bmp = Render(layout, history, now: now);
        Assert.Equal(SKColors.Black, bmp.GetPixel(50, 45)); // gap in the middle
        Assert.Equal(new SKColor(255, 0, 0), bmp.GetPixel(10, 45));
    }

    [Fact]
    public void ImageElementDrawsUploadedPicture()
    {
        var store = new ImageStore(_images);
        using var src = new SKBitmap(10, 10);
        src.Erase(SKColors.Lime);
        using var data = src.Encode(SKEncodedImageFormat.Png, 100);
        var name = store.Import(data.ToArray(), "logo.png");
        Assert.Equal("logo.png", name);

        var layout = Layout("\"static\": [ { \"type\": \"image\", \"src\": \"" + name + "\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50, \"fit\": \"contain\" } ]");
        using var bmp = Render(layout);
        Assert.Equal(SKColors.Lime, bmp.GetPixel(50, 25));
        Assert.Equal(SKColors.Black, bmp.GetPixel(5, 25)); // letterboxed

        var missing = Layout("\"static\": [ { \"type\": \"image\", \"src\": \"..\\\\secret.png\", \"x\": 0, \"y\": 0, \"w\": 100, \"h\": 50 } ]");
        using var none = Render(missing);
        Assert.Equal(SKColors.Black, none.GetPixel(50, 25));
    }

    public void Dispose()
    {
        try { Directory.Delete(_images, true); } catch { /* best effort */ }
    }
}

public class Phase3UnitTests
{
    [Fact]
    public void HistoryTrimsOldSamplesAndUnusedKeys()
    {
        var h = new SensorHistory { Retention = TimeSpan.FromSeconds(10) };
        var t0 = new DateTime(2026, 1, 1);
        for (int i = 0; i < 30; i++) h.Record(t0.AddSeconds(i), new HashSet<string> { "a", "b" }, k => i);
        var list = new List<HistorySample>();
        h.Read("a", DateTime.MinValue, list);
        Assert.Equal(11, list.Count);
        Assert.Equal(19, list[0].Value);

        h.Record(t0.AddSeconds(30), new HashSet<string> { "a" }, _ => double.NaN);
        Assert.False(h.Has("b"));
        list.Clear();
        h.Read("a", t0.AddSeconds(30), list);
        Assert.Null(Assert.Single(list).Value);
    }

    [Theory]
    [InlineData("default.json", true)]
    [InlineData("GPU page.json", true)]
    [InlineData("../x.json", false)]
    [InlineData("a\\b.json", false)]
    [InlineData("C:x.json", false)]
    [InlineData(".hidden.json", false)]
    [InlineData("x.txt", false)]
    [InlineData("a..json", false)]
    public void LayoutNames(string name, bool ok) => Assert.Equal(ok, LayoutLibrary.IsValidName(name));

    [Fact]
    public void NormalizeLayoutName()
    {
        Assert.Equal("GPU page.json", LayoutLibrary.NormalizeName("  GPU page "));
        Assert.Equal("x.json", LayoutLibrary.NormalizeName("x.JSON"));
        Assert.Null(LayoutLibrary.NormalizeName("../evil"));
        Assert.Null(LayoutLibrary.NormalizeName(""));
    }

    [Theory]
    [InlineData("logo.png", true)]
    [InlineData("photo-2.jpg", true)]
    [InlineData("../logo.png", false)]
    [InlineData("logo.svg", false)]
    [InlineData("sub/logo.png", false)]
    public void ImageNames(string name, bool ok) => Assert.Equal(ok, ImageStore.IsValidName(name));

    [Fact]
    public void ImageImportRejectsJunkAndSanitizesNames()
    {
        var dir = Directory.CreateTempSubdirectory("sp-img-").FullName;
        try
        {
            var store = new ImageStore(dir);
            Assert.Throws<InvalidDataException>(() => store.Import(Encoding.UTF8.GetBytes("<svg/>"), "x.png"));

            using var bmp = new SKBitmap(4, 4);
            using var jpg = bmp.Encode(SKEncodedImageFormat.Jpeg, 80);
            Assert.Equal("my-photo.jpg", store.Import(jpg.ToArray(), "..\\my photo!.jpeg".Replace(" ", "-")));
            Assert.Equal("my-photo-2.jpg", store.Import(jpg.ToArray(), "my-photo.jpg"));
            Assert.Equal("image.jpg", store.Import(jpg.ToArray(), "???"));
            Assert.Equal(3, store.List().Count);
            Assert.True(store.Delete("image.jpg"));
            Assert.False(store.Delete("../image.jpg"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NewElementsRoundTrip()
    {
        const string json = """
            { "canvas": { "width": 800, "height": 480 },
              "static": [ { "type": "image", "src": "a.png", "fit": "cover", "x": 1, "y": 2, "w": 3, "h": 4 },
                          { "type": "ellipse", "x": 0, "y": 0, "w": 10, "h": 10, "gradient": { "kind": "radial", "colors": ["#fff", "#000"], "stops": [0, 0.5] } } ],
              "widgets": [ { "type": "graph", "value": "cpu.load", "duration": 120, "autoScale": true, "gridLines": 3, "x": 0, "y": 0, "w": 100, "h": 40 } ] }
            """;
        var layout = LayoutSerializer.Parse(json);
        var again = LayoutSerializer.Parse(LayoutSerializer.Serialize(layout));
        var img = Assert.IsType<ImageElement>(again.Static[0]);
        Assert.Equal(ImageFit.Cover, img.Fit);
        var el = Assert.IsType<EllipseElement>(again.Static[1]);
        Assert.Equal(GradientKind.Radial, el.Gradient!.Kind);
        Assert.Equal([0f, 0.5f], el.Gradient.Stops!);
        var g = Assert.IsType<GraphElement>(again.Widgets[0]);
        Assert.Equal(120, g.Duration);
        Assert.True(g.AutoScale);
        Assert.Contains("cpu.load", LayoutSerializer.GetSensorKeys(again));
    }

    [Fact]
    public void GroupsRoundTripAcrossLayers()
    {
        const string json = """
            { "static": [ { "type": "rect", "group": "CPU card", "x": 0, "y": 0, "w": 10, "h": 10 }, { "type": "rect", "x": 0, "y": 0, "w": 1, "h": 1 } ],
              "widgets": [ { "type": "text", "group": "CPU card", "text": "x", "x": 0, "y": 0 } ] }
            """;
        var again = LayoutSerializer.Parse(LayoutSerializer.Serialize(LayoutSerializer.Parse(json)));
        Assert.Equal("CPU card", again.Static[0].Group);
        Assert.Null(again.Static[1].Group);
        Assert.Equal("CPU card", again.Widgets[0].Group);
        Assert.DoesNotContain("\"group\": null", LayoutSerializer.Serialize(again));
    }
}

/// <summary>Layout management, images and background export over HTTP.</summary>
public sealed class EditorServerPhase3Tests : IDisposable
{
    private readonly TestEditorHost _host = new();
    private readonly EditorServer _server;
    private readonly HttpClient _http = new();
    private readonly string _base, _token;

    public EditorServerPhase3Tests()
    {
        _server = new EditorServer(_host);
        _server.Start();
        _base = $"http://127.0.0.1:{_server.Port}";
        _token = new Uri(_server.Url).Query[3..];
    }

    private async Task<HttpResponseMessage> Send(HttpMethod m, string path, string? json = null, byte[]? bytes = null)
    {
        var r = new HttpRequestMessage(m, _base + path);
        r.Headers.Add("X-Panel-Token", _token);
        if (json is not null) r.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (bytes is not null) r.Content = new ByteArrayContent(bytes);
        return await _http.SendAsync(r);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task CreateRenameDeleteLayoutsAndPages()
    {
        var list = await Json(await Send(HttpMethod.Get, "/api/layouts"));
        Assert.Equal("default.json", list.GetProperty("showing").GetString());
        Assert.Equal(800, list.GetProperty("layouts")[0].GetProperty("width").GetInt32());

        // Blank page keeps canvas + aliases; duplicate copies everything.
        var created = await Send(HttpMethod.Post, "/api/layouts", "{\"name\":\"GPU\"}");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("GPU.json", (await Json(created)).GetProperty("name").GetString());
        var blank = _host.Library.Load("GPU.json");
        Assert.Empty(blank.Widgets);
        Assert.NotEmpty(blank.Aliases);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/layouts", "{\"name\":\"gpu.json\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/layouts", "{\"name\":\"../evil\"}")).StatusCode);
        await Send(HttpMethod.Post, "/api/layouts", "{\"name\":\"Copy\",\"copyFrom\":\"default.json\"}");
        Assert.NotEmpty(_host.Library.Load("Copy.json").Widgets);

        // Edit a specific layout by name.
        var get = await Send(HttpMethod.Get, "/api/layout?name=Copy.json");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/layout?name=GPU.json", await get.Content.ReadAsStringAsync())).StatusCode);
        Assert.Equal(["GPU.json"], _host.SavedNames);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/layout?name=..%5Csettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/layout?name=nope.json")).StatusCode);

        // Rotation.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, "/api/pages", "{\"pages\":[\"default.json\",\"nope.json\"]}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/pages", "{\"pages\":[\"default.json\",\"GPU.json\"],\"seconds\":1}")).StatusCode);
        Assert.Equal(["default.json", "GPU.json"], _host.PageList);
        Assert.Equal(3, _host.Seconds); // clamped
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/show?name=GPU.json")).StatusCode);
        Assert.Equal("GPU.json", _host.Shown);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/show?name=Copy.json")).StatusCode);

        // Rename follows into the rotation; delete removes from it but never the last page.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/layouts/rename", "{\"from\":\"GPU.json\",\"to\":\"Graphics\"}")).StatusCode);
        Assert.Equal(["default.json", "Graphics.json"], _host.PageList);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/api/layouts?name=Graphics.json")).StatusCode);
        Assert.Equal(["default.json"], _host.PageList);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, "/api/layouts?name=default.json")).StatusCode);
        Assert.True(_host.Library.Exists("default.json"));
    }

    [Fact]
    public async Task ImagesUploadListDeleteAndBackground()
    {
        using var bmp = new SKBitmap(20, 10);
        bmp.Erase(SKColors.Orange);
        using var png = bmp.Encode(SKEncodedImageFormat.Png, 100);

        var up = await Send(HttpMethod.Post, "/api/images?name=My%20Logo.png", bytes: png.ToArray());
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var name = (await Json(up)).GetProperty("name").GetString();
        Assert.Equal("My Logo.png", name);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/images?name=x.png", bytes: "nope"u8.ToArray())).StatusCode);

        var list = await Json(await Send(HttpMethod.Get, "/api/images"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(20, list[0].GetProperty("width").GetInt32());
        Assert.StartsWith("data:image/png;base64,", list[0].GetProperty("thumb").GetString());

        var layout = "{ \"canvas\": { \"width\": 40, \"height\": 20, \"background\": \"#000000\" }, " +
                     "\"static\": [ { \"type\": \"image\", \"src\": \"My Logo.png\", \"x\": 0, \"y\": 0, \"w\": 40, \"h\": 20, \"fit\": \"stretch\" } ], " +
                     "\"widgets\": [ { \"type\": \"rect\", \"x\": 0, \"y\": 0, \"w\": 40, \"h\": 20, \"fill\": \"#ffffff\" } ] }";
        var bg = await Send(HttpMethod.Post, "/api/background?scale=2", layout);
        Assert.Equal(HttpStatusCode.OK, bg.StatusCode);
        Assert.Equal("image/png", bg.Content.Headers.ContentType!.MediaType);
        using var decoded = SKBitmap.Decode(await bg.Content.ReadAsByteArrayAsync());
        Assert.Equal(80, decoded.Width);
        Assert.Equal(SKColors.Orange, decoded.GetPixel(40, 20)); // widgets are not part of the background

        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, "/api/images?name=..%2FMy%20Logo.png")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, "/api/images?name=My%20Logo.png")).StatusCode);
        Assert.Equal(0, (await Json(await Send(HttpMethod.Get, "/api/images"))).GetArrayLength());
    }

    [Fact]
    public async Task GraphPreviewRecordsUnpolledSensors()
    {
        var layout = "{ \"canvas\": { \"width\": 40, \"height\": 20 }, \"widgets\": [ { \"type\": \"graph\", \"value\": \"drive.C.usedPct\", \"x\": 0, \"y\": 0, \"w\": 40, \"h\": 20 } ] }";
        var res = await Send(HttpMethod.Post, "/api/render", layout);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        _host.Dispose();
    }
}
