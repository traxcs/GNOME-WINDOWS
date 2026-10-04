using System.Runtime.InteropServices;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.VirtualDesktop;

public sealed unsafe class InternalComBackend : IVirtualDesktopBackend
{
    private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid IID_IServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid IID_IApplicationViewCollection = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
    private static readonly Guid IID_IObjectArray = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid IID_IVirtualDesktop = new("3F07F4BE-B107-441A-AF0F-39D82529072C");

    private static readonly (string Build, Guid Iid)[] ManagerInternalIids =
    {
        ("24H2 (26100)", new Guid("53F5CA0B-158F-4124-900C-057158060B27")),
        ("23H2 (22631)", new Guid("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E")),
    };

    private const int Slot_GetCount = 3;
    private const int Slot_MoveViewToDesktop = 4;
    private const int Slot_GetCurrentDesktop = 6;
    private const int Slot_GetDesktops = 7;
    private const int Slot_SwitchDesktop = 9;
    private const int Slot_CreateDesktop = 11;
    private const int Slot_MoveDesktop = 12;
    private const int Slot_RemoveDesktop = 13;
    private const int Slot_Desktop_GetId = 4;
    private const int Slot_Array_GetCount = 3;
    private const int Slot_Array_GetAt = 4;
    private const int Slot_GetViewForHwnd = 6;

    private IntPtr _serviceProvider;
    private IntPtr _manager;
    private IntPtr _views;

    public string Name { get; private set; } = "Internal COM";
    public bool SupportsDirectSwitch => true;
    public bool SupportsCreate => true;
    public bool SupportsRemoveAny => true;
    public bool SupportsMoveWindow => _views != IntPtr.Zero;
    public bool SupportsReorder => true;

    private InternalComBackend() { }

