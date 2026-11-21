namespace SensorPanel.Sensors;

public readonly record struct HistorySample(DateTime Time, double? Value);

/// <summary>Source of recent values for graph widgets.</summary>
public interface IHistorySource
{
    /// <summary>Appends samples of <paramref name="key"/> newer than <paramref name="since"/>, oldest first.</summary>
    void Read(string key, DateTime since, List<HistorySample> into);
}

/// <summary>
/// Rolling per-key history for the sensors graphs use. Written by the poll thread, read by
/// the UI and editor threads — all access goes through one lock (a few hundred samples).
/// </summary>
public sealed class SensorHistory : IHistorySource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<HistorySample>> _series = new(StringComparer.Ordinal);

    /// <summary>Samples older than this are dropped.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Records one sample per key and forgets keys that are no longer listed.</summary>
    public void Record(DateTime now, IReadOnlyCollection<string> keys, Func<string, double?> value)
    {
        var cutoff = now - Retention;
        lock (_gate)
        {
            if (_series.Count > 0)
            {
                foreach (var stale in _series.Keys.Where(k => !keys.Contains(k)).ToList())
                    _series.Remove(stale);
            }
            foreach (var key in keys)
            {
                if (!_series.TryGetValue(key, out var q)) _series[key] = q = new Queue<HistorySample>();
                var v = value(key);
                q.Enqueue(new HistorySample(now, v is { } d && double.IsFinite(d) ? d : null));
                while (q.Count > 0 && q.Peek().Time < cutoff) q.Dequeue();
            }
        }
    }

    public bool Has(string key)
    {
        lock (_gate) return _series.TryGetValue(key, out var q) && q.Count > 0;
    }

    public void Read(string key, DateTime since, List<HistorySample> into)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(key, out var q)) return;
            foreach (var s in q)
                if (s.Time >= since) into.Add(s);
        }
    }
}
