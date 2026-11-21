using System.Text.Json;
using Microsoft.Win32;

namespace Vaka;

public static class AppPaths
{
    /// <summary>
    /// %APPDATA%\Vaka, or VAKA_DATA_DIR when set (lets a test run use a scratch folder). The first time, data from
    /// the app's old name (%APPDATA%\SensorPanel) is copied in.
    /// </summary>
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        if (Environment.GetEnvironmentVariable("VAKA_DATA_DIR") is { Length: > 0 } dir) return dir;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "Vaka");
        try { MigrateLegacyData(Path.Combine(appData, "SensorPanel"), root); }
        catch { /* start with defaults rather than not at all; the old folder is untouched */ }
        return root;
    }

    /// <summary>
    /// Copies <paramref name="legacy"/> (layouts, images, settings, backups) to <paramref name="current"/> when only
    /// the legacy folder exists. Copies into a side folder and renames it into place, so an interrupted copy is
    /// redone next time instead of leaving a half-filled data folder. Never modifies the legacy folder.
    /// </summary>
    public static bool MigrateLegacyData(string legacy, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return false;
        var staging = current + ".migrating";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        foreach (var src in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(staging, Path.GetRelativePath(legacy, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest);
        }
        Directory.CreateDirectory(staging); // an empty legacy folder still migrates (to an empty one)
        Directory.Move(staging, current);
        return true;
    }

    public static string Layouts => Path.Combine(Root, "layouts");
    public static string Images => Path.Combine(Root, "images");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "panel.log");
}

public sealed class AppSettings
{
    /// <summary>Monitor device interface path of the panel display.</summary>
    public string? DisplayId { get; set; }
    public int DisplayWidth { get; set; }
    public int DisplayHeight { get; set; }
    public string Layout { get; set; } = "default.json";
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Layouts shown in rotation; when empty only <see cref="Layout"/> is shown.</summary>
    public List<string>? Pages { get; set; }
    public int PageSeconds { get; set; } = 15;

    public IReadOnlyList<string> EffectivePages => Pages is { Count: > 0 } p ? p : [Layout];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new();
        }
        catch (Exception ex) { Log.Write($"Settings unreadable, using defaults: {ex.Message}"); }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        AtomicFile.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json));
    }
}

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}

public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1_000_000;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(AppPaths.LogFile, AppPaths.LogFile + ".old", overwrite: true);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the panel */ }
    }
}

public static class Environment2
{
    public static bool IsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static bool IsPawnIoInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }
}
