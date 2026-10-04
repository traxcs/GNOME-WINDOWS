using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.VirtualDesktop;

public sealed class VirtualDesktopService : IDisposable
{
    private readonly PublicVirtualDesktopManager _public = new();
    private readonly bool _allowInternal;
    private IVirtualDesktopBackend _backend = null!;
    private Thread? _watchThread;
    private readonly ManualResetEvent _stop = new(false);
    private readonly SynchronizationContext? _ui;

    public event Action? Changed;

    public VirtualDesktopService(bool allowInternalApi)
    {
        _allowInternal = allowInternalApi;
        _ui = SynchronizationContext.Current;
        CreateBackend();
        StartRegistryWatcher();
    }

    public PublicVirtualDesktopManager Public => _public;
    public IVirtualDesktopBackend Backend => _backend;
    public string BackendName => _backend.Name;
    public bool IsFullFeatured => _backend is InternalComBackend;

    private void CreateBackend()
    {
        _backend?.Dispose();
        if (_allowInternal)
        {
            var internalBackend = InternalComBackend.TryCreate(out string reason);
            if (internalBackend != null)
            {
                _backend = internalBackend;
                Log.Info($"Virtual desktops: {internalBackend.Name}");
                return;
            }
            Log.Warn($"Virtual desktops: internal API not usable ({reason}); using documented shortcuts fallback.");
        }
        _backend = new ShortcutBackend(_public);
    }

    public void Reconnect()
    {
        _public.Reset();
        CreateBackend();
        Changed?.Invoke();
    }

    public T Run<T>(Func<IVirtualDesktopBackend, T> call, T fallback)
    {
        try { return call(_backend); }
        catch (VirtualDesktopException ex) when (ex.IsDisconnected)
        {
            Log.Warn("Virtual desktop backend disconnected, reconnecting.", ex);
            try { CreateBackend(); return call(_backend); }
            catch (Exception ex2) { Log.Error("Virtual desktop call failed after reconnect", ex2); return fallback; }
        }
        catch (Exception ex)
        {
            Log.Error("Virtual desktop call failed", ex);
            return fallback;
        }
    }

    public IReadOnlyList<Guid> GetDesktops() => Run(b => b.GetDesktopIds(), Array.Empty<Guid>());
    public Guid GetCurrent() => Run(b => b.GetCurrentDesktopId(), Guid.Empty);
    public Guid GetWindowDesktop(IntPtr hwnd) => _public.GetWindowDesktopId(hwnd);

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter, SafeWaitHandle evt, bool async);
    private const uint REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    private void StartRegistryWatcher()
    {
        _watchThread = new Thread(WatchLoop) { IsBackground = true, Name = "GnomeWin.VDWatch" };
        _watchThread.Start();
    }

    private void WatchLoop()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShortcutBackend.RegistryPath)
                            ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer");
            if (key == null) return;
            using var changed = new AutoResetEvent(false);
            var handles = new WaitHandle[] { _stop, changed };
            while (true)
            {
                int rc = RegNotifyChangeKeyValue(key.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, changed.SafeWaitHandle, true);
                if (rc != 0) { Log.Warn($"RegNotifyChangeKeyValue failed: {rc}"); return; }
                if (WaitHandle.WaitAny(handles) == 0) return;
                Thread.Sleep(60);
                if (_ui != null) _ui.Post(_ => Changed?.Invoke(), null);
                else Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Virtual desktop registry watcher stopped", ex);
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _watchThread?.Join(500);
        _backend?.Dispose();
    }
}
