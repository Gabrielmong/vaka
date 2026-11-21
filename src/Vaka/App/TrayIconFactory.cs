using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Vaka.App;

/// <summary>Draws a small ring-gauge icon so we don't need an .ico asset.</summary>
internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(30, 30, 30));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var track = new Pen(Color.FromArgb(80, 80, 80), 4);
            g.DrawEllipse(track, 6, 6, 20, 20);
            using var arc = new Pen(Color.White, 4);
            g.DrawArc(arc, 6, 6, 20, 20, 140, 250);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
