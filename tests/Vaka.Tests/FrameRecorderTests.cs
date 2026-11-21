using System.Drawing;
using System.Text.Json;
using Vaka.App;
using Vaka.Layout;
using Xunit;

namespace Vaka.Tests;

public sealed class FrameRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-frames-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    private static bool IsWebP(string path)
    {
        var b = File.ReadAllBytes(path);
        return b.Length > 12 && System.Text.Encoding.ASCII.GetString(b, 0, 4) == "RIFF"
                             && System.Text.Encoding.ASCII.GetString(b, 8, 4) == "WEBP";
    }

    [Fact]
    public void WritesLoopAndStills()
    {
        var r = FrameRecorder.Record(_dir, "duo", new Size(200, 120));
        Assert.Equal(30, r.Frames);
        var folder = Path.Combine(_dir, "duo");
        for (int i = 0; i < 30; i++) Assert.True(IsWebP(Path.Combine(folder, $"{i:00}.webp")), $"frame {i}");
        foreach (var s in new[] { "idle", "gaming", "rendering" }) Assert.True(IsWebP(Path.Combine(folder, s + ".webp")), s);
        Assert.Equal(33, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void StillsAreLoopFrames()
    {
        FrameRecorder.Record(_dir, "duo", new Size(200, 120));
        string F(string n) => Path.Combine(_dir, "duo", n + ".webp");
        Assert.Equal(File.ReadAllBytes(F("05")), File.ReadAllBytes(F("idle")));
        Assert.Equal(File.ReadAllBytes(F("15")), File.ReadAllBytes(F("gaming")));
        Assert.Equal(File.ReadAllBytes(F("25")), File.ReadAllBytes(F("rendering")));
    }

    [Fact]
    public void IsByteDeterministic()
    {
        FrameRecorder.Record(Path.Combine(_dir, "a"), "duo", new Size(200, 120));
        FrameRecorder.Record(Path.Combine(_dir, "b"), "duo", new Size(200, 120));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, "a", "duo", "12.webp")),
                     File.ReadAllBytes(Path.Combine(_dir, "b", "duo", "12.webp")));
    }

    [Fact]
    public void PrerollFillsGraphs()
    {
        FrameRecorder.Record(Path.Combine(_dir, "full"), "trends", new Size(400, 240), seconds: 1, prerollSeconds: 120);
        FrameRecorder.Record(Path.Combine(_dir, "empty"), "trends", new Size(400, 240), seconds: 1, prerollSeconds: 0);
        Assert.NotEqual(File.ReadAllBytes(Path.Combine(_dir, "full", "trends", "00.webp")),
                        File.ReadAllBytes(Path.Combine(_dir, "empty", "trends", "00.webp")));
    }

    [Fact]
    public void ManifestListsAllTemplates()
    {
        FrameRecorder.RecordAll(_dir, new Size(160, 96), seconds: 1);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "manifest.json")));
        var root = doc.RootElement;
        Assert.Equal(30, root.GetProperty("loopSeconds").GetInt32());
        Assert.Equal("duo", root.GetProperty("hero").GetString());
        Assert.Equal(160, root.GetProperty("size").GetProperty("w").GetInt32());
        Assert.Equal(["Idle", "Gaming", "Rendering"],
            root.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()!));
        Assert.Equal(10, root.GetProperty("states")[1].GetProperty("from").GetInt32());
        var ids = root.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToList();
        Assert.Equal(TemplateLibrary.All.Select(t => t.Id), ids);
        var first = root.GetProperty("templates")[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("name").GetString()));
        Assert.True(first.GetProperty("shows").GetArrayLength() > 0);
        Assert.Equal(1, first.GetProperty("frames").GetInt32());
    }

    [Fact]
    public void UnknownTemplateFails()
    {
        Assert.Equal(1, Program.Frames(["--frames", _dir, "--template", "nope"]));
        Assert.False(Directory.Exists(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void BadSizeFails() => Assert.Equal(1, Program.Frames(["--frames", _dir, "--size", "huge"]));
}
