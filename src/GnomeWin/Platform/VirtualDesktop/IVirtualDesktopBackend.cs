namespace GnomeWin.Platform.VirtualDesktop;

public interface IVirtualDesktopBackend : IDisposable
{
    string Name { get; }
    bool SupportsDirectSwitch { get; }
    bool SupportsCreate { get; }
    bool SupportsRemoveAny { get; }
    bool SupportsMoveWindow { get; }
    bool SupportsReorder { get; }

    IReadOnlyList<Guid> GetDesktopIds();
    Guid GetCurrentDesktopId();
    bool Switch(Guid id);
    Guid? Create();
    bool Remove(Guid id, Guid fallback);
    bool MoveWindow(IntPtr hwnd, Guid id);
    bool MoveDesktop(Guid id, int index);
}

public sealed class VirtualDesktopException : Exception
{
    public VirtualDesktopException(string message, int hr) : base($"{message} (0x{hr:X8})") => HResult = hr;

    public bool IsDisconnected => HResult is unchecked((int)0x800706BA) or unchecked((int)0x80010108) or unchecked((int)0x800706BE) or unchecked((int)0x80010012);
}
