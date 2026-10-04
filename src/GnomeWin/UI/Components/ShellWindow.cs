using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GnomeWin.Platform.Win32;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.UI.Components;

public class ShellWindow : Window
{
    private RECT? _desiredRect;
    private POINT? _desiredPos;
    private readonly bool _noActivate;

    public ShellWindow(bool noActivate, bool transparent)
    {
        _noActivate = noActivate;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = !noActivate;
        Topmost = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        if (transparent)
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
        }
        SetResourceReference(FontFamilyProperty, "Font.Ui");
        SetResourceReference(ForegroundProperty, "Brush.Fg");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    public IntPtr Handle { get; private set; }

    protected bool PreferSoftwareRendering { get; set; } = true;

    public static void UseSoftwareRendering(Window w)
    {
        var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
        if (src?.CompositionTarget != null) src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
    }

    public double DpiScale => Handle == IntPtr.Zero ? 1.0 : GetDpiForWindow(Handle) / 96.0;

    public IntPtr EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        long ex = GetExStyle(Handle);
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        if (_noActivate) ex |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex));
        if (PreferSoftwareRendering) UseSoftwareRendering(this);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        ApplyPlacement();
    }

    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE && _noActivate)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (msg == WM_DPICHANGED)
        {
            Dispatcher.BeginInvoke(ApplyPlacement, System.Windows.Threading.DispatcherPriority.Render);
        }
        return IntPtr.Zero;
    }

    public void PlacePhysical(RECT r)
    {
        _desiredRect = r;
        _desiredPos = null;
        ApplyPlacement();
    }

    public void MovePhysical(int x, int y)
    {
        _desiredPos = new POINT(x, y);
        _desiredRect = null;
        ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        if (Handle == IntPtr.Zero) return;
        IntPtr after = Topmost ? HWND_TOPMOST : HWND_TOP;
        if (_desiredRect is RECT r)
            SetWindowPos(Handle, after, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        else if (_desiredPos is POINT p)
            SetWindowPos(Handle, after, p.X, p.Y, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOOWNERZORDER);
    }

    public void BringToTopmost()
    {
        if (Handle != IntPtr.Zero)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void SetCornerPreference(int pref)
    {
        if (Handle == IntPtr.Zero) EnsureHandle();
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    public (int W, int H) PhysicalSize(Size dip) => ((int)Math.Ceiling(dip.Width * DpiScale), (int)Math.Ceiling(dip.Height * DpiScale));

    public void ForceActivate()
    {
        if (Handle == IntPtr.Zero) return;
        WindowActions.Activate(Handle);
        Activate();
    }
}
