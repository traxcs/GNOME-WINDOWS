using System.Runtime.InteropServices;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.Audio;


[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notifyData);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute(int mute, ref Guid context);
    [PreserveSig] int GetMute(out int mute);
}

public sealed class AudioController : IDisposable
{
    private const int eRender = 0, eMultimedia = 1;
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private readonly Callback _callback;
    private readonly SynchronizationContext? _ui;

    public event Action? Changed;

    public AudioController()
    {
        _ui = SynchronizationContext.Current;
        _callback = new Callback(this);
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            _enumerator.RegisterEndpointNotificationCallback(_callback);
            BindDefaultDevice();
        }
        catch (Exception ex)
        {
            Log.Warn("Core Audio unavailable", ex);
        }
    }

    public bool IsAvailable => _volume != null;

    private void BindDefaultDevice()
    {
        if (_volume != null)
        {
            try { _volume.UnregisterControlChangeNotify(_callback); } catch { }
            Marshal.ReleaseComObject(_volume);
            _volume = null;
        }
        if (_enumerator == null) return;
        if (_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out IMMDevice device) != 0 || device == null) return;
        try
        {
            Guid iid = IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, (int)NativeMethods.CLSCTX_ALL, IntPtr.Zero, out object o) == 0)
            {
                _volume = (IAudioEndpointVolume)o;
                _volume.RegisterControlChangeNotify(_callback);
            }
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    public float Volume
    {
        get => _volume != null && _volume.GetMasterVolumeLevelScalar(out float v) == 0 ? v : 0f;
        set
        {
            if (_volume == null) return;
            Guid ctx = Guid.Empty;
            _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref ctx);
        }
    }

    public bool Muted
    {
        get => _volume != null && _volume.GetMute(out int m) == 0 && m != 0;
        set
        {
            if (_volume == null) return;
            Guid ctx = Guid.Empty;
            _volume.SetMute(value ? 1 : 0, ref ctx);
        }
    }

    private void Raise(bool rebind)
    {
        void Do()
        {
            if (rebind) BindDefaultDevice();
            Changed?.Invoke();
        }
        if (_ui != null) _ui.Post(_ => Do(), null); else Do();
    }

    public void Dispose()
    {
        try
        {
            if (_volume != null) { _volume.UnregisterControlChangeNotify(_callback); Marshal.ReleaseComObject(_volume); }
            if (_enumerator != null) { _enumerator.UnregisterEndpointNotificationCallback(_callback); Marshal.ReleaseComObject(_enumerator); }
        }
        catch { }
        _volume = null;
        _enumerator = null;
    }

    [ComVisible(true)]
    private sealed class Callback : IAudioEndpointVolumeCallback, IMMNotificationClient
    {
        private readonly AudioController _owner;
        public Callback(AudioController owner) => _owner = owner;
        public int OnNotify(IntPtr notifyData) { _owner.Raise(false); return 0; }
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == eRender && role == eMultimedia) _owner.Raise(true);
            return 0;
        }
        public int OnPropertyValueChanged(string id, PROPERTYKEY key) => 0;
    }
}
