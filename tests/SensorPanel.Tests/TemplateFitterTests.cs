using System.Drawing;
using SensorPanel.Layout;
using SensorPanel.Sensors;
using Xunit;

namespace SensorPanel.Tests;

public class TemplateFitterTests
{
    private static HardwareSummary Hw(string? gpu = "RTX 3070") => new("Ryzen 7 3800X", gpu, null, 4, ['C', 'D']);

    [Fact]
    public void DrivePlaceholders()
    {
        var layout = new PanelLayout
        {
            Widgets =
            {
                new BarElement { Value = "drive.$1.usedPct" },
                new TextElement { Text = "$drive2 {drive.$2.freeGB:0} GB" },
                new BarElement { Value = "drive.$3.usedPct" },
                new TextElement { Text = "$drive3" },
            },
        };
        TemplateFitter.ApplyPlaceholders(layout, Hw());
        Assert.Equal("drive.C.usedPct", ((BarElement)layout.Widgets[0]).Value);
        Assert.Equal("D: {drive.D.freeGB:0} GB", ((TextElement)layout.Widgets[1]).Text);
        Assert.Equal("drive.$3.usedPct", ((BarElement)layout.Widgets[2]).Value);
        Assert.Equal("—", ((TextElement)layout.Widgets[3]).Text);
    }

    [Fact]
    public void NamePlaceholders()
    {
        var layout = new PanelLayout
        {
            Static = { new TextElement { Text = "$cpu.name" }, new TextElement { Text = "$gpu.name / $board.name" } },
        };
        TemplateFitter.ApplyPlaceholders(layout, Hw(gpu: null));
        Assert.Equal("Ryzen 7 3800X", ((TextElement)layout.Static[0]).Text);
        Assert.Equal("GPU / Board", ((TextElement)layout.Static[1]).Text);
    }

    [Fact]
    public void FitsTo1024x600()
    {
        var ring = new RingElement { X = 100, Y = 100, W = 200, H = 200, Thickness = 10 };
        var rect = new RectElement { X = 0, Y = 0, W = 800, H = 480, Radius = 8 };
        var text = new TextElement { X = 400, Y = 240, W = 100, H = 30, Size = 20 };
        var line = new LineElement { X = 0, Y = 480, X2 = 800, Y2 = 0, Width = 2 };
        var layout = new PanelLayout { Static = { rect, line }, Widgets = { ring, text } };

        TemplateFitter.Fit(layout, 1024, 600);

        Assert.Equal((1024, 600), (layout.Canvas.Width, layout.Canvas.Height));
        Assert.Equal(250, ring.W, 3);
        Assert.Equal(250, ring.H, 3);
        Assert.Equal(200 * 1.28, ring.X + ring.W / 2, 3);
        Assert.Equal(200 * 1.25, ring.Y + ring.H / 2, 3);
        Assert.Equal(12.5, ring.Thickness, 3);
        Assert.Equal((1024f, 600f), (rect.W, rect.H));
        Assert.Equal(10, rect.Radius, 3);
        Assert.Equal(25, text.Size, 3);
        Assert.Equal((512f, 300f, 128f), (text.X, text.Y, text.W));
        Assert.Equal((0f, 600f, 1024f, 0f), (line.X, line.Y, line.X2, line.Y2));
        Assert.Equal(2.5, line.Width, 3);
    }

    [Theory]
    [InlineData(480, 800)]
    [InlineData(1920, 480)]
    [InlineData(320, 480)]
    public void RejectsPortraitAndBars(int w, int h)
    {
        Assert.False(TemplateFitter.CanFit(w, h));
        var layout = new PanelLayout { Widgets = { new RingElement { X = 10, W = 50, H = 50 } } };
        TemplateFitter.Fit(layout, w, h);
        Assert.Equal((800, 480), (layout.Canvas.Width, layout.Canvas.Height));
        Assert.Equal(50, layout.Widgets[0].W);
    }

    [Theory]
    [InlineData(1024, 600)]
    [InlineData(1280, 800)]
    [InlineData(800, 480)]
    [InlineData(1280, 720)]
    public void AcceptsCommonLandscape(int w, int h) => Assert.True(TemplateFitter.CanFit(w, h));

    [Fact]
    public void TargetSizeFallsBackToCanvas()
    {
        Assert.Equal(new Size(1024, 600), TemplateFitter.TargetSize(null, new CanvasSpec { Width = 1024, Height = 600 }));
        Assert.Equal(new Size(800, 480), TemplateFitter.TargetSize(null, null));
        Assert.Equal(new Size(1280, 800), TemplateFitter.TargetSize(new Size(1280, 800), new CanvasSpec()));
    }
}
