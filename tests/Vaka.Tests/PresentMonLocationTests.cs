using Vaka.Sensors;
using Xunit;

namespace Vaka.Tests;

/// <summary>
/// Vaka runs as administrator, so the presentmon.exe it starts must sit where only administrators can write:
/// its own folder under Program Files, or a path an administrator set in HKLM. Never PATH or %LOCALAPPDATA%.
/// </summary>
public sealed class PresentMonLocationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vaka-pm-" + Guid.NewGuid().ToString("N"));
    private string Trusted => Path.Combine(_dir, "Program Files");
    private string InstallDir => Path.Combine(Trusted, "Vaka");
    private string BuildDir => Path.Combine(_dir, "repo", "bin");

    public PresentMonLocationTests()
    {
        foreach (var d in new[] { InstallDir, BuildDir }) Directory.CreateDirectory(d);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    private static string Touch(string path) { File.WriteAllText(path, ""); return path; }

    [Fact]
    public void UsesTheCopyInAProtectedInstallFolder()
    {
        var exe = Touch(Path.Combine(InstallDir, "presentmon.exe"));
        Assert.Equal(exe, FpsMonitor.FindPresentMon(InstallDir, [Trusted], null));
    }

    [Fact]
    public void IgnoresACopyNextToAnUnprotectedBuild()
    {
        Touch(Path.Combine(BuildDir, "presentmon.exe"));
        Assert.Null(FpsMonitor.FindPresentMon(BuildDir, [Trusted], null));
    }

    [Fact]
    public void AFolderThatOnlyStartsLikeTheTrustedOneIsNotTrusted()
    {
        var lookalike = Path.Combine(_dir, "Program Files Evil");
        Directory.CreateDirectory(lookalike);
        Touch(Path.Combine(lookalike, "presentmon.exe"));
        Assert.Null(FpsMonitor.FindPresentMon(lookalike, [Trusted], null));
    }

    [Fact]
    public void IgnoresOtherPresentMonNamesInTheInstallFolder()
    {
        Touch(Path.Combine(InstallDir, "presentmon-planted.exe"));
        Assert.Null(FpsMonitor.FindPresentMon(InstallDir, [Trusted], null));
    }

    [Fact]
    public void AnAdminSetPathWins()
    {
        Touch(Path.Combine(InstallDir, "presentmon.exe"));
        var chosen = Touch(Path.Combine(BuildDir, "pm.exe"));
        Assert.Equal(chosen, FpsMonitor.FindPresentMon(InstallDir, [Trusted], chosen));
    }

    [Fact]
    public void AMissingAdminSetPathFindsNothing()
    {
        Touch(Path.Combine(InstallDir, "presentmon.exe"));
        Assert.Null(FpsMonitor.FindPresentMon(InstallDir, [Trusted], Path.Combine(BuildDir, "gone.exe")));
    }

    [Fact]
    public void NeverSearchesPath()
    {
        Touch(Path.Combine(BuildDir, "presentmon.exe"));
        var old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", BuildDir + ";" + old);
            Assert.Null(FpsMonitor.FindPresentMon(BuildDir, [Trusted], null));
        }
        finally { Environment.SetEnvironmentVariable("PATH", old); }
    }
}
