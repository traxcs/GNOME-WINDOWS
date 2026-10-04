using System.Runtime.InteropServices;
using System.Windows.Interop;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Win32;

public sealed class AppBar : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly int _callbackMsg;
    private bool _registered;
    private uint _edge = ABE_TOP;
    private RECT _monitor;
    private int _thickness;

    public event Action<RECT>? PositionAssigned;
    public event Action<bool>? FullscreenAppChanged;

    public AppBar(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)!;
        _callbackMsg = RegisterWindowMessage("GnomeWin.AppBarMessage");
        _source.AddHook(WndProc);
    }

    public void Register(RECT monitor, int thicknessPx, uint edge = ABE_TOP)
    {
        _monitor = monitor;
        _thickness = thicknessPx;
        _edge = edge;
        if (!_registered)
        {
            var abd = New();
            abd.uCallbackMessage = (uint)_callbackMsg;
            _registered = SHAppBarMessage(ABM_NEW, ref abd) != UIntPtr.Zero;
            if (!_registered) Log.Warn("ABM_NEW failed");
        }
        UpdatePosition();
    }

    public void UpdatePosition()
    {
        if (!_registered) return;
        var abd = New();
        abd.uEdge = _edge;
        abd.rc = _monitor;
        switch (_edge)
        {
            case ABE_TOP: abd.rc.Bottom = abd.rc.Top + _thickness; break;
            case ABE_BOTTOM: abd.rc.Top = abd.rc.Bottom - _thickness; break;
            case ABE_LEFT: abd.rc.Right = abd.rc.Left + _thickness; break;
            case ABE_RIGHT: abd.rc.Left = abd.rc.Right - _thickness; break;
        }
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        switch (_edge)
        {
            case ABE_TOP: abd.rc.Bottom = abd.rc.Top + _thickness; break;
            case ABE_BOTTOM: abd.rc.Top = abd.rc.Bottom - _thickness; break;
            case ABE_LEFT: abd.rc.Right = abd.rc.Left + _thickness; break;
            case ABE_RIGHT: abd.rc.Left = abd.rc.Right - _thickness; break;
        }
        SHAppBarMessage(ABM_SETPOS, ref abd);
        PositionAssigned?.Invoke(abd.rc);
    }

    public void Unregister()
    {
        if (!_registered) return;
        var abd = New();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    private APPBARDATA New() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMsg)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    UpdatePosition();
                    break;
                case ABN_FULLSCREENAPP:
                    FullscreenAppChanged?.Invoke(lParam != IntPtr.Zero);
                    break;
            }
            handled = true;
        }
        else if (_registered && msg == WM_ACTIVATE)
        {
            var abd = New();
            SHAppBarMessage(ABM_ACTIVATE, ref abd);
        }
        else if (_registered && msg == WM_WINDOWPOSCHANGED)
        {
            var abd = New();
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
