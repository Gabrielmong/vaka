using System.Globalization;
using SkiaSharp;

namespace SensorPanel.Rendering;

/// <summary>Parses CSS-style hex colors: #rgb, #rrggbb, #rrggbbaa.</summary>
public static class ColorParser
{
    public static bool TryParse(string? s, out SKColor color)
    {
        color = SKColors.Transparent;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var hex = s.Trim();
        if (hex[0] == '#') hex = hex[1..];

        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

        if (hex.Length is not (6 or 8)) return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;

        color = hex.Length == 6
            ? new SKColor((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255)
            : new SKColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static SKColor Parse(string? s, SKColor fallback) => TryParse(s, out var c) ? c : fallback;
}
