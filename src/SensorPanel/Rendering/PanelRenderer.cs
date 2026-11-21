using System.Globalization;
using SensorPanel.Layout;
using SensorPanel.Sensors;
using SkiaSharp;

namespace SensorPanel.Rendering;

/// <summary>
/// Draws a layout onto a Skia canvas. The static layer is baked once into an image per
/// (layout, size) and reused; only widgets are drawn every frame.
/// </summary>
public sealed class PanelRenderer : IDisposable
{
    private readonly Dictionary<(string, int), SKTypeface> _typefaces = new();
    private SKImage? _static;
    private PanelLayout? _staticLayout;
    private int _staticW, _staticH;

    private readonly Dictionary<string, (DateTime Stamp, SKImage? Image)> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HistorySample> _samples = new();
    private IHistorySource? _history;

    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Where image elements load their pictures from.</summary>
    public ImageStore Images { get; set; } = new(AppPaths.Images);

    public void Render(SKCanvas canvas, int width, int height, PanelLayout layout,
        Func<string, double?> values, DateTime now, string? overlay = null, IHistorySource? history = null)
    {
        _history = history;
        EnsureStatic(layout, width, height);
        canvas.DrawImage(_static!, 0, 0, SKSamplingOptions.Default, null);

        canvas.Save();
        ApplyTransform(canvas, layout, width, height);
        foreach (var e in layout.Widgets)
            if (!e.Hidden) Draw(canvas, e, values, now, dynamic: true);
        canvas.Restore();

        if (!string.IsNullOrEmpty(overlay))
            DrawOverlay(canvas, width, height, overlay);
    }

    /// <summary>Draws only the background (canvas color + static layer), e.g. for export.</summary>
    public void RenderStatic(SKCanvas canvas, int width, int height, PanelLayout layout)
    {
        EnsureStatic(layout, width, height);
        canvas.DrawImage(_static!, 0, 0, SKSamplingOptions.Default, null);
    }

    /// <summary>Forces the static layer to be re-baked on the next frame.</summary>
    public void InvalidateStatic() => _staticLayout = null;

    private void EnsureStatic(PanelLayout layout, int width, int height)
    {
        if (_static is not null && ReferenceEquals(_staticLayout, layout) && _staticW == width && _staticH == height)
            return;

        _static?.Dispose();
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(ColorParser.Parse(layout.Canvas.Background, SKColors.Black));
        c.Save();
        ApplyTransform(c, layout, width, height);
        foreach (var e in layout.Static)
            if (!e.Hidden) Draw(c, e, _ => null, DateTime.Now, dynamic: false);
        c.Restore();
        _static = surface.Snapshot();
        _staticLayout = layout;
        _staticW = width;
        _staticH = height;
    }

    private static void ApplyTransform(SKCanvas c, PanelLayout layout, int width, int height)
    {
        var t = ScaleTransform.Compute(layout.Canvas.Width, layout.Canvas.Height, width, height, layout.Scale);
        c.Translate(t.OffsetX, t.OffsetY);
        c.Scale(t.ScaleX, t.ScaleY);
        c.ClipRect(new SKRect(0, 0, layout.Canvas.Width, layout.Canvas.Height));
    }

    private void Draw(SKCanvas c, Element e, Func<string, double?> values, DateTime now, bool dynamic)
    {
        switch (e)
        {
            case RectElement r: DrawRect(c, r); break;
            case EllipseElement el: DrawEllipse(c, el); break;
            case LineElement l: DrawLine(c, l); break;
            case TextElement t: DrawText(c, t, values, now, dynamic); break;
            case BarElement b: DrawBar(c, b, values); break;
            case RingElement ring: DrawRing(c, ring, values); break;
            case GraphElement g: DrawGraph(c, g, values, now); break;
            case ImageElement img: DrawImage(c, img); break;
        }
    }

    // ── Shapes ────────────────────────────────────────────────────────────

    private static void DrawRect(SKCanvas c, RectElement r)
    {
        var rect = SKRect.Create(r.X, r.Y, r.W, r.H);
        if ((GradientFill(r.Gradient, rect, r.Opacity) ?? Fill(r.Fill, r.Opacity)) is { } fill)
        {
            using (fill) DrawRoundedOrSquare(c, rect, r.Radius, fill);
        }
        if (Stroke(r.Stroke, r.StrokeWidth, r.Opacity) is { } stroke)
        {
            using (stroke) DrawRoundedOrSquare(c, rect, r.Radius, stroke);
        }
    }

