using System.Net;
using System.Text;
using System.Text.Json;
using Vaka.Editor;
using Vaka.Layout;
using Vaka.Sensors;
using Xunit;

namespace Vaka.Tests;

/// <summary>Template endpoints of the editor server, over HTTP with a fake host (the owner's PC sensors).</summary>
public sealed class TemplateApiTests : IDisposable
{
    private readonly TestEditorHost _host;
    private readonly EditorServer _server;
    private readonly HttpClient _http = new();
    private readonly string _base, _token;

    public TemplateApiTests() : this(SensorDetectorTests.Catalog(SensorDetectorTests.AmdNvidia())) { }

    private TemplateApiTests(SensorCatalog? catalog)
    {
        _host = new TestEditorHost(catalog);
        _server = new EditorServer(_host);
        _server.Start();
        _base = $"http://127.0.0.1:{_server.Port}";
        _token = new Uri(_server.Url).Query[3..];
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod m, string path, string? body = null)
    {
        var r = new HttpRequestMessage(m, _base + path);
        r.Headers.Add("X-Panel-Token", _token);
        if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(r);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();

    [Fact]
    public async Task ListsTemplates()
    {
        var (status, body) = await Send(HttpMethod.Get, "/api/templates");
        Assert.Equal(HttpStatusCode.OK, status);
        var first = body.GetProperty("templates")[0];
        Assert.Equal("duo", first.GetProperty("id").GetString());
        Assert.NotEmpty(Strings(first.GetProperty("shows")));
    }

    [Fact]
    public async Task BuildReturnsLayoutAndDetection()
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/templates/build", """{"id":"duo"}""");
        Assert.Equal(HttpStatusCode.OK, status);
        var layout = LayoutSerializer.Parse(body.GetProperty("layout").GetRawText());
        Assert.Equal("/amdcpu/0/temperature/2", layout.Aliases["cpu.temp"].Sensor);
        var detection = body.GetProperty("detection");
        Assert.Contains("cpu.temp", Strings(detection.GetProperty("found")));
        Assert.Contains("board.temp", Strings(detection.GetProperty("missing")));
        Assert.Equal("NVIDIA GeForce RTX 3070", detection.GetProperty("summary").GetProperty("gpu").GetString());
        Assert.Equal("C", Strings(detection.GetProperty("summary").GetProperty("drives"))[0]);
    }

    [Fact]
    public async Task BuildUsesPanelSize()
    {
        _host.PanelSize = new System.Drawing.Size(1024, 600);
        var (_, body) = await Send(HttpMethod.Post, "/api/templates/build", """{"id":"duo"}""");
        Assert.Equal(1024, LayoutSerializer.Parse(body.GetProperty("layout").GetRawText()).Canvas.Width);
    }

    [Fact]
    public async Task BuildRejectsUnknownTemplate()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/templates/build", """{"id":"nope"}""");
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task BuildReturns503WithoutCatalog()
    {
        using var t = new TemplateApiTests(null);
        var (status, body) = await t.Send(HttpMethod.Post, "/api/templates/build", """{"id":"duo"}""");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("loading", body.GetProperty("error").GetString());
        var (create, _) = await t.Send(HttpMethod.Post, "/api/layouts", """{"name":"Mine","template":"duo"}""");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create);
        Assert.False(t._host.Library.Exists("Mine.json"));
    }

    [Fact]
    public async Task CreatesLayoutFromTemplate()
    {
        var (status, body) = await Send(HttpMethod.Post, "/api/layouts", """{"name":"Mine","template":"duo"}""");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Mine.json", body.GetProperty("name").GetString());
        var saved = _host.Library.Load("Mine.json");
        Assert.Equal("/amdcpu/0/temperature/2", saved.Aliases["cpu.temp"].Sensor);
    }

    [Fact]
    public async Task CreateFromTemplateNeverOverwrites()
    {
        var path = _host.Library.ResolvePath("default.json");
        var before = await File.ReadAllBytesAsync(path);
        var (status, _) = await Send(HttpMethod.Post, "/api/layouts", """{"name":"default","template":"duo"}""");
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task BuildMergesShowingLayoutAliases()
    {
        // The showing layout (bundled default.json) has hand-picked aliases; same-type ones are reused.
        var showing = _host.Library.Load("default.json");
        showing.Aliases["cpu.temp"] = new SensorAlias { Sensor = "/amdcpu/0/temperature/4" };
        _host.Library.Save("default.json", showing);
        var (_, body) = await Send(HttpMethod.Post, "/api/templates/build", """{"id":"duo"}""");
        Assert.Equal("/amdcpu/0/temperature/4", LayoutSerializer.Parse(body.GetProperty("layout").GetRawText()).Aliases["cpu.temp"].Sensor);
    }

    [Fact]
    public async Task RedetectKeepsUserAlias()
    {
        var layout = new PanelLayout
        {
            Aliases =
            {
                ["cpu.temp"] = new SensorAlias { Sensor = "/amdcpu/0/temperature/4" },
                ["gpu.temp"] = new SensorAlias { Sensor = "/gone/0/temperature/0" },
                ["my.own"] = new SensorAlias { Sensor = "/amdcpu/0/load/1" },
            },
        };
        var (status, body) = await Send(HttpMethod.Post, "/api/templates/redetect", LayoutSerializer.Serialize(layout));
        Assert.Equal(HttpStatusCode.OK, status);
        var aliases = body.GetProperty("aliases");
        Assert.Equal("/amdcpu/0/temperature/4", aliases.GetProperty("cpu.temp").GetProperty("sensor").GetString());
        Assert.Equal("/gpu-nvidia/0/temperature/0", aliases.GetProperty("gpu.temp").GetProperty("sensor").GetString());
        Assert.Equal("/amdcpu/0/load/1", aliases.GetProperty("my.own").GetProperty("sensor").GetString()); // non-vocabulary kept
        Assert.Contains("cpu.temp", Strings(body.GetProperty("detection").GetProperty("found")));
    }

    [Fact]
    public async Task RedetectKeepsUserCalibration()
    {
        // A hand-made layout shows gpu.clock in MHz (scale 1) and calibrates cpu.temp; re-detect must not change either.
        var layout = new PanelLayout
        {
            Aliases =
            {
                ["gpu.clock"] = new SensorAlias { Sensor = "/gpu-nvidia/0/clock/0" },
                ["cpu.temp"] = new SensorAlias { Sensor = "/amdcpu/0/temperature/2", Offset = -5 },
            },
        };
        var (_, body) = await Send(HttpMethod.Post, "/api/templates/redetect", LayoutSerializer.Serialize(layout));
        var aliases = LayoutSerializer.Parse($$"""{"aliases":{{body.GetProperty("aliases").GetRawText()}}}""").Aliases;
        Assert.Equal(1, aliases["gpu.clock"].Scale);
        Assert.Equal(-5, aliases["cpu.temp"].Offset);
    }

    [Fact]
    public async Task FpsLeaseEndpoint()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/fps/lease");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(_host.FpsLeases);
    }

    [Fact]
    public async Task BuildLeasesFps()
    {
        await Send(HttpMethod.Post, "/api/templates/build", """{"id":"duo"}""");
        Assert.NotEmpty(_host.FpsLeases);
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
        _host.Dispose();
    }
}
