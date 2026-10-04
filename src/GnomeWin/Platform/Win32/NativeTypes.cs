using System.Runtime.InteropServices;

namespace GnomeWin.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
public struct RECT : IEquatable<RECT>
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    public readonly bool Intersects(RECT o) =>
        Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom;

    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public readonly bool Covers(RECT o) =>
        Left <= o.Left && Top <= o.Top && Right >= o.Right && Bottom >= o.Bottom;

    public readonly bool Equals(RECT o) =>
        Left == o.Left && Top == o.Top && Right == o.Right && Bottom == o.Bottom;

    public override readonly bool Equals(object? obj) => obj is RECT r && Equals(r);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public static bool operator ==(RECT a, RECT b) => a.Equals(b);
    public static bool operator !=(RECT a, RECT b) => !a.Equals(b);
    public override readonly string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X, Y;
    public POINT(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int cx, cy;
    public SIZE(int x, int y) { cx = x; cy = y; }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk, wScan;
    public uint dwFlags, time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL, wParamH;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion U;
    public static int Size => Marshal.SizeOf<INPUT>();
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct COPYDATASTRUCT
{
    public IntPtr dwData;
    public int cbData;
    public IntPtr lpData;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    public int fVisible;
    public int fSourceClientAreaOnly;
}

[StructLayout(LayoutKind.Sequential)]
public struct MARGINS
{
    public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
    public PROPERTYKEY(Guid f, uint p) { fmtid = f; pid = p; }
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPVARIANT
{
    public ushort vt;
    public ushort r1, r2, r3;
    public IntPtr p;
    public IntPtr p2;

    public readonly string? GetString()
    {
        if (vt == 31 || vt == 8) return Marshal.PtrToStringUni(p);
        return null;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct BITMAP
{
    public int bmType, bmWidth, bmHeight, bmWidthBytes;
    public ushort bmPlanes, bmBitsPixel;
    public IntPtr bmBits;
}

[StructLayout(LayoutKind.Sequential)]
public struct POWERBROADCAST_SETTING
{
    public Guid PowerSetting;
    public uint DataLength;
    public byte Data;
}
