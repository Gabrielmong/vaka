using System.Text.Json;
using System.Text.Json.Serialization;
using SensorPanel.Rendering;

namespace SensorPanel.Layout;

public static class LayoutSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static PanelLayout Parse(string json)
    {
        var layout = JsonSerializer.Deserialize<PanelLayout>(json, Options)
                     ?? throw new JsonException("Layout file is empty.");
        if (layout.Canvas.Width <= 0 || layout.Canvas.Height <= 0)
            throw new JsonException("Canvas width and height must be positive.");
        return layout;
    }

    public static string Serialize(PanelLayout layout) => JsonSerializer.Serialize(layout, Options);

    /// <summary>All sensor keys the widget layer needs (bindings + template placeholders).</summary>
    public static IReadOnlySet<string> GetSensorKeys(PanelLayout layout)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in layout.Widgets)
        {
            if (element is ValueElement { Value: { Length: > 0 } v })
                keys.Add(v);
            if (element is TextElement text)
                foreach (var k in Template.Parse(text.Text).Keys)
                    keys.Add(k);
        }
        return keys;
    }
}
