using Vaka.Layout;

namespace Vaka.Rendering;

public static class Thresholds
{
    /// <summary>Returns the color of the highest threshold the value reaches, or the base color.</summary>
    public static string Resolve(string baseColor, IReadOnlyList<Threshold>? thresholds, double? value)
    {
        if (thresholds is null || thresholds.Count == 0 || value is null || double.IsNaN(value.Value))
            return baseColor;

        string color = baseColor;
        double best = double.NegativeInfinity;
        foreach (var t in thresholds)
        {
            if (value.Value >= t.Gte && t.Gte >= best)
            {
                best = t.Gte;
                color = t.Color;
            }
        }
        return color;
    }

    /// <summary>The color of the highest threshold the value reaches, if any.</summary>
    public static bool TryMatch(IReadOnlyList<Threshold>? thresholds, double? value, out string color)
    {
        color = "";
        if (thresholds is null || thresholds.Count == 0) return false;
        color = Resolve("", thresholds, value);
        return color.Length > 0;
    }

    /// <summary>Normalised 0..1 position of value between min and max (0 when unknown).</summary>
    public static float Fraction(double? value, double min, double max)
    {
        if (value is null || double.IsNaN(value.Value) || max <= min) return 0f;
        return (float)Math.Clamp((value.Value - min) / (max - min), 0, 1);
    }
}
