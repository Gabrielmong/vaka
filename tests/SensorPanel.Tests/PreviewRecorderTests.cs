using SensorPanel.Editor;
using Xunit;

namespace SensorPanel.Tests;

public class PreviewRecorderTests
{
    [Fact]
    public void ThrottlesPerKeyNotGlobally()
    {
        // The gallery renders ten templates back to back; each one's graph keys must get their own sample.
        var r = new PreviewRecorder();
        var t0 = new DateTime(2026, 11, 20, 12, 0, 0);
        Assert.Equal(["a"], r.Due(["a"], t0));
        Assert.Equal(["b"], r.Due(["b"], t0.AddMilliseconds(10)));
        Assert.Empty(r.Due(["a"], t0.AddMilliseconds(20)));
        Assert.Equal(["a"], r.Due(["a"], t0.AddMilliseconds(950)));
    }
}
