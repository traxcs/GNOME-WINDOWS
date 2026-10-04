using System.Runtime;
using System.Runtime.InteropServices;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.Win32;

public static class MemoryTrimmer
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    public static void Trim(string reason)
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            Log.Debug($"Memory trimmed ({reason}), managed heap {GC.GetTotalMemory(false) / 1048576} MB.");
        }
        catch (Exception ex)
        {
            Log.Debug("Memory trim failed: " + ex.Message);
        }
    }
}