    private static void DrawEllipse(SKCanvas c, EllipseElement el)
    {
        var rect = SKRect.Create(el.X, el.Y, el.W, el.H);
        if ((GradientFill(el.Gradient, rect, el.Opacity) ?? Fill(el.Fill, el.Opacity)) is { } fill) { using (fill) c.DrawOval(rect, fill); }
        if (Stroke(el.Stroke, el.StrokeWidth, el.Opacity) is { } stroke) { using (stroke) c.DrawOval(rect, stroke); }
    }

    private static void DrawLine(SKCanvas c, LineElement l)
    {
        using var p = Stroke(l.Color, l.Width, l.Opacity)!;
        p.StrokeCap = l.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt;
        c.DrawLine(l.X, l.Y, l.X2, l.Y2, p);
    }

    // ── Text ──────────────────────────────────────────────────────────────

    private void DrawText(SKCanvas c, TextElement t, Func<string, double?> values, DateTime now, bool dynamic)
    {
        using var tl = LayoutText(t, values, now, dynamic);
        if (tl.Text.Length == 0) return;

        double? value = t.Value is { Length: > 0 } key ? values(key) : null;
        using var paint = Fill(Thresholds.Resolve(t.Color, t.Thresholds, value), t.Opacity) ?? new SKPaint();
        c.DrawText(tl.Text, tl.AnchorX, tl.Baseline, tl.Align, tl.Font, paint);
    }

    private sealed class TextLayout(string text, SKFont font, float anchorX, float baseline, SKTextAlign align, SKRect bounds)
        : IDisposable
    {
        public string Text { get; } = text;
        public SKFont Font { get; } = font;
        public float AnchorX { get; } = anchorX;
        public float Baseline { get; } = baseline;
        public SKTextAlign Align { get; } = align;
        public SKRect Bounds { get; } = bounds;
        public void Dispose() => Font.Dispose();
    }

    private TextLayout LayoutText(TextElement t, Func<string, double?> values, DateTime now, bool dynamic)
    {
        var text = dynamic ? Template.Parse(t.Text).Format(values, now, Culture) : t.Text;
        var font = new SKFont(GetTypeface(t.Font, t.Weight), t.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };

        var m = font.Metrics;
        float capHeight = m.CapHeight > 0 ? m.CapHeight : -m.Ascent * 0.72f;
        float baseline = t.Y + capHeight;

        var (x, align) = t.Align switch
        {
            TextAlign.Center => (t.W > 0 ? t.X + t.W / 2 : t.X, SKTextAlign.Center),
            TextAlign.Right => (t.W > 0 ? t.X + t.W : t.X, SKTextAlign.Right),
            _ => (t.X, SKTextAlign.Left),
        };

        float width = text.Length > 0 ? font.MeasureText(text) : 0;
        float left = align switch
        {
            SKTextAlign.Center => x - width / 2,
            SKTextAlign.Right => x - width,
            _ => x,
        };
        float height = capHeight + m.Descent;
        var bounds = t.W > 0
            ? SKRect.Create(Math.Min(t.X, left), t.Y, Math.Max(t.W, width), height)
            : SKRect.Create(left, t.Y, Math.Max(width, 1), height);

        return new TextLayout(text, font, x, baseline, align, bounds);
    }

    /// <summary>Element bounds in canvas units ([x, y, w, h]); null for hidden elements.</summary>
    public (float[]?[] Static, float[]?[] Widgets) MeasureBounds(PanelLayout layout, Func<string, double?> values, DateTime now) =>
        (layout.Static.Select(e => Bounds(e, values, now, dynamic: false)).ToArray(),
         layout.Widgets.Select(e => Bounds(e, values, now, dynamic: true)).ToArray());

    private float[]? Bounds(Element e, Func<string, double?> values, DateTime now, bool dynamic)
    {
        SKRect r;
        switch (e)
        {
            case LineElement l:
                float half = Math.Max(l.Width / 2, 1);
                r = new SKRect(Math.Min(l.X, l.X2) - half, Math.Min(l.Y, l.Y2) - half,
                               Math.Max(l.X, l.X2) + half, Math.Max(l.Y, l.Y2) + half);
                break;
            case TextElement t:
                using (var tl = LayoutText(t, values, now, dynamic)) r = tl.Bounds;
                break;
            default:
                r = SKRect.Create(e.X, e.Y, e.W, e.H);
                break;
        }
        return [r.Left, r.Top, r.Width, r.Height];
    }

