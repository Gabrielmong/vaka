using System.Drawing;
using Vaka.Layout;
using Vaka.Sensors;
using Xunit;

namespace Vaka.Tests;

public class MockSensorsTests
{
    private static readonly MockSensors M = new();
    private static readonly PanelLayout Duo =
        TemplateLibrary.Build("duo", M.CatalogAt(0), MockSensors.Drives, new Size(800, 480), null).Layout;

    private static double? V(double t, string key) => M.CatalogAt(t).Resolve(Duo, key);

    [Fact]
    public void DetectsTheFullRig()
    {
        var (_, d) = TemplateLibrary.Build("duo", M.CatalogAt(0), MockSensors.Drives, new Size(800, 480), null);
        Assert.Contains("3800X", d.Summary.Cpu);
        Assert.Contains("3070", d.Summary.Gpu);
        Assert.Equal(['C', 'D', 'E', 'F'], d.Summary.Drives);
        Assert.Empty(d.Missing);
    }

    [Theory]
    [InlineData(5, "cpu.temp", 38, 3)]
    [InlineData(5, "cpu.load", 4, 3)]
    [InlineData(15, "cpu.temp", 68, 3)]
    [InlineData(15, "gpu.temp", 72, 3)]
    [InlineData(15, "gpu.load", 97, 3)]
    [InlineData(15, "fps", 140, 10)]
    [InlineData(25, "cpu.temp", 84, 3)]
    [InlineData(25, "cpu.load", 100, 3)]
    [InlineData(25, "cpu.core5", 100, 6)]
    public void StatesHitTheirTargets(double t, string key, double expected, double tol)
        => Assert.InRange(V(t, key)!.Value, expected - tol, expected + tol);

    [Fact]
    public void GpuMostlyIdleWhileRendering() => Assert.InRange(V(25, "gpu.load")!.Value, 0, 15);

    [Fact]
    public void FpsIsEmptyOutsideGaming()
    {
        Assert.Null(V(5, "fps"));
        Assert.Null(V(25, "fps"));
    }

    [Fact]
    public void StateScriptWraps()
    {
        Assert.Equal("Idle", MockSensors.StateAt(0));
        Assert.Equal("Gaming", MockSensors.StateAt(10));
        Assert.Equal("Rendering", MockSensors.StateAt(29.9));
        Assert.Equal("Idle", MockSensors.StateAt(30));
        Assert.Equal("Rendering", MockSensors.StateAt(-1));
    }

    [Fact]
    public void LoopIsSeamless()
    {
        foreach (var t in new[] { -120.0, -61, -1, 0, 3, 14, 29 })
        {
            var a = M.CatalogAt(t).Sensors;
            var b = M.CatalogAt(t + MockSensors.LoopSeconds).Sensors;
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.True(Same(a[i].Value, b[i].Value), $"{a[i].Id} {a[i].Name} at t={t}: {a[i].Value} vs {b[i].Value}");
        }
    }

    [Fact]
    public void IsDeterministic()
    {
        var a = new MockSensors().CatalogAt(7).Sensors;
        var b = new MockSensors().CatalogAt(7).Sensors;
        Assert.Equal(a.Select(s => (s.Id, s.Name, s.Value)), b.Select(s => (s.Id, s.Name, s.Value)));
    }

    [Fact]
    public void ClockStaysInOneMinute()
        => Assert.Equal(M.ClockAt(0).ToString("HH:mm"), M.ClockAt(29).ToString("HH:mm"));

    [Fact]
    public void MockValuesStayInRange()
    {
        for (double t = -120; t < 30; t += 0.5)
            foreach (var s in M.CatalogAt(t).Sensors)
            {
                if (s.Value is not { } v) continue;
                if (s.Type == "Load" || s.Id.EndsWith(".usedPct"))
                    Assert.InRange(v, 0, 100);
                else
                    Assert.True(v >= 0, $"{s.Id} {s.Name} = {v} at t={t}");
            }
    }

    private static bool Same(double? a, double? b) =>
        a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) < 1e-6;
}
