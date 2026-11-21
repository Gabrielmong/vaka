using System.Text.Json;
using Microsoft.Win32;

namespace SensorPanel;

public static class AppPaths
{
    /// <summary>%APPDATA%\SensorPanel, or SP_DATA_DIR when set (lets a test run use a scratch folder).</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SP_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SensorPanel");

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