    private SKTypeface GetTypeface(string family, int weight)
    {
        var key = (family, weight);
        if (!_typefaces.TryGetValue(key, out var tf))
        {
            tf = SKTypeface.FromFamilyName(family, (SKFontStyleWeight)weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                 ?? SKTypeface.Default;
            _typefaces[key] = tf;
        }
        return tf;
    }

    // ── Value widgets ─────────────────────────────────────────────────────

    private static void DrawBar(SKCanvas c, BarElement b, Func<string, double?> values)
    {
        double? v = b.Value is { Length: > 0 } key ? values(key) : null;
        float f = Thresholds.Fraction(v, b.Min, b.Max);
        var track = SKRect.Create(b.X, b.Y, b.W, b.H);

        using (var tp = Fill(b.Track, b.Opacity)) if (tp is not null) DrawRoundedOrSquare(c, track, b.Radius, tp);
        if (f <= 0) return;

        var fill = b.Vertical
            ? SKRect.Create(b.X, b.Y + b.H * (1 - f), b.W, b.H * f)
            : SKRect.Create(b.X, b.Y, b.W * f, b.H);
        SKPaint? fp;
        if (Thresholds.TryMatch(b.Thresholds, v, out var hit)) fp = Fill(hit, b.Opacity);
        else
        {
            // The gradient spans the whole track, so the fill reveals more of it as the value rises.
            var g = b.Gradient is null ? null : CloneWithAngle(b.Gradient, b.Vertical ? 270 : 0);
            fp = GradientFill(g, track, b.Opacity) ?? Fill(b.Color, b.Opacity);
        }
        using (fp) if (fp is not null) DrawRoundedOrSquare(c, fill, b.Radius, fp);
    }

    private static void DrawRing(SKCanvas c, RingElement r, Func<string, double?> values)
    {
        double? v = r.Value is { Length: > 0 } key ? values(key) : null;
        float f = Thresholds.Fraction(v, r.Min, r.Max);

        float size = Math.Min(r.W, r.H);
        float radius = (size - r.Thickness) / 2f;
        if (radius <= 0) return;
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        var cap = r.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt;

        using (var tp = Stroke(r.Track, r.Thickness, r.Opacity))
        {
            if (tp is not null)
            {
                tp.StrokeCap = cap;
                if (Math.Abs(r.Sweep) >= 360) c.DrawOval(oval, tp);
                else c.DrawArc(oval, r.StartAngle, r.Sweep, false, tp);
            }
        }

        var color = Thresholds.Resolve(r.Color, r.Thresholds, v);
        if (f > 0)
        {
            using var vp = Stroke(color, r.Thickness, r.Opacity)!;
            vp.StrokeCap = cap;
            if (r.Gradient is { } rg && !Thresholds.TryMatch(r.Thresholds, v, out _))
                vp.Shader = SweepShader(rg, cx, cy, r.StartAngle, r.Sweep, r.Opacity);
            c.DrawArc(oval, r.StartAngle, r.Sweep * f, false, vp);
        }

        if (r.StartTick)
        {
            using var tick = Stroke(color, 1.5f, r.Opacity)!;
            double a = r.StartAngle * Math.PI / 180;
            float inner = radius - r.Thickness / 2f - 4, outer = radius + r.Thickness / 2f + 2;
            c.DrawLine(cx + inner * (float)Math.Cos(a), cy + inner * (float)Math.Sin(a),
                       cx + outer * (float)Math.Cos(a), cy + outer * (float)Math.Sin(a), tick);
        }
    }

    // ── Graph ─────────────────────────────────────────────────────────────

    private void DrawGraph(SKCanvas c, GraphElement g, Func<string, double?> values, DateTime now)
    {
        var box = SKRect.Create(g.X, g.Y, g.W, g.H);
        if (box.Width <= 0 || box.Height <= 0) return;

        using (var bg = Fill(g.Background, g.Opacity)) if (bg is not null) c.DrawRect(box, bg);
        if (g.GridLines > 0 && Stroke(g.GridColor, 1, g.Opacity) is { } grid)
        {
            using (grid)
                for (int i = 1; i <= g.GridLines; i++)
                {
                    float y = MathF.Round(g.Y + g.H * i / (g.GridLines + 1)) + 0.5f;
                    c.DrawLine(g.X, y, g.X + g.W, y, grid);
                }
        }

        if (g.Value is not { Length: > 0 } key || _history is null) return;
        double duration = Math.Clamp(g.Duration, 5, 3600);
        var start = now.AddSeconds(-duration);
        _samples.Clear();
        // Include one sample before the window so the line reaches the left edge.
        _history.Read(key, start.AddSeconds(-5), _samples);
        if (_samples.Count == 0) return;

        double min = g.Min, max = g.Max;
        if (g.AutoScale)
        {
            min = double.PositiveInfinity; max = double.NegativeInfinity;
            foreach (var s in _samples)
                if (s.Value is { } v) { min = Math.Min(min, v); max = Math.Max(max, v); }
            if (double.IsInfinity(min)) return;
            double pad = Math.Max((max - min) * 0.1, Math.Max(Math.Abs(max) * 0.02, 0.5));
            min -= pad; max += pad;
        }
        if (max <= min) max = min + 1;

        float X(DateTime t) => g.X + (float)((t - start).TotalSeconds / duration) * g.W;
        float Y(double v) => g.Y + g.H - (float)Math.Clamp((v - min) / (max - min), 0, 1) * g.H;

        // Missing samples split the series into separate runs.
        using var line = new SKPathBuilder();
        using var area = new SKPathBuilder();
        bool open = false;
        float runStartX = 0, lastX = 0, bottom = g.Y + g.H;
        foreach (var s in _samples)
        {
            if (s.Value is not { } v)
            {
                if (open) { area.LineTo(lastX, bottom); area.LineTo(runStartX, bottom); area.Close(); open = false; }
                continue;
            }
            float x = X(s.Time), y = Y(v);
            if (!open)
            {
                line.MoveTo(x, y);
                area.MoveTo(x, y);
                runStartX = x;
                open = true;
            }
            else
            {
                line.LineTo(x, y);
                area.LineTo(x, y);
            }
            lastX = x;
        }
        if (open) { area.LineTo(lastX, bottom); area.LineTo(runStartX, bottom); area.Close(); }

        c.Save();
        c.ClipRect(box);
        if ((GradientFill(g.Gradient, box, g.Opacity) ?? Fill(g.Fill, g.Opacity)) is { } af)
        {
            using (af) using (var ap = area.Detach()) c.DrawPath(ap, af);
        }
        double? latest = _samples[^1].Value ?? values(key);
        if (Stroke(Thresholds.Resolve(g.Color, g.Thresholds, latest), g.Width, g.Opacity) is { } lp)
        {
            using (lp)
            {
                lp.StrokeJoin = SKStrokeJoin.Round;
                lp.StrokeCap = SKStrokeCap.Round;
                using var lpath = line.Detach();
                c.DrawPath(lpath, lp);
            }
        }
        c.Restore();
    }

    // ── Image ─────────────────────────────────────────────────────────────

    private void DrawImage(SKCanvas c, ImageElement e)
    {
        var image = LoadImage(e.Src);
        var box = SKRect.Create(e.X, e.Y, e.W, e.H);
        if (image is null || box.Width <= 0 || box.Height <= 0) return;

        float iw = image.Width, ih = image.Height;
        SKRect dest = box, src = SKRect.Create(0, 0, iw, ih);
        if (e.Fit == ImageFit.Contain)
        {
            float s = Math.Min(box.Width / iw, box.Height / ih);
            dest = SKRect.Create(box.MidX - iw * s / 2, box.MidY - ih * s / 2, iw * s, ih * s);
        }
        else if (e.Fit == ImageFit.Cover)
        {
            float s = Math.Max(box.Width / iw, box.Height / ih);
            float sw = box.Width / s, sh = box.Height / s;
            src = SKRect.Create((iw - sw) / 2, (ih - sh) / 2, sw, sh);
        }

        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(255 * Math.Clamp(e.Opacity, 0, 1))) };
        c.Save();
        if (e.Radius > 0) c.ClipRoundRect(new SKRoundRect(dest, e.Radius), antialias: true);
        c.DrawImage(image, src, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        c.Restore();
    }

