using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace SensorPanel.App;

/// <summary>
/// Hardware initialisation allocates a lot of short-lived data (WMI, device enumeration).
/// After it finishes, compact the heap and hand unused pages back to Windows.
/// </summary>
internal static class MemoryTrim
{
    public static void AfterStartup()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { /* best effort */ }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}
