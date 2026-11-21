using System.Runtime.InteropServices;

namespace Vaka.Display;

public static class MonitorEnumerator
{
    public static List<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        foreach (var screen in Screen.AllScreens)
        {
            string id = screen.DeviceName, description = "Display";
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            // Index 0 = first monitor attached to this adapter output; flag returns the device interface path.
            if (EnumDisplayDevices(screen.DeviceName, 0, ref dd, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                if (!string.IsNullOrEmpty(dd.DeviceID)) id = dd.DeviceID;
                if (!string.IsNullOrEmpty(dd.DeviceString)) description = dd.DeviceString;
            }
            result.Add(new MonitorInfo(screen.DeviceName, id, description, screen.Bounds, screen.Primary));
        }
        return result;
    }

    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
}
