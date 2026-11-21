using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SensorPanel.Editor;
using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

/// <summary>Drives a real EditorServer over HTTP with a fake host.</summary>
public sealed class EditorServerTests : IDisposable
{
    private readonly TestEditorHost _host = new(
        new SensorCatalog([new("/amdcpu/0/temperature/2", "Tctl", "Temperature", "°C", "Ryzen", "Cpu", 55)], DateTime.Now));
    private readonly EditorServer _server;
    private readonly HttpClient _http = new();
    private readonly string _base, _token;

    public EditorServerTests()
    {
        _server = new EditorServer(_host);
        _server.Start();
        var uri = new Uri(_server.Url);
        _base = $"http://127.0.0.1:{_server.Port}";
        _token = uri.Query[3..];
    }

    private HttpRequestMessage Req(HttpMethod m, string path, string? body = null, bool token = true)
    {
        var r = new HttpRequestMessage(m, _base + path);
        if (token) r.Headers.Add("X-Panel-Token", _token);
        if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    [Fact]
    public async Task ServesPageWithCsp()
    {
        var res = await _http.GetAsync(_base + "/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        var csp = res.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp);

        // Per-response nonce: substituted into the page and allowed for styles only.
        Assert.DoesNotContain(EditorServer.NoncePlaceholder, html);
        var nonce = System.Text.RegularExpressions.Regex.Match(html, "name=\"csp-nonce\" content=\"([^\"]+)\"").Groups[1].Value;
        Assert.True(nonce.Length >= 16);
        Assert.Contains($"style-src 'self' 'nonce-{nonce}'", csp);
        var html2 = await _http.GetStringAsync(_base + "/");
        Assert.DoesNotContain(nonce, html2);

        // Hashed bundle files under /assets are served with proper types.
        var script = System.Text.RegularExpressions.Regex.Match(html, "src=\"/(assets/[^\"]+\\.js)\"").Groups[1].Value;
        Assert.NotEmpty(script);
        var js = await _http.GetAsync(_base + "/" + script);
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("text/javascript", js.Content.Headers.ContentType!.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(_base + "/assets/nope.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(_base + "/../secret.json")).StatusCode);
    }

    [Fact]
    public async Task ApiRejectsMissingTokenAndForeignOrigin()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, "/api/layout", token: false))).StatusCode);
        var r = Req(HttpMethod.Get, "/api/layout");
        r.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(r)).StatusCode);
    }

    [Fact]
    public async Task LayoutRenderSaveRoundTrip()
    {
        var get = await _http.SendAsync(Req(HttpMethod.Get, "/api/layout"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var json = await get.Content.ReadAsStringAsync();
        var layout = LayoutSerializer.Parse(json);
        Assert.NotEmpty(layout.Widgets);

        var render = await _http.SendAsync(Req(HttpMethod.Post, "/api/render?scale=0.5", json));
        Assert.Equal(HttpStatusCode.OK, render.StatusCode);
        using var doc = JsonDocument.Parse(await render.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(400, root.GetProperty("width").GetInt32());
        Assert.Equal(240, root.GetProperty("height").GetInt32());
        Assert.True(root.GetProperty("sensorsReady").GetBoolean());
        var png = Convert.FromBase64String(root.GetProperty("image").GetString()!);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(layout.Widgets.Count, root.GetProperty("bounds").GetProperty("widgets").GetArrayLength());
        Assert.Equal(layout.Static.Count, root.GetProperty("bounds").GetProperty("static").GetArrayLength());

        var put = await _http.SendAsync(Req(HttpMethod.Put, "/api/layout", json));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(["default.json"], _host.SavedNames);
        Assert.Equal(layout.Widgets.Count, _host.Library.Load("default.json").Widgets.Count);
    }

    [Fact]
    public async Task BadLayoutIs400()
    {
        var res = await _http.SendAsync(Req(HttpMethod.Put, "/api/layout", "{\"canvas\":{\"width\":0,\"height\":10}}"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(_host.SavedNames);
        res = await _http.SendAsync(Req(HttpMethod.Post, "/api/render", "{ not json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SensorsAndFonts()
    {
        var s = await _http.SendAsync(Req(HttpMethod.Get, "/api/sensors"));
        using var doc = JsonDocument.Parse(await s.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ready").GetBoolean());
        var first = doc.RootElement.GetProperty("sensors")[0];
        Assert.Equal("/amdcpu/0/temperature/2", first.GetProperty("id").GetString());
        Assert.Equal(55, first.GetProperty("value").GetDouble());

        var fonts = await (await _http.SendAsync(Req(HttpMethod.Get, "/api/fonts"))).Content.ReadFromJsonAsync<string[]>();
        Assert.Contains(fonts!, f => f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        _host.Dispose();
    }
}
