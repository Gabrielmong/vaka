namespace Vaka.Sensors;

/// <summary>
/// Sensors that don't come from LibreHardwareMonitor.
/// drive.{Letter}.{usedPct|freeGB|usedGB|totalGB} — sizes in GiB, like Explorer.
/// </summary>
public static class BuiltinSensors
{
    private const double GiB = 1024d * 1024 * 1024;

    public static Func<double?>? TryCreate(string key)
    {
        var parts = key.Split('.');
        if (parts.Length == 3 && parts[0] == "drive" && parts[1].Length == 1 && char.IsLetter(parts[1][0]))
        {
            var root = char.ToUpperInvariant(parts[1][0]) + @":\";
            return parts[2] switch
            {
                "usedPct" => () => Drive(root, (total, free) => (total - free) * 100.0 / total),
                "freeGB" => () => Drive(root, (_, free) => free / GiB),
                "usedGB" => () => Drive(root, (total, free) => (total - free) / GiB),
                "totalGB" => () => Drive(root, (total, _) => total / GiB),
                _ => null,
            };
        }
        return null;
    }

    /// <summary>Catalog entries for every ready fixed drive.</summary>
    public static IEnumerable<SensorInfo> CatalogEntries()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { yield break; }

        foreach (var d in drives)
        {
            bool ready;
            try { ready = d.IsReady && d.DriveType == DriveType.Fixed; } catch { ready = false; }
            if (!ready) continue;

            var letter = d.Name[0].ToString().ToUpperInvariant();
            string label;
            try { label = string.IsNullOrEmpty(d.VolumeLabel) ? $"Drive {letter}:" : $"Drive {letter}: ({d.VolumeLabel})"; }
            catch { label = $"Drive {letter}:"; }

            foreach (var (metric, name, unit) in new[]
                     {
                         ("usedPct", "Used space", "%"), ("usedGB", "Used", "GB"),
                         ("freeGB", "Free", "GB"), ("totalGB", "Total", "GB"),
                     })
            {
                var key = $"drive.{letter}.{metric}";
                yield return new SensorInfo(key, name, "Drive", unit, label, "Drive",
                    SensorCatalog.Clean(TryCreate(key)?.Invoke()));
            }
        }
    }

    private static double? Drive(string root, Func<double, double, double> calc)
    {
        try
        {
            var d = new DriveInfo(root);
            if (!d.IsReady || d.TotalSize <= 0) return null;
            return calc(d.TotalSize, d.TotalFreeSpace);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
