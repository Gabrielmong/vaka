namespace Vaka.Editor;

/// <summary>
/// Throttles preview graph history per key (one sample per key every 900 ms), so many previews rendered
/// back to back (the template gallery) each get samples instead of only the first one.
/// </summary>
internal sealed class PreviewRecorder
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(900);
    private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);

    public List<string> Due(IEnumerable<string> keys, DateTime now)
    {
        var due = new List<string>();
        foreach (var k in keys)
        {
            if (_last.TryGetValue(k, out var t) && now - t < Every) continue;
            _last[k] = now;
            due.Add(k);
        }
        return due;
    }
}
