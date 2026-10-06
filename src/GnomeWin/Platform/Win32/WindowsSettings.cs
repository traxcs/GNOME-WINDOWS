using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.Win32;

public static class WindowsSettings
{
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02, PERSIST = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
    private const uint SPI_GETKEYBOARDSPEED = 0x000A, SPI_SETKEYBOARDSPEED = 0x000B;
    private const uint SPI_GETKEYBOARDDELAY = 0x0016, SPI_SETKEYBOARDDELAY = 0x0017;
    private const uint SPI_SETDOUBLECLICKTIME = 0x0020, SPI_SETMOUSEBUTTONSWAP = 0x0021;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068, SPI_SETWHEELSCROLLLINES = 0x0069;
    private const uint SPI_GETMOUSESPEED = 0x0070, SPI_SETMOUSESPEED = 0x0071;
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const int SM_SWAPBUTTON = 23;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiGetInt(uint action, uint param, out int value, uint ini);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SpiSet(uint action, uint param, IntPtr value, uint ini);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    public static bool PrimaryButtonIsRight
    {
        get => GetSystemMetrics(SM_SWAPBUTTON) != 0;
        set => SpiSet(SPI_SETMOUSEBUTTONSWAP, value ? 1u : 0u, IntPtr.Zero, PERSIST);
    }

    public static int MouseSpeed
    {
        get => SpiGetInt(SPI_GETMOUSESPEED, 0, out int v, 0) ? v : 10;
        set => SpiSet(SPI_SETMOUSESPEED, 0, new IntPtr(Math.Clamp(value, 1, 20)), PERSIST);
    }

    public static int DoubleClickTime
    {
        get => (int)GetDoubleClickTime();
        set => SpiSet(SPI_SETDOUBLECLICKTIME, (uint)Math.Clamp(value, 200, 900), IntPtr.Zero, PERSIST);
    }

    public static int WheelScrollLines
    {
        get => SpiGetInt(SPI_GETWHEELSCROLLLINES, 0, out int v, 0) ? v : 3;
        set => SpiSet(SPI_SETWHEELSCROLLLINES, (uint)Math.Clamp(value, 1, 20), IntPtr.Zero, PERSIST);
    }

    public static int KeyboardDelay
    {
        get => SpiGetInt(SPI_GETKEYBOARDDELAY, 0, out int v, 0) ? v : 1;
        set => SpiSet(SPI_SETKEYBOARDDELAY, (uint)Math.Clamp(value, 0, 3), IntPtr.Zero, PERSIST);
    }

    public static int KeyboardSpeed
    {
        get => SpiGetInt(SPI_GETKEYBOARDSPEED, 0, out int v, 0) ? v : 31;
        set => SpiSet(SPI_SETKEYBOARDSPEED, (uint)Math.Clamp(value, 0, 31), IntPtr.Zero, PERSIST);
    }

    public static bool ClientAreaAnimations
    {
        get => !SpiGetInt(SPI_GETCLIENTAREAANIMATION, 0, out int v, 0) || v != 0;
        set => SpiSet(SPI_SETCLIENTAREAANIMATION, 0, new IntPtr(value ? 1 : 0), PERSIST);
    }

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool WindowsDarkMode
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        set => UI.Themes.ThemeManager.SetWindowsDarkMode(value);
    }

    public static bool TransparencyEffects
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            key.SetValue("EnableTransparency", value ? 1 : 0, RegistryValueKind.DWord);
            NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", NativeMethods.SMTO_ABORTIFHUNG, 200, out _);
        }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint accessFlags, uint index, IntPtr buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static readonly Guid SchemePowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid SchemeBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid SchemePerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    public static Guid ActivePowerScheme
    {
        get
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr p) != 0 || p == IntPtr.Zero) return SchemeBalanced;
            try { return Marshal.PtrToStructure<Guid>(p); }
            finally { LocalFree(p); }
        }
    }

    public static IReadOnlyList<Guid> AvailablePowerSchemes()
    {
        var list = new List<Guid>();
        const uint ACCESS_SCHEME = 16;
        for (uint i = 0; ; i++)
        {
            uint size = 16;
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buf, ref size) != 0) break;
                list.Add(Marshal.PtrToStructure<Guid>(buf));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return list;
    }

    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);

    public static string PowerSchemeName(Guid scheme, bool french)
    {
        if (scheme == SchemePowerSaver) return french ? "Économie d'énergie" : "Power Saver";
        if (scheme == SchemeBalanced) return french ? "Équilibré" : "Balanced";
        if (scheme == SchemePerformance) return french ? "Performance" : "Performance";
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0) return scheme.ToString();
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0
                ? Marshal.PtrToStringUni(buf) ?? scheme.ToString()
                : scheme.ToString();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static bool NightLightActive
    {
        get
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate");
                return k?.GetValue("Data") is byte[] d && d.Length > 18 && d[18] == 0x15;
            }
            catch { return false; }
        }
    }

    public static bool SetPowerScheme(Guid scheme)
    {
        uint rc = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (rc != 0) Log.Warn($"PowerSetActiveScheme failed: {rc}");
        return rc == 0;
    }

    public static int ScreenBlankMinutes
    {
        get => ReadIndex(SubVideo, VideoIdle) / 60;
        set => WriteIndex(SubVideo, VideoIdle, value * 60);
    }

    public static int SuspendMinutes
    {
        get => ReadIndex(SubSleep, StandbyIdle) / 60;
        set => WriteIndex(SubSleep, StandbyIdle, value * 60);
    }

    private static int ReadIndex(Guid sub, Guid setting)
    {
        Guid scheme = ActivePowerScheme;
        return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint v) == 0 ? (int)v : 0;
    }

    private static void WriteIndex(Guid sub, Guid setting, int seconds)
    {
        Guid scheme = ActivePowerScheme;
        PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)Math.Max(0, seconds));
        PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)Math.Max(0, seconds));
        PowerSetActiveScheme(IntPtr.Zero, ref scheme);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    public static string OsName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string product = key?.GetValue("ProductName") as string ?? "Windows";
        string display = key?.GetValue("DisplayVersion") as string ?? string.Empty;
        if (Environment.OSVersion.Version.Build >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return $"{product} {display} (build {Environment.OSVersion.Version.Build})".Trim();
    }

    public static string Processor()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string name = (key?.GetValue("ProcessorNameString") as string ?? "?").Trim();
        return $"{name} × {Environment.ProcessorCount}";
    }

    public static string Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? $"{Math.Round(m.ullTotalPhys / 1073741824.0, 1)} Gio" : "?";
    }

    public static string DiskCapacity()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return $"{Math.Round(d.TotalSize / 1e9)} Go ({Math.Round(d.AvailableFreeSpace / 1e9)} Go libres)";
        }
        catch { return "?"; }
    }

    public static string TimeZone() => TimeZoneInfo.Local.DisplayName;

    public static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log.Warn($"Cannot open {uri}", ex); }
    }
}
