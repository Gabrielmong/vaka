using System.Drawing;

namespace SensorPanel.Display;

/// <param name="DeviceName">GDI name, e.g. \\.\DISPLAY3 — can change between boots.</param>
/// <param name="MonitorId">Monitor device interface path (EDID-based), e.g. \\?\DISPLAY#GSM5B7F#5&amp;..#{guid}.</param>
public sealed record MonitorInfo(string DeviceName, string MonitorId, string Description, Rectangle Bounds, bool Primary)
{
    public string Label => $"{Description} — {Bounds.Width}×{Bounds.Height}{(Primary ? " (primary)" : "")}  [{DeviceName.TrimStart('\\', '.')}]";
}

public static class MonitorMatcher
{
    /// <summary>"GSM5B7F" from "\\?\DISPLAY#GSM5B7F#5&amp;2f3b&amp;0&amp;UID4353#{guid}".</summary>
    public static string? HardwareId(string? monitorId)
    {
        if (string.IsNullOrEmpty(monitorId)) return null;
        var parts = monitorId.Split('#');
        return parts.Length >= 3 && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    /// <summary>
    /// Picks the panel monitor. With a saved choice: exact id → same hardware model (unique) →
    /// same resolution (unique, non-primary). Never falls back to an unrelated monitor, so the
    /// panel can't cover the main screen. Without a saved choice: the unique non-primary monitor
    /// matching the layout canvas size.
    /// </summary>
    public static MonitorInfo? Select(IReadOnlyList<MonitorInfo> monitors, string? savedId,
        int savedWidth, int savedHeight, int canvasWidth, int canvasHeight)
    {
        if (!string.IsNullOrEmpty(savedId))
        {
            var exact = monitors.FirstOrDefault(m => string.Equals(m.MonitorId, savedId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;

            var hw = HardwareId(savedId);
            if (hw is not null)
            {
                var sameModel = monitors.Where(m => string.Equals(HardwareId(m.MonitorId), hw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sameModel.Count == 1) return sameModel[0];
            }

            if (savedWidth > 0 && savedHeight > 0)
                return UniqueNonPrimary(monitors, savedWidth, savedHeight);

            return null;
        }

        return UniqueNonPrimary(monitors, canvasWidth, canvasHeight);
    }

    private static MonitorInfo? UniqueNonPrimary(IReadOnlyList<MonitorInfo> monitors, int w, int h)
    {
        var matches = monitors.Where(m => !m.Primary && m.Bounds.Width == w && m.Bounds.Height == h).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
