using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Vaka.Rendering;

/// <summary>
/// Text template with sensor placeholders: "{cpu.temp:0}°C", "{time:HH:mm}", "{date:MM/dd/yy}".
/// Format after ':' is a .NET format string. "{{" and "}}" are literal braces.
/// Missing values render as "--".
/// </summary>
public sealed class Template
{
    public const string Missing = "--";
    private static readonly ConcurrentDictionary<string, Template> Cache = new();

    private readonly List<Part> _parts;
    public IReadOnlyList<string> Keys { get; }

    private readonly record struct Part(string? Literal, string? Key, string? Format);

    private Template(List<Part> parts)
    {
        _parts = parts;
        Keys = parts.Where(p => p.Key is not null && !IsClockKey(p.Key))
                    .Select(p => p.Key!)
                    .Distinct()
                    .ToList();
    }

    public static Template Parse(string text) => Cache.GetOrAdd(text ?? "", Build);

    private static bool IsClockKey(string key) => key is "time" or "date";

    private static Template Build(string text)
    {
        var parts = new List<Part>();
        var literal = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { literal.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { literal.Append('}'); i++; continue; }
            if (c == '{')
            {
                int end = text.IndexOf('}', i + 1);
                if (end < 0) { literal.Append(text, i, text.Length - i); break; }
                if (literal.Length > 0) { parts.Add(new Part(literal.ToString(), null, null)); literal.Clear(); }
                var token = text.Substring(i + 1, end - i - 1);
                int colon = token.IndexOf(':');
                var key = (colon < 0 ? token : token[..colon]).Trim();
                var format = colon < 0 ? null : token[(colon + 1)..];
                parts.Add(new Part(null, key, string.IsNullOrEmpty(format) ? null : format));
                i = end;
                continue;
            }
            literal.Append(c);
        }
        if (literal.Length > 0) parts.Add(new Part(literal.ToString(), null, null));
        return new Template(parts);
    }

    public string Format(Func<string, double?> values, DateTime now, IFormatProvider culture)
    {
        if (_parts.Count == 1 && _parts[0].Literal is { } only) return only;
        var sb = new StringBuilder();
        foreach (var p in _parts)
        {
            if (p.Literal is not null) { sb.Append(p.Literal); continue; }
            switch (p.Key)
            {
                case "time": sb.Append(now.ToString(p.Format ?? "HH:mm", culture)); break;
                case "date": sb.Append(now.ToString(p.Format ?? "d", culture)); break;
                default:
                    var v = values(p.Key!);
                    sb.Append(v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)
                        ? Missing
                        : v.Value.ToString(p.Format ?? "0.##", culture));
                    break;
            }
        }
        return sb.ToString();
    }

    public string Format(Func<string, double?> values, DateTime now) =>
        Format(values, now, CultureInfo.CurrentCulture);
}
