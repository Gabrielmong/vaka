using Xunit;

namespace Vaka.Tests;

/// <summary>The app was called Sensor Panel; its data folder (%APPDATA%\SensorPanel) is copied to the new one once.</summary>
public sealed class DataMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vaka-migrate-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_dir, "SensorPanel");
    private string Current => Path.Combine(_dir, "Vaka");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    private void SeedLegacy()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "layouts", "backups"));
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "{\"layout\":\"mine.json\"}");
        File.WriteAllText(Path.Combine(Legacy, "layouts", "mine.json"), "{}");
        File.WriteAllText(Path.Combine(Legacy, "layouts", "backups", "mine.1.json"), "{}");
    }

    [Fact]
    public void CopiesEverythingAndKeepsTheOldFolder()
    {
        SeedLegacy();
        Assert.True(AppPaths.MigrateLegacyData(Legacy, Current));
        Assert.Equal("{\"layout\":\"mine.json\"}", File.ReadAllText(Path.Combine(Current, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Current, "layouts", "mine.json")));
        Assert.True(File.Exists(Path.Combine(Current, "layouts", "backups", "mine.1.json")));
        Assert.True(File.Exists(Path.Combine(Legacy, "layouts", "mine.json")));   // never touched
    }

    [Fact]
    public void DoesNothingWhenTheNewFolderExists()
    {
        SeedLegacy();
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "settings.json"), "{}");
        Assert.False(AppPaths.MigrateLegacyData(Legacy, Current));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Current, "settings.json")));
        Assert.False(File.Exists(Path.Combine(Current, "layouts", "mine.json")));
    }

    [Fact]
    public void DoesNothingWithoutALegacyFolder()
    {
        Assert.False(AppPaths.MigrateLegacyData(Legacy, Current));
        Assert.False(Directory.Exists(Current));
    }

    [Fact]
    public void AnInterruptedCopyIsRetried()
    {
        SeedLegacy();
        Directory.CreateDirectory(Current + ".migrating");   // left behind by a crash mid-copy
        File.WriteAllText(Path.Combine(Current + ".migrating", "partial.tmp"), "x");
        Assert.True(AppPaths.MigrateLegacyData(Legacy, Current));
        Assert.True(File.Exists(Path.Combine(Current, "layouts", "mine.json")));
        Assert.False(File.Exists(Path.Combine(Current, "partial.tmp")));
        Assert.False(Directory.Exists(Current + ".migrating"));
    }
}
