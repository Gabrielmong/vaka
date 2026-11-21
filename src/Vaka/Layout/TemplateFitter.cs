using System.Drawing;
using System.Text.RegularExpressions;
using Vaka.Sensors;

namespace Vaka.Layout;

/// <summary>Turns a raw template (authored at 800×480 with placeholders) into a layout for this PC and panel.</summary>
public static partial class TemplateFitter
{
    public const int BaseWidth = 800, BaseHeight = 480;

    [GeneratedRegex(@"drive\.\$([1-4])\.")]
    private static partial Regex DriveKey();

    [GeneratedRegex(@"\$drive([1-4])")]
    private static partial Regex DriveLabel();

    /// <summary>
    /// drive.$N. → the Nth drive's letter (left as is when there is no such drive, so it shows --),
    /// $driveN → "C:" or "—", $cpu.name / $gpu.name / $board.name → hardware names.
    /// </summary>
    public static void ApplyPlaceholders(PanelLayout layout, HardwareSummary hw)
    {
        string Keys(string s) => DriveKey().Replace(s, m =>
            int.Parse(m.Groups[1].Value) is var n && n <= hw.Drives.Count ? $"drive.{hw.Drives[n - 1]}." : m.Value);

        foreach (var el in layout.Static.Concat(layout.Widgets))
        {
            if (el is ValueElement { Value: { } v } ve) ve.Value = Keys(v);
            if (el is TextElement t)
                t.Text = DriveLabel().Replace(Keys(t.Text), m =>
                        int.Parse(m.Groups[1].Value) is var n && n <= hw.Drives.Count ? $"{hw.Drives[n - 1]}:" : "—")
                    .Replace("$cpu.name", hw.Cpu ?? "CPU")
                    .Replace("$gpu.name", hw.Gpu ?? "GPU")
                    .Replace("$board.name", hw.Board ?? "Board");
        }
    }

    /// <summary>Landscape panels close to 5:3 (1.4–2.0) are fitted; others keep 800×480 and use Fit scaling.</summary>
    public static bool CanFit(int width, int height)
    {
        if (width <= 0 || height <= 0 || width < height) return false;
        double aspect = (double)width / height;
        return aspect >= 1.4 && aspect <= 2.0;
    }

    /// <summary>
    /// Positions stretch to the new size; rects, lines, bars, graphs and text boxes stretch too, while
    /// ellipses, rings and images keep their shape (scaled by the smaller factor, kept centred).
    /// Font sizes and stroke widths scale by the smaller factor.
    /// </summary>
    public static void Fit(PanelLayout layout, int width, int height)
    {
        if ((width == layout.Canvas.Width && height == layout.Canvas.Height) || !CanFit(width, height)) return;
        float sx = width / (float)layout.Canvas.Width, sy = height / (float)layout.Canvas.Height, s = Math.Min(sx, sy);
        layout.Canvas.Width = width;
        layout.Canvas.Height = height;

        foreach (var el in layout.Static.Concat(layout.Widgets))
        {
            if (el is EllipseElement or RingElement or ImageElement)
            {
                float cx = (el.X + el.W / 2) * sx, cy = (el.Y + el.H / 2) * sy;
                el.W *= s; el.H *= s;
                el.X = cx - el.W / 2; el.Y = cy - el.H / 2;
            }
            else
            {
                el.X *= sx; el.Y *= sy; el.W *= sx; el.H *= sy;
            }

            switch (el)
            {
                case RectElement r: r.Radius *= s; r.StrokeWidth *= s; break;
                case EllipseElement e: e.StrokeWidth *= s; break;
                case LineElement l: l.X2 *= sx; l.Y2 *= sy; l.Width *= s; break;
                case TextElement t: t.Size *= s; break;
                case BarElement b: b.Radius *= s; break;
                case RingElement g: g.Thickness *= s; break;
                case GraphElement g: g.Width *= s; break;
                case ImageElement i: i.Radius *= s; break;
            }
        }
    }

    /// <summary>The size to build for: the panel monitor, else the showing layout's canvas, else 800×480.</summary>
    public static Size TargetSize(Size? panel, CanvasSpec? showing) =>
        panel is { Width: > 0, Height: > 0 } p ? p
        : showing is { Width: > 0, Height: > 0 } c ? new Size(c.Width, c.Height)
        : new Size(BaseWidth, BaseHeight);
}
