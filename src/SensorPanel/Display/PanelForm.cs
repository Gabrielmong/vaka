using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SensorPanel.Layout;
using SensorPanel.Rendering;
using SensorPanel.Sensors;
using SkiaSharp;

namespace SensorPanel.Display;

/// <summary>
/// Borderless, non-activating, topmost window covering the panel display. Renders with Skia
/// into a pixel buffer that GDI blits on paint.
/// </summary>
public sealed class PanelForm : Form
{
    private const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_DISPLAYCHANGE = 0x7E, WM_DPICHANGED = 0x2E0;

    private readonly PanelRenderer _renderer = new();
    private SKBitmap? _skBitmap;
    private Bitmap? _gdiBitmap;

    /// <summary>Raised when Windows reports a display/DPI change; the owner should re-place the window.</summary>
    public event Action? DisplayChanged;

    public PanelForm()
    {
        Text = "Sensor Panel";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void RenderFrame(PanelLayout layout, IReadOnlyDictionary<string, double?> values, DateTime now, string? overlay,
        IHistorySource? history = null)
    {
        if (!EnsureBuffers()) return;
        using (var canvas = new SKCanvas(_skBitmap!))
        {
            _renderer.Render(canvas, _skBitmap!.Width, _skBitmap.Height, layout,
                k => values.TryGetValue(k, out var v) ? v : null, now, overlay, history);
        }
        Invalidate();
    }

    /// <summary>Re-asserts topmost without activating (other topmost windows can cover us).</summary>
    public void BringToTopNoActivate()
    {
        if (IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private bool EnsureBuffers()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return false;
        if (_skBitmap is not null && _skBitmap.Width == w && _skBitmap.Height == h) return true;

        _gdiBitmap?.Dispose();
        _skBitmap?.Dispose();
        _skBitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        // GDI bitmap shares the Skia pixel memory — no copy per frame.
        _gdiBitmap = new Bitmap(w, h, _skBitmap.RowBytes, PixelFormat.Format32bppPArgb, _skBitmap.GetPixels());
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_gdiBitmap is null) { e.Graphics.Clear(Color.Black); return; }
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.DrawImageUnscaled(_gdiBitmap, 0, 0);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_DPICHANGED:
                // Don't let WinForms resize us to the "suggested" rect; we place ourselves.
                DisplayChanged?.Invoke();
                m.Result = IntPtr.Zero;
                return;
            case WM_DISPLAYCHANGE:
                base.WndProc(ref m);
                DisplayChanged?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gdiBitmap?.Dispose();
            _skBitmap?.Dispose();
            _renderer.Dispose();
        }
        base.Dispose(disposing);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
