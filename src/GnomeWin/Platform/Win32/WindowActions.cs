using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Win32;

public static class WindowActions
{
    public static bool Activate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        if (SetForegroundWindow(hwnd)) return true;

        var input = new[] { new INPUT { type = INPUT_MOUSE } };
        SendInput(1, input, INPUT.Size);
        if (SetForegroundWindow(hwnd)) return true;

        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint myThread = GetCurrentThreadId();
        if (fgThread != 0 && fgThread != myThread)
        {
            AttachThreadInput(myThread, fgThread, true);
            try
            {
                BringWindowToTop(hwnd);
                bool ok = SetForegroundWindow(hwnd);
                if (!ok) Log.Debug($"SetForegroundWindow refused for 0x{hwnd.ToInt64():X}");
                return ok;
            }
            finally { AttachThreadInput(myThread, fgThread, false); }
        }
        return false;
    }

    public static void Minimize(IntPtr hwnd) => ShowWindowAsync(hwnd, SW_MINIMIZE);

    public static void Restore(IntPtr hwnd) => ShowWindowAsync(hwnd, SW_RESTORE);

    public static void ToggleMaximize(IntPtr hwnd) =>
        ShowWindowAsync(hwnd, IsZoomed(hwnd) ? SW_RESTORE : SW_MAXIMIZE);

    public static void Close(IntPtr hwnd) => PostMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);

    public static void MoveToMonitor(IntPtr hwnd, RECT targetWork)
    {
        bool wasMax = IsZoomed(hwnd);
        if (wasMax) ShowWindow(hwnd, SW_RESTORE);
        GetWindowRect(hwnd, out RECT r);
        int w = Math.Min(r.Width, targetWork.Width), h = Math.Min(r.Height, targetWork.Height);
        int x = targetWork.Left + (targetWork.Width - w) / 2, y = targetWork.Top + (targetWork.Height - h) / 2;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
        if (wasMax) ShowWindow(hwnd, SW_MAXIMIZE);
    }
}