    public static InternalComBackend? TryCreate(out string reason)
    {
        var b = new InternalComBackend();
        try
        {
            Guid clsid = CLSID_ImmersiveShell, iid = IID_IServiceProvider;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out b._serviceProvider);
            if (hr != 0) { reason = $"ImmersiveShell unavailable (0x{hr:X8})"; b.Dispose(); return null; }

            foreach (var (build, candidate) in ManagerInternalIids)
            {
                if (QueryService(b._serviceProvider, CLSID_VirtualDesktopManagerInternal, candidate, out b._manager) == 0 && b._manager != IntPtr.Zero)
                {
                    b.Name = $"Internal COM, Windows 11 {build} layout";
                    break;
                }
            }
            if (b._manager == IntPtr.Zero) { reason = "IVirtualDesktopManagerInternal: no known IID for this Windows build"; b.Dispose(); return null; }

            if (QueryService(b._serviceProvider, IID_IApplicationViewCollection, IID_IApplicationViewCollection, out b._views) != 0)
                b._views = IntPtr.Zero;

            int count = b.GetCount();
            var ids = b.GetDesktopIds();
            Guid current = b.GetCurrentDesktopId();
            if (count <= 0 || count != ids.Count || !ids.Contains(current))
            {
                reason = $"validation failed (count={count}, list={ids.Count}, current={current})";
                b.Dispose();
                return null;
            }
            reason = "ok";
            return b;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            b.Dispose();
            return null;
        }
    }

    private static void* Fn(IntPtr obj, int slot) => (*(void***)obj)[slot];

    private static int QueryService(IntPtr sp, Guid service, Guid iid, out IntPtr result)
    {
        var f = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Fn(sp, 3);
        IntPtr r = IntPtr.Zero;
        int hr = f(sp, &service, &iid, &r);
        result = r;
        return hr;
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new VirtualDesktopException(what, hr);
    }

    private void EnsureAlive()
    {
        if (_manager == IntPtr.Zero) throw new VirtualDesktopException("backend disposed", unchecked((int)0x80010108));
    }

    private int GetCount()
    {
        EnsureAlive();
        var f = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Fn(_manager, Slot_GetCount);
        int c;
        Check(f(_manager, &c), "GetCount");
        return c;
    }

    private static Guid DesktopId(IntPtr desktop)
    {
        var f = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)Fn(desktop, Slot_Desktop_GetId);
        Guid g;
        Check(f(desktop, &g), "IVirtualDesktop.GetId");
        return g;
    }

    private List<IntPtr> GetDesktopPointers()
    {
        EnsureAlive();
        var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Fn(_manager, Slot_GetDesktops);
        IntPtr array;
        Check(f(_manager, &array), "GetDesktops");
        var list = new List<IntPtr>();
        try
        {
            var getCount = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Fn(array, Slot_Array_GetCount);
            var getAt = (delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)Fn(array, Slot_Array_GetAt);
            uint n;
            Check(getCount(array, &n), "IObjectArray.GetCount");
            Guid iid = IID_IVirtualDesktop;
            for (uint i = 0; i < n; i++)
            {
                IntPtr d;
                Check(getAt(array, i, &iid, &d), "IObjectArray.GetAt");
                list.Add(d);
            }
        }
        catch
        {
            foreach (var p in list) Marshal.Release(p);
            throw;
        }
        finally { Marshal.Release(array); }
        return list;
    }

    private T WithDesktop<T>(Guid id, Func<IntPtr, T> action, T notFound)
    {
        var desktops = GetDesktopPointers();
        try
        {
            foreach (var d in desktops)
                if (DesktopId(d) == id) return action(d);
            return notFound;
        }
        finally { foreach (var p in desktops) Marshal.Release(p); }
    }

    public IReadOnlyList<Guid> GetDesktopIds()
    {
        var desktops = GetDesktopPointers();
        try { return desktops.Select(DesktopId).ToList(); }
        finally { foreach (var p in desktops) Marshal.Release(p); }
    }

    public Guid GetCurrentDesktopId()
    {
        EnsureAlive();
        var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Fn(_manager, Slot_GetCurrentDesktop);
        IntPtr d;
        Check(f(_manager, &d), "GetCurrentDesktop");
        try { return DesktopId(d); }
        finally { Marshal.Release(d); }
    }

    public bool Switch(Guid id) => WithDesktop(id, d =>
    {
        var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Fn(_manager, Slot_SwitchDesktop);
        Check(f(_manager, d), "SwitchDesktop");
        return true;
    }, false);

    public Guid? Create()
    {
        EnsureAlive();
        var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Fn(_manager, Slot_CreateDesktop);
        IntPtr d;
        Check(f(_manager, &d), "CreateDesktop");
        try { return DesktopId(d); }
        finally { Marshal.Release(d); }
    }

    public bool Remove(Guid id, Guid fallback)
    {
        if (id == fallback) return false;
        var desktops = GetDesktopPointers();
        try
        {
            IntPtr target = IntPtr.Zero, fb = IntPtr.Zero;
            foreach (var d in desktops)
            {
                Guid g = DesktopId(d);
                if (g == id) target = d;
                else if (g == fallback) fb = d;
            }
            if (target == IntPtr.Zero || fb == IntPtr.Zero) return false;
            var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)Fn(_manager, Slot_RemoveDesktop);
            Check(f(_manager, target, fb), "RemoveDesktop");
            return true;
        }
        finally { foreach (var p in desktops) Marshal.Release(p); }
    }

    public bool MoveWindow(IntPtr hwnd, Guid id)
    {
        if (_views == IntPtr.Zero) return false;
        var getView = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Fn(_views, Slot_GetViewForHwnd);
        IntPtr view;
        int hr = getView(_views, hwnd, &view);
        if (hr < 0 || view == IntPtr.Zero)
        {
            Log.Debug($"No application view for 0x{hwnd.ToInt64():X} (0x{hr:X8})");
            return false;
        }
        IntPtr viewPtr = view;
        try
        {
            return WithDesktop(id, d =>
            {
                var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)Fn(_manager, Slot_MoveViewToDesktop);
                Check(f(_manager, viewPtr, d), "MoveViewToDesktop");
                return true;
            }, false);
        }
        finally { Marshal.Release(viewPtr); }
    }

    public bool MoveDesktop(Guid id, int index) => WithDesktop(id, d =>
    {
        var f = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Fn(_manager, Slot_MoveDesktop);
        Check(f(_manager, d, index), "MoveDesktop");
        return true;
    }, false);

    public void Dispose()
    {
        if (_views != IntPtr.Zero) { try { Marshal.Release(_views); } catch { } _views = IntPtr.Zero; }
        if (_manager != IntPtr.Zero) { try { Marshal.Release(_manager); } catch { } _manager = IntPtr.Zero; }
        if (_serviceProvider != IntPtr.Zero) { try { Marshal.Release(_serviceProvider); } catch { } _serviceProvider = IntPtr.Zero; }
    }
}
