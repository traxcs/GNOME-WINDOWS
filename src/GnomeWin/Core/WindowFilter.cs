using GnomeWin.Platform.Win32;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Core;

public static class WindowFilter
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "Shell_InputSwitchTopLevelWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "ThumbnailDeviceHelperWnd", "EdgeUiInputTopWndClass", "EdgeUiInputWndClass", "ApplicationManager_ImmersiveShellWindow",
        "Internet Explorer_Hidden", "tooltips_class32", "#32768", "SysShadow",
    };

    public static bool IsShellOrTrayClass(string cls) =>
        cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW";

    public static bool IsAppWindow(IntPtr hwnd, string className, Func<IntPtr, Guid> desktopIdResolver)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (ExcludedClasses.Contains(className)) return false;

        long style = GetStyle(hwnd);
        long ex = GetExStyle(hwnd);
        if ((style & WS_CHILD) != 0) return false;
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if ((ex & WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        if ((ex & WS_EX_NOACTIVATE) != 0 && !appWindow) return false;

        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        if (owner != IntPtr.Zero && !appWindow && IsWindowVisible(owner)) return false;

        int cloaked = GetCloakedState(hwnd);
        if (cloaked != 0)
        {
            if (cloaked != DWM_CLOAKED_SHELL) return false;
            if (desktopIdResolver(hwnd) == Guid.Empty) return false;
        }

        string title = GetWindowTitle(hwnd);
        if (title.Length == 0 && !appWindow && (style & WS_CAPTION) != WS_CAPTION) return false;

        if (!IsIconic(hwnd))
        {
            GetWindowRect(hwnd, out RECT r);
            if (r.Width <= 1 || r.Height <= 1) return false;
        }
        return true;
    }
}
