using Microsoft.Win32;
using GnomeWin.Input.GlobalHotkeys;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.VirtualDesktop;

public sealed class ShortcutBackend : IVirtualDesktopBackend
{
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private readonly PublicVirtualDesktopManager _public;

    public ShortcutBackend(PublicVirtualDesktopManager pub) => _public = pub;

    public string Name => "Documented shortcuts (limited)";
    public bool SupportsDirectSwitch => false;
    public bool SupportsCreate => true;
    public bool SupportsRemoveAny => false;
    public bool SupportsMoveWindow => false;
    public bool SupportsReorder => false;

    public IReadOnlyList<Guid> GetDesktopIds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key?.GetValue("VirtualDesktopIDs") is byte[] raw && raw.Length >= 16)
        {
            var list = new List<Guid>(raw.Length / 16);
            for (int i = 0; i + 16 <= raw.Length; i += 16) list.Add(new Guid(raw.AsSpan(i, 16)));
            return list;
        }
        Guid current = GetCurrentDesktopId();
        return current == Guid.Empty ? Array.Empty<Guid>() : new[] { current };
    }

    public Guid GetCurrentDesktopId()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
        {
            if (key?.GetValue("CurrentVirtualDesktop") is byte[] raw && raw.Length == 16) return new Guid(raw);
        }
        int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        using (var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{session}\VirtualDesktops"))
        {
            if (key?.GetValue("CurrentVirtualDesktop") is byte[] raw && raw.Length == 16) return new Guid(raw);
        }
        IntPtr fg = GetForegroundWindow();
        return fg != IntPtr.Zero ? _public.GetWindowDesktopId(fg) : Guid.Empty;
    }

    public bool Switch(Guid id)
    {
        var ids = GetDesktopIds();
        int from = ids.ToList().IndexOf(GetCurrentDesktopId());
        int to = ids.ToList().IndexOf(id);
        if (from < 0 || to < 0 || from == to) return from == to;
        int arrow = to > from ? VK_RIGHT : VK_LEFT;
        for (int i = 0; i < Math.Abs(to - from); i++)
            KeyboardHookService.SendChord(VK_CONTROL, VK_LWIN, arrow);
        return true;
    }

    public Guid? Create()
    {
        KeyboardHookService.SendChord(VK_CONTROL, VK_LWIN, VK_D);
        return null;
    }

    public bool Remove(Guid id, Guid fallback)
    {
        if (id != GetCurrentDesktopId()) return false;
        KeyboardHookService.SendChord(VK_CONTROL, VK_LWIN, VK_F4);
        return true;
    }

    public bool MoveWindow(IntPtr hwnd, Guid id) => false;
    public bool MoveDesktop(Guid id, int index) => false;
    public void Dispose() { }
}
