using Vaka.Layout;

namespace Vaka.Rendering;

public readonly record struct ScaleResult(float ScaleX, float ScaleY, float OffsetX, float OffsetY);

public static class ScaleTransform
{
    /// <summary>Maps a canvas of (cw, ch) units onto a target of (tw, th) pixels.</summary>
    public static ScaleResult Compute(float cw, float ch, float tw, float th, ScaleMode mode)
    {
        if (cw <= 0 || ch <= 0 || tw <= 0 || th <= 0) return new ScaleResult(1, 1, 0, 0);

        if (mode == ScaleMode.Stretch)
            return new ScaleResult(tw / cw, th / ch, 0, 0);

        float sx = tw / cw, sy = th / ch;
        float s = mode == ScaleMode.Fill ? Math.Max(sx, sy) : Math.Min(sx, sy);
        return new ScaleResult(s, s, (tw - cw * s) / 2f, (th - ch * s) / 2f);
    }
}
