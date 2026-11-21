using System.Text.Json.Serialization;

namespace Vaka.Layout;

/// <summary>Root of a layout file. Coordinates are in canvas units (see <see cref="Canvas"/>).</summary>
public sealed class PanelLayout
{
    public int Version { get; set; } = 1;
    public CanvasSpec Canvas { get; set; } = new();
    public ScaleMode Scale { get; set; } = ScaleMode.Fit;

    /// <summary>Friendly sensor keys (e.g. "cpu.temp") mapped to LibreHardwareMonitor sensors.</summary>
    public Dictionary<string, SensorAlias> Aliases { get; set; } = new();

    /// <summary>Display names for raw sensor identifiers (e.g. "Fan #6" → "AIO Pump").</summary>
    public Dictionary<string, string> SensorNames { get; set; } = new();

    /// <summary>Elements drawn once and cached as the background image.</summary>
    public List<Element> Static { get; set; } = new();

    /// <summary>Elements redrawn every tick on top of the static layer.</summary>
    public List<Element> Widgets { get; set; } = new();
}

public sealed class CanvasSpec
{
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 480;
    public string Background { get; set; } = "#000000";
}

public enum ScaleMode
{
    /// <summary>Uniform scale, letterboxed.</summary>
    Fit,
    /// <summary>Uniform scale, cropped.</summary>
    Fill,
    /// <summary>Non-uniform scale to exactly cover the display.</summary>
    Stretch,
}

public sealed class SensorAlias
{
    /// <summary>LibreHardwareMonitor sensor identifier, e.g. "/amdcpu/0/temperature/2".</summary>
    public string Sensor { get; set; } = "";

    /// <summary>Optional sensor name, used when several sensors share an identifier.</summary>
    public string? Name { get; set; }

    /// <summary>value = raw * Scale + Offset (e.g. MHz → GHz with Scale 0.001).</summary>
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }

    /// <summary>
    /// Computed alias: a <see cref="Formula"/> over other keys, e.g. "cpu.power + gpu.power + 50".
    /// When set, <see cref="Sensor"/>, scale and offset are ignored.
    /// </summary>
    public string? Expr { get; set; }
}

public sealed class Threshold
{
    /// <summary>Applies when value &gt;= Gte. The highest matching threshold wins.</summary>
    public double Gte { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public enum TextAlign { Left, Center, Right }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RectElement), "rect")]
[JsonDerivedType(typeof(EllipseElement), "ellipse")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(BarElement), "bar")]
[JsonDerivedType(typeof(RingElement), "ring")]
[JsonDerivedType(typeof(GraphElement), "graph")]
[JsonDerivedType(typeof(ImageElement), "image")]
public abstract class Element
{
    public string? Id { get; set; }
    /// <summary>
    /// Editor-only: elements sharing a group name select and move together. Groups may span
    /// the static and widget lists (e.g. a card background plus its live widgets). The renderer ignores it.
    /// </summary>
    public string? Group { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float Opacity { get; set; } = 1;
    public bool Hidden { get; set; }
}

/// <summary>Base for elements bound to a sensor value.</summary>
public abstract class ValueElement : Element
{
    /// <summary>Sensor key: an alias, a raw identifier ("/..."), or a built-in ("drive.C.usedPct").</summary>
    public string? Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public string Color { get; set; } = "#ffffff";
    public List<Threshold>? Thresholds { get; set; }
}

public enum GradientKind { Linear, Radial }

/// <summary>
/// Color gradient. On rects/ellipses/graph areas it follows <see cref="Angle"/>
/// (0 = left→right, 90 = top→bottom); on bars it follows the fill direction and on rings the arc.
/// </summary>
public sealed class Gradient
{
    public GradientKind Kind { get; set; } = GradientKind.Linear;
    public float Angle { get; set; } = 90;
    public List<string> Colors { get; set; } = new();
    /// <summary>Optional 0..1 positions, one per color; evenly spaced when omitted.</summary>
    public List<float>? Stops { get; set; }
}

public sealed class RectElement : Element
{
    public string? Fill { get; set; }
    /// <summary>Overrides <see cref="Fill"/> when set.</summary>
    public Gradient? Gradient { get; set; }
    public string? Stroke { get; set; }
    public float StrokeWidth { get; set; } = 1;
    public float Radius { get; set; }
}

public sealed class EllipseElement : Element
{
    public string? Fill { get; set; }
    public Gradient? Gradient { get; set; }
    public string? Stroke { get; set; }
    public float StrokeWidth { get; set; } = 1;
}

public sealed class LineElement : Element
{
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public string Color { get; set; } = "#ffffff";
    public float Width { get; set; } = 1;
    public bool RoundCap { get; set; }
}

/// <summary>
/// Text. In the widget layer, <see cref="Text"/> is a template: "{cpu.temp:0}°C", "{time:HH:mm}".
/// Y is the visual top of capital letters / digits.
/// </summary>
public sealed class TextElement : ValueElement
{
    public string Text { get; set; } = "";
    public string Font { get; set; } = "Segoe UI";
    public float Size { get; set; } = 16;
    public int Weight { get; set; } = 400;
    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class BarElement : ValueElement
{
    public string Track { get; set; } = "#3a3a3a";
    public float Radius { get; set; }
    public bool Vertical { get; set; }
    /// <summary>Fill gradient spanning the whole track; a matching threshold color wins.</summary>
    public Gradient? Gradient { get; set; }
}

/// <summary>Arc gauge inside the (X, Y, W, H) box. Angles in degrees, 0 = 3 o'clock, clockwise.</summary>
public sealed class RingElement : ValueElement
{
    public string Track { get; set; } = "#3a3a3a";
    public float Thickness { get; set; } = 12;
    public float StartAngle { get; set; } = 140;
    public float Sweep { get; set; } = 360;
    public bool RoundCap { get; set; }
    public bool StartTick { get; set; }
    /// <summary>Gradient along the arc; a matching threshold color wins.</summary>
    public Gradient? Gradient { get; set; }
}

/// <summary>Line/area chart of a sensor's recent history inside the (X, Y, W, H) box.</summary>
public sealed class GraphElement : ValueElement
{
    /// <summary>Time window shown, in seconds.</summary>
    public int Duration { get; set; } = 60;
    /// <summary>Fit the vertical range to the visible data instead of Min/Max.</summary>
    public bool AutoScale { get; set; }
    /// <summary>Line width; 0 hides the line.</summary>
    public float Width { get; set; } = 1.5f;
    /// <summary>Area fill under the line (null = no area).</summary>
    public string? Fill { get; set; }
    public Gradient? Gradient { get; set; }
    public string? Background { get; set; }
    /// <summary>Number of horizontal grid lines.</summary>
    public int GridLines { get; set; }
    public string GridColor { get; set; } = "#ffffff22";
}

public enum ImageFit { Contain, Cover, Stretch }

/// <summary>Picture uploaded through the editor (file name inside the images folder).</summary>
public sealed class ImageElement : Element
{
    public string Src { get; set; } = "";
    public ImageFit Fit { get; set; } = ImageFit.Contain;
    public float Radius { get; set; }
}
