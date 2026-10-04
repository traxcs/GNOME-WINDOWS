using System.Runtime.InteropServices;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.VirtualDesktop;

[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
    [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
}

[ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
internal class CVirtualDesktopManager { }

public sealed class PublicVirtualDesktopManager
{
    private IVirtualDesktopManager? _mgr;

    public PublicVirtualDesktopManager()
    {
        Reset();
    }

    public bool IsAvailable => _mgr != null;

    public void Reset()
    {
        try
        {
            if (_mgr != null) Marshal.ReleaseComObject(_mgr);
            _mgr = (IVirtualDesktopManager)new CVirtualDesktopManager();
        }
        catch (Exception ex)
        {
            Log.Warn("IVirtualDesktopManager unavailable", ex);
            _mgr = null;
        }
    }

    public Guid GetWindowDesktopId(IntPtr hwnd)
    {
        if (_mgr == null) return Guid.Empty;
        try { return _mgr.GetWindowDesktopId(hwnd, out Guid id) == 0 ? id : Guid.Empty; }
        catch { return Guid.Empty; }
    }

    public bool? IsOnCurrentDesktop(IntPtr hwnd)
    {
        if (_mgr == null) return null;
        try { return _mgr.IsWindowOnCurrentVirtualDesktop(hwnd, out int on) == 0 ? on != 0 : null; }
        catch { return null; }
    }

    public bool MoveOwnWindow(IntPtr hwnd, Guid desktop)
    {
        if (_mgr == null) return false;
        try { return _mgr.MoveWindowToDesktop(hwnd, ref desktop) == 0; }
        catch { return false; }
    }
}