    /// <summary>Decoded images, reloaded when the file changes.</summary>
    private SKImage? LoadImage(string name)
    {
        var path = Images.PathFor(name);
        if (path is null) return null;
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_images.TryGetValue(name, out var cached) && cached.Stamp == stamp) return cached.Image;

        cached.Image?.Dispose();
        SKImage? image = null;
        if (stamp != DateTime.MinValue)
        {
            try { image = SKImage.FromEncodedData(path)?.ToRasterImage(); }
            catch (Exception ex) { Log.Write($"Image '{name}' failed to load: {ex.Message}"); }
        }
        _images[name] = (stamp, image);
        return image;
    }

    // ── Gradients ─────────────────────────────────────────────────────────

    private static Gradient CloneWithAngle(Gradient g, float angle) =>
        new() { Kind = g.Kind, Angle = angle, Colors = g.Colors, Stops = g.Stops };

    private static bool GradientStops(Gradient g, float opacity, out SKColor[] colors, out float[]? stops)
    {
        colors = g.Colors.Select(col => WithOpacity(ColorParser.Parse(col, SKColors.Transparent), opacity)).ToArray();
        stops = g.Stops is { } s && s.Count == colors.Length ? s.Select(x => Math.Clamp(x, 0f, 1f)).ToArray() : null;
        return colors.Length >= 2;
    }

    /// <summary>A fill paint with a linear/radial gradient over <paramref name="box"/>, or null.</summary>
    private static SKPaint? GradientFill(Gradient? g, SKRect box, float opacity)
    {
        if (g is null || !GradientStops(g, opacity, out var colors, out var stops)) return null;

        SKShader shader;
        if (g.Kind == GradientKind.Radial)
        {
            float radius = Math.Max(1, Math.Max(box.Width, box.Height) / 2);
            shader = SKShader.CreateRadialGradient(new SKPoint(box.MidX, box.MidY), radius, colors, stops, SKShaderTileMode.Clamp);
        }
        else
        {
            double a = g.Angle * Math.PI / 180;
            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
            // Half the box's extent along the gradient direction, so the stops span corner to corner.
            float half = Math.Max(0.5f, (Math.Abs(box.Width * dx) + Math.Abs(box.Height * dy)) / 2);
            var p0 = new SKPoint(box.MidX - dx * half, box.MidY - dy * half);
            var p1 = new SKPoint(box.MidX + dx * half, box.MidY + dy * half);
            shader = SKShader.CreateLinearGradient(p0, p1, colors, stops, SKShaderTileMode.Clamp);
        }
        return new SKPaint { Shader = shader, IsAntialias = true, Style = SKPaintStyle.Fill };
    }

    /// <summary>Gradient along an arc starting at <paramref name="start"/> spanning <paramref name="sweep"/> degrees.</summary>
    private static SKShader? SweepShader(Gradient g, float cx, float cy, float start, float sweep, float opacity)
    {
        if (!GradientStops(g, opacity, out var colors, out var stops)) return null;
        float span = Math.Clamp(Math.Abs(sweep), 1, 360) / 360f;
        int n = colors.Length;
        var pos = new float[n];
        for (int i = 0; i < n; i++) pos[i] = (stops?[i] ?? (float)i / (n - 1)) * span;
        if (sweep < 0)
        {
            // Counter-clockwise arc: build it clockwise from the far end with the colors reversed.
            Array.Reverse(colors);
            for (int i = 0; i < n; i++) pos[i] = span - pos[i];
            Array.Reverse(pos);
            start += sweep;
        }
        if (span < 1)
        {
            // Outside the arc (round caps), hold the end color past the end and the start color
            // before the start, switching halfway through the gap instead of wrapping.
            float mid = (span + 1) / 2;
            colors = [.. colors, colors[n - 1], colors[0]];
            pos = [.. pos, mid, mid];
        }
        return SKShader.CreateSweepGradient(new SKPoint(cx, cy), colors, pos, SKMatrix.CreateRotationDegrees(start, cx, cy));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void DrawRoundedOrSquare(SKCanvas c, SKRect rect, float radius, SKPaint paint)
    {
        if (radius > 0) c.DrawRoundRect(rect, radius, radius, paint);
        else c.DrawRect(rect, paint);
    }

    private static SKPaint? Fill(string? color, float opacity)
    {
        if (!ColorParser.TryParse(color, out var col)) return null;
        return new SKPaint { Color = WithOpacity(col, opacity), IsAntialias = true, Style = SKPaintStyle.Fill };
    }

    private static SKPaint? Stroke(string? color, float width, float opacity)
    {
        if (!ColorParser.TryParse(color, out var col) || width <= 0) return null;
        return new SKPaint { Color = WithOpacity(col, opacity), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
    }

    private static SKColor WithOpacity(SKColor c, float opacity) =>
        opacity >= 1 ? c : c.WithAlpha((byte)(c.Alpha * Math.Clamp(opacity, 0, 1)));

    private void DrawOverlay(SKCanvas c, int width, int height, string message)
    {
        using var font = new SKFont(GetTypeface("Segoe UI", 600), 14) { Edging = SKFontEdging.Antialias };
        using var bg = new SKPaint { Color = new SKColor(160, 20, 20, 220) };
        using var fg = new SKPaint { Color = SKColors.White, IsAntialias = true };
        float h = 24;
        c.DrawRect(0, height - h, width, h, bg);
        c.DrawText(message, 8, height - 7, SKTextAlign.Left, font, fg);
    }

    public void Dispose()
    {
        _static?.Dispose();
        foreach (var (_, img) in _images.Values) img?.Dispose();
        _images.Clear();
        foreach (var tf in _typefaces.Values)
            if (!ReferenceEquals(tf, SKTypeface.Default)) tf.Dispose();
        _typefaces.Clear();
    }
}
