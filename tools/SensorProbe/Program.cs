using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;

// Lists every sensor LibreHardwareMonitor can see on this machine.
// Run elevated: motherboard fans/voltages and CPU temps need admin.

var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
var outPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "sensors.txt");

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsStorageEnabled = true,
    IsNetworkEnabled = true,
};

var sb = new StringBuilder();
sb.AppendLine($"Admin: {isAdmin}   Time: {DateTime.Now}");
sb.AppendLine();

try
{
    computer.Open();
    // Some sensors (e.g. load) only report after a second sample.
    UpdateAll(computer);
    Thread.Sleep(1000);
    UpdateAll(computer);

    foreach (var hw in computer.Hardware)
        Dump(hw, 0);
}
catch (Exception ex)
{
    sb.AppendLine($"ERROR: {ex}");
}
finally
{
    computer.Close();
}

File.WriteAllText(outPath, sb.ToString());
Console.WriteLine(sb.ToString());
Console.WriteLine($"Written to {outPath}");

void UpdateAll(Computer c)
{
    foreach (var hw in c.Hardware)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware) sub.Update();
    }
}

void Dump(IHardware hw, int depth)
{
    var pad = new string(' ', depth * 2);
    sb.AppendLine($"{pad}[{hw.HardwareType}] {hw.Name}   ({hw.Identifier})");
    foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
    {
        var val = s.Value.HasValue ? s.Value.Value.ToString("0.##") : "n/a";
        sb.AppendLine($"{pad}  {s.SensorType,-12} {s.Name,-32} {val,10}   {s.Identifier}");
    }
    foreach (var sub in hw.SubHardware)
        Dump(sub, depth + 1);
    sb.AppendLine();
}
