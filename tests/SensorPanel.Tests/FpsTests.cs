using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

public class FpsTests
{
    private const string Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,TimeInMs," +
        "MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI";

    private static string Row(string app, int pid, string chain, string ms) =>
        $"{app},{pid},{chain},DXGI,0,0,0,Hardware: Independent Flip,1.0,NA,{ms},NA,0.1";

    private sealed class Clock { public long Now = 100_000; }

    private static (FpsMonitor Mon, Clock Clock) Make(int foreground = 42)
    {
        var clock = new Clock();
        return (new FpsMonitor(() => null, () => foreground, () => clock.Now), clock);
    }

    /// <summary>Feeds frames of <paramref name="ms"/> each, advancing the clock in real time.</summary>
    private static void Frames(FpsMonitor mon, Clock clock, int pid, string chain, double ms, int count)
    {
        for (int i = 0; i < count; i++)
        {
            clock.Now += (long)Math.Round(ms);
            mon.Ingest(Row("game.exe", pid, chain, ms.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Fact]
    public void AverageFpsAndFrameTime()
    {
        var (mon, clock) = Make();
        mon.Ingest(Header);
        Frames(mon, clock, 42, "0xA", 10, 300); // 100 fps for 3 s
        var r = mon.Compute(42);
        Assert.Equal(100, r.Fps!.Value, 1);
        Assert.Equal(10, r.FrameTime!.Value, 1);
        Assert.Equal(100, r.Low1!.Value, 1);
    }

    [Fact]
    public void OnePercentLowReflectsStutter()
    {
        var (mon, clock) = Make();
        mon.Ingest(Header);
        for (int s = 0; s < 5; s++)
        {
            Frames(mon, clock, 42, "0xA", 10, 97);
            Frames(mon, clock, 42, "0xA", 50, 3); // 3% of frames are 50 ms hitches
        }
        var r = mon.Compute(42);
        Assert.Equal(20, r.Low1!.Value, 1);
        Assert.True(r.Fps > 70);
    }

    [Fact]
    public void UsesForegroundProcessAndBusiestSwapChain()
    {
        var (mon, clock) = Make();
        mon.Ingest(Header);
        for (int i = 0; i < 60; i++)
        {
            Frames(mon, clock, 42, "0xGAME", 8, 2);   // ~125 fps main window
            Frames(mon, clock, 42, "0xOVERLAY", 16, 1);
            mon.Ingest(Row("other.exe", 7, "0xB", "33.3"));
        }
        Assert.Equal(125, mon.Compute(42).Fps!.Value, 0);
        Assert.Equal(30, mon.Compute(7).Fps!.Value, 0);
        Assert.Null(mon.Compute(999).Fps);
    }

    [Fact]
    public void FallsBackToSameExeForBrowserStyleApps()
    {
        var (mon, clock) = Make();
        mon.Ingest(Header);
        Frames(mon, clock, 500, "0xGPU", 16, 120); // "game.exe" GPU child process presents for window pid 42
        Assert.Null(mon.Compute(42).Fps);
        Assert.Equal(62.5, mon.Compute(42, "GAME.exe").Fps!.Value, 1);
        Assert.Null(mon.Compute(42, "explorer.exe").Fps);
    }

    [Fact]
    public void GoesBlankWhenFramesStop()
    {
        var (mon, clock) = Make();
        mon.Ingest(Header);
        Frames(mon, clock, 42, "0xA", 16.7, 120);
        Assert.NotNull(mon.Compute(42).Fps);
        clock.Now += 1500;
        Assert.Null(mon.Compute(42).Fps);
    }

    [Fact]
    public void IgnoresJunkRowsAndNeedsHeader()
    {
        var (mon, clock) = Make();
        Frames(mon, clock, 42, "0xA", 10, 50); // no header yet
        Assert.Null(mon.Compute(42).Fps);
        mon.Ingest(Header);
        mon.Ingest(Row("game.exe", 42, "0xA", "NA"));
        mon.Ingest(Row("game.exe", 42, "0xA", "0"));
        mon.Ingest("garbage");
        mon.Ingest("game.exe,notapid,0xA,DXGI");
        Assert.Null(mon.Compute(42).Fps);
    }

    [Fact]
    public void ParsesRealV1Header()
    {
        var (mon, clock) = Make();
        mon.Ingest("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents");
        for (int i = 0; i < 100; i++)
        {
            clock.Now += 7;
            mon.Ingest("Code.exe,42,0x000001FE92BE4C60,DXGI,0,0,0,0.0172,0.1658,7.0");
        }
        Assert.Equal(1000 / 7.0, mon.Compute(42).Fps!.Value, 1);
    }

    [Fact]
    public void HubResolvesFpsKeysAndAsksForMonitor()
    {
        var fps = new FpsMonitor(() => null, () => 1, () => 0);
        using var hub = new SensorHub(fps);
        hub.Open(SensorGroups.None);
        var layout = new PanelLayout();
        var values = hub.Poll(layout, new HashSet<string> { "fps", "fps.low1", "fps.frametime", "drive.C.usedPct" });
        Assert.True(fps.Wanted);
        Assert.Null(values["fps"]);
        Assert.Contains("PresentMon not found", fps.Status);
        Assert.Contains(hub.BuildCatalog().Sensors, s => s.Id == "fps.low1");

        hub.Poll(new PanelLayout(), new HashSet<string> { "drive.C.usedPct" });
        Assert.False(fps.Wanted);
    }

    /// <summary>Opt-in (SP_FPS_LIVE=1, elevated, PresentMon installed): runs the real capture briefly.</summary>
    [Fact]
    public void LiveCapture()
    {
        if (Environment.GetEnvironmentVariable("SP_FPS_LIVE") != "1") return;
        var pid = System.Diagnostics.Process.GetProcessesByName("Code").FirstOrDefault()?.Id ?? 0;
        using var mon = new FpsMonitor(foregroundPid: () => pid);
        mon.Wanted = true;
        mon.Tick();
        Assert.True(mon.Running, mon.Status);
        Thread.Sleep(3000);
        mon.Tick();
        Assert.True(mon.Running, mon.Status);
        Assert.True(mon.FramesSeen > 0, "no frames parsed from PresentMon output");
        mon.Wanted = false;
        mon.Tick();
        Assert.False(mon.Running);
    }
}
