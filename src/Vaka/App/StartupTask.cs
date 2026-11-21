using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Vaka.App;

/// <summary>
/// Registers a Task Scheduler task that starts the panel at logon with highest privileges,
/// so it runs elevated without a UAC prompt.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "Vaka";

    /// <summary>The task's name when the app was called Sensor Panel; removed whenever Vaka registers its own.</summary>
    private const string LegacyTaskName = "SensorPanel";

    public static bool IsInstalled() => RunSchtasks($"/Query /TN \"{TaskName}\"", out _) == 0;

    /// <summary>True when the registered task is the installer's machine-wide one (left to the installer).</summary>
    public static bool IsAllUsersInstalled() =>
        RunSchtasks($"/Query /TN \"{TaskName}\" /XML", out var xml) == 0 && IsAllUsersXml(xml);

    /// <summary>Whether a task definition uses the Users group principal written for <c>--all-users</c>.</summary>
    public static bool IsAllUsersXml(string xml)
    {
        try
        {
            return System.Xml.Linq.XDocument.Parse(xml.Trim()).Descendants()
                .Any(e => e.Name.LocalName == "GroupId" && e.Value.Trim() == UsersGroupSid);
        }
        catch (System.Xml.XmlException) { return false; }
    }

    /// <summary>BUILTIN\Users: the all-users task starts for anyone who logs on (elevated for admins).</summary>
    private const string UsersGroupSid = "S-1-5-32-545";

    /// <param name="allUsers">
    /// Register for every user (used by the installer, which runs as SYSTEM) instead of the current account.
    /// </param>
    public static bool Install(string exePath, bool allUsers, out string output)
    {
        var xml = BuildXml(exePath, allUsers ? null : WindowsIdentity.GetCurrent().Name);
        var xmlPath = Path.Combine(Path.GetTempPath(), $"Vaka.task.{Guid.NewGuid():N}.xml");
        File.WriteAllText(xmlPath, xml, Encoding.Unicode);
        try
        {
            if (RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", out output) != 0) return false;
            RemoveLegacy();
            return true;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    /// <summary>Task definition; a null <paramref name="userId"/> means any user (Users group principal).</summary>
    public static string BuildXml(string exePath, string? userId)
    {
        var trigger = userId is null ? "" : $"<UserId>{Esc(userId)}</UserId>";
        var principal = userId is null
            ? $"<GroupId>{UsersGroupSid}</GroupId>"
            : $"<UserId>{Esc(userId)}</UserId><LogonType>InteractiveToken</LogonType>";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Vaka — hardware monitor display</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled>{trigger}<Delay>PT10S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">{principal}<RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowHardTerminate>true</AllowHardTerminate>
                <Priority>5</Priority>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{Esc(exePath)}</Command><WorkingDirectory>{Esc(Path.GetDirectoryName(exePath)!)}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
    }

    public static bool Uninstall(out string output)
    {
        RemoveLegacy();
        return RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out output) == 0;
    }

    /// <summary>An upgrade from Sensor Panel leaves its logon task (pointing at a removed exe) behind.</summary>
    private static void RemoveLegacy()
    {
        if (RunSchtasks($"/Query /TN \"{LegacyTaskName}\"", out _) == 0) RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F", out _);
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";

    private static int RunSchtasks(string args, out string output)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.ExitCode;
    }
}
