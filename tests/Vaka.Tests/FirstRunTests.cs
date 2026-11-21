using System.Drawing;
using Vaka.App;
using Vaka.Display;
using Vaka.Layout;
using Xunit;

namespace Vaka.Tests;

public class FirstRunTests
{
    private static MonitorInfo Mon(int w, int h, bool primary) =>
        new($"\\\\.\\DISPLAY{w}", $"id{w}", "Monitor", new Rectangle(0, 0, w, h), primary);

    [Fact]
    public void FirstRunSizeSingleMonitorIs800x480() =>
        Assert.Equal(new Size(800, 480), FirstRun.TargetSize([Mon(2560, 1440, true)]));

    [Fact]
    public void FirstRunSizeUsesPanelMonitor() =>
        Assert.Equal(new Size(1024, 600), FirstRun.TargetSize([Mon(2560, 1440, true), Mon(1024, 600, false)]));

    [Fact]
    public void FirstRunIgnoresOrdinarySecondMonitor() =>
        Assert.Equal(new Size(800, 480), FirstRun.TargetSize([Mon(2560, 1440, true), Mon(1920, 1080, false)]));

    [Fact]
    public void FirstRunSizeAmbiguousIs800x480() =>
        Assert.Equal(new Size(800, 480), FirstRun.TargetSize([Mon(2560, 1440, true), Mon(1024, 600, false), Mon(1280, 800, false)]));

    [Fact]
    public void FirstRunCreatesDuo()
    {
        var layout = FirstRun.Create(SensorDetectorTests.Catalog(SensorDetectorTests.AmdNvidia()), ['C'], new Size(1024, 600));
        Assert.Equal("/amdcpu/0/temperature/2", layout.Aliases["cpu.temp"].Sensor);
        Assert.Equal(1024, layout.Canvas.Width);
        Assert.Equal(TemplateLibrary.LoadRaw("duo").Widgets.Count, layout.Widgets.Count);
    }

    [Fact]
    public void EnsureDefaultsReportsEmptyFolder()
    {
        var dir = Directory.CreateTempSubdirectory("sp-firstrun-").FullName;
        try
        {
            Assert.True(LayoutStore.EnsureDefaults(dir));
            Assert.Empty(Directory.GetFiles(dir)); // nothing copied any more
            File.WriteAllText(Path.Combine(dir, "x.json"), "{}");
            Assert.False(LayoutStore.EnsureDefaults(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void EnsureDefaultsCreatesMissingFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-firstrun-" + Guid.NewGuid().ToString("N"));
        try { Assert.True(LayoutStore.EnsureDefaults(dir)); Assert.True(Directory.Exists(dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
