using System.Drawing;
using System.Text.Json;
using SensorPanel.Sensors;

namespace SensorPanel.Layout;

public sealed record TemplateInfo(string Id, string Name, string Description, string[] Shows);

/// <summary>
/// Built-in layout templates (embedded templates/*.json, authored at 800×480 against the
/// <see cref="SensorDetector.Vocabulary"/> keys). <see cref="Build"/> makes one fit this PC.
/// </summary>
public static class TemplateLibrary
{
    private static readonly Lazy<IReadOnlyList<TemplateInfo>> Manifest = new(() =>
        JsonSerializer.Deserialize<List<TemplateInfo>>(Read("manifest"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? []);

    public static IReadOnlyList<TemplateInfo> All => Manifest.Value;

    public static PanelLayout LoadRaw(string id)
    {
        if (!All.Any(t => t.Id == id)) throw new KeyNotFoundException($"Unknown template “{id}”.");
        return LayoutSerializer.Parse(Read(id));
    }

    public static (PanelLayout Layout, Detection Detection) Build(string id, SensorCatalog catalog,
        IReadOnlyList<char> drives, Size target, PanelLayout? existing)
    {
        var layout = LoadRaw(id);
        var detection = SensorDetector.Detect(catalog, drives, existing);
        layout.Aliases = detection.Aliases;
        if (existing is not null)
            foreach (var (k, v) in existing.SensorNames) layout.SensorNames.TryAdd(k, v);
        TemplateFitter.ApplyPlaceholders(layout, detection.Summary);
        TemplateFitter.Fit(layout, target.Width, target.Height);
        return (layout, detection);
    }

    /// <summary>The Windows drive first, then other ready fixed drives in letter order (at most 4).</summary>
    public static IReadOnlyList<char> SystemDrives()
    {
        var system = char.ToUpperInvariant(Path.GetPathRoot(Environment.SystemDirectory)?.FirstOrDefault() ?? 'C');
        var list = new List<char> { system };
        try
        {
            foreach (var d in DriveInfo.GetDrives().OrderBy(d => d.Name))
            {
                char letter = char.ToUpperInvariant(d.Name[0]);
                bool ok;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
                if (ok && !list.Contains(letter)) list.Add(letter);
            }
        }
        catch (IOException) { /* keep the system drive */ }
        return list.Take(4).ToArray();
    }

    private static string Read(string name)
    {
        using var stream = typeof(TemplateLibrary).Assembly.GetManifestResourceStream($"templates/{name}.json")
                           ?? throw new KeyNotFoundException($"Template resource “{name}” is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
