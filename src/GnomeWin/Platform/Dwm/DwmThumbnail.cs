using GnomeWin.Platform.Win32;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Dwm;

public sealed class DwmThumbnail : IDisposable
{
    private IntPtr _thumb;
    private RECT _lastDest;
    private byte _lastOpacity = 255;
    private bool _lastVisible;
    private bool _initialized;

    public IntPtr Source { get; }
    public IntPtr Destination { get; }
    public bool IsValid => _thumb != IntPtr.Zero;

    private DwmThumbnail(IntPtr thumb, IntPtr dest, IntPtr src)
    {
        _thumb = thumb;
        Destination = dest;
        Source = src;
    }

    public static DwmThumbnail? Register(IntPtr destination, IntPtr source)
    {
        if (destination == IntPtr.Zero || source == IntPtr.Zero || !IsWindow(source)) return null;
        return DwmRegisterThumbnail(destination, source, out IntPtr thumb) == 0 && thumb != IntPtr.Zero
            ? new DwmThumbnail(thumb, destination, source)
            : null;
    }

    public SIZE SourceSize
    {
        get
        {
            if (_thumb == IntPtr.Zero) return default;
            return DwmQueryThumbnailSourceSize(_thumb, out SIZE s) == 0 ? s : default;
        }
    }

    public static RECT? VisibleSourceRect(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) return null;
        GetWindowRect(hwnd, out RECT win);
        RECT frame = GetFrameBounds(hwnd);
        if (frame.IsEmpty || win.IsEmpty) return null;
        var r = new RECT(frame.Left - win.Left, frame.Top - win.Top, frame.Right - win.Left, frame.Bottom - win.Top);
        return r.IsEmpty ? null : r;
    }

    public bool Update(RECT destination, byte opacity = 255, bool visible = true, RECT? source = null)
    {
        if (_thumb == IntPtr.Zero) return false;
        if (_initialized && destination == _lastDest && opacity == _lastOpacity && visible == _lastVisible && source == null)
            return true;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = destination,
            opacity = opacity,
            fVisible = visible ? 1 : 0,
            fSourceClientAreaOnly = 0,
        };
        if (source is RECT src)
        {
            props.dwFlags |= DWM_TNP_RECTSOURCE;
            props.rcSource = src;
        }
        int hr = DwmUpdateThumbnailProperties(_thumb, ref props);
        _initialized = true;
        _lastDest = destination;
        _lastOpacity = opacity;
        _lastVisible = visible;
        return hr == 0;
    }

    public void Hide() => Update(_lastDest, _lastOpacity, visible: false);

    public void Dispose()
    {
        if (_thumb != IntPtr.Zero)
        {
            DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }
    }
}
