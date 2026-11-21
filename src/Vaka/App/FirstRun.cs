using System.Drawing;
using Vaka.Display;
using Vaka.Layout;
using Vaka.Sensors;

namespace Vaka.App;

/// <summary>A new install (no layouts at all) starts from the Duo template, set up for this PC.</summary>
public static class FirstRun
{
    public const string TemplateId = "duo";

    /// <summary>
    /// The panel is the only non-primary monitor that looks like a panel (≤ 1280×800 — an ordinary second
    /// monitor must not be taken over); otherwise assume the common 800×480.
    /// </summary>
    public static Size TargetSize(IReadOnlyList<MonitorInfo> monitors)
    {
        var others = monitors.Where(m => !m.Primary && m.Bounds.Width <= 1280 && m.Bounds.Height <= 800).ToList();
        return others.Count == 1 ? others[0].Bounds.Size : new Size(TemplateFitter.BaseWidth, TemplateFitter.BaseHeight);
    }

    public static PanelLayout Create(SensorCatalog catalog, IReadOnlyList<char> drives, Size target)
    {
        try
        {
            return TemplateLibrary.Build(TemplateId, catalog, drives, target, existing: null).Layout;
        }
        catch (Exception ex)
        {
            Log.Write("First-run template build failed, using it without sensors: " + ex);
            return TemplateLibrary.LoadRaw(TemplateId);
        }
    }
}
