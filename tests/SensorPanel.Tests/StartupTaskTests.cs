using System.Xml.Linq;
using SensorPanel.App;
using Xunit;

namespace SensorPanel.Tests;

public class StartupTaskTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void AllUsersTaskUsesUsersGroup()
    {
        // The MSI registers the task as SYSTEM, so it must name the Users group, not the account running it.
        var doc = XDocument.Parse(StartupTask.BuildXml(@"C:\Program Files\SensorPanel\SensorPanel.exe", null));
        var principal = doc.Descendants(Ns + "Principal").Single();
        Assert.Equal("S-1-5-32-545", principal.Element(Ns + "GroupId")?.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")?.Value);
        Assert.Empty(doc.Descendants(Ns + "UserId"));
        Assert.Equal(@"C:\Program Files\SensorPanel\SensorPanel.exe", doc.Descendants(Ns + "Command").Single().Value);
    }

    [Fact]
    public void RecognisesInstallerTask()
    {
        // The tray toggle must not delete the installer's machine-wide task on one user's click.
        Assert.True(StartupTask.IsAllUsersXml(StartupTask.BuildXml(@"C:\x\SensorPanel.exe", null)));
        Assert.False(StartupTask.IsAllUsersXml(StartupTask.BuildXml(@"C:\x\SensorPanel.exe", @"DOMAIN\me")));
        Assert.False(StartupTask.IsAllUsersXml("not xml"));
    }

    [Fact]
    public void UserTaskKeepsUser()
    {
        var doc = XDocument.Parse(StartupTask.BuildXml(@"C:\x\SensorPanel.exe", @"DOMAIN\me"));
        Assert.Equal(@"DOMAIN\me", doc.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")?.Value);
        var principal = doc.Descendants(Ns + "Principal").Single();
        Assert.Equal(@"DOMAIN\me", principal.Element(Ns + "UserId")?.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")?.Value);
        Assert.Null(principal.Element(Ns + "GroupId"));
    }
}
