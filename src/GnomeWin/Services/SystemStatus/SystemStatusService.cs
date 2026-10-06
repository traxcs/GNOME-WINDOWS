using System.Management;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using GnomeWin.Platform.Audio;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.SystemStatus;

public enum NetworkKind { None, Wifi, Ethernet, Other }

public sealed class SystemStatusService : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly AudioController _audio;
    private Radio? _wifiRadio, _btRadio;

    public event Action? NetworkChanged;
    public event Action? AudioChanged;
    public event Action? PowerChanged;
    public event Action? RadiosChanged;
    public event Action? BrightnessChanged;

    public NetworkKind Network { get; private set; }
    public string? Ssid { get; private set; }
    public int SignalBars { get; private set; } = -1;
    public bool HasBattery { get; private set; }
    public int BatteryPercent { get; private set; }
    public bool Charging { get; private set; }
    public bool BatterySaver { get; private set; }
    public int? Brightness { get; private set; }
    public bool? WifiOn => _wifiRadio == null ? null : _wifiRadio.State == RadioState.On;
    public bool? BluetoothOn => _btRadio == null ? null : _btRadio.State == RadioState.On;

    public float Volume { get => _audio.Volume; set => _audio.Volume = value; }
    public bool Muted { get => _audio.Muted; set => _audio.Muted = value; }
    public bool AudioAvailable => _audio.IsAvailable;
    public string? AudioDeviceName => _audio.DeviceName;

    public SystemStatusService()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _audio = new AudioController();
        _audio.Changed += () => AudioChanged?.Invoke();

        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        RefreshNetwork();
        RefreshPower();
    }

    private bool _details;

    public void EnsureDetails()
    {
        if (_details) { RefreshNetwork(); RefreshBrightnessAsync(); return; }
        _details = true;
        try { NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged; }
        catch (Exception ex) { Log.Warn("NetworkStatusChanged unavailable", ex); }
        RefreshNetwork();
        _ = InitRadiosAsync();
        RefreshBrightnessAsync();
    }

    private void Post(Action a) => _ui.Post(_ => a(), null);

    private void OnNetworkStatusChanged(object? sender) => Post(RefreshNetwork);
    private void OnNetworkChanged(object? sender, EventArgs e) => Post(RefreshNetwork);

    private void RefreshNetworkBasic()
    {
        Network = NetworkKind.None; Ssid = null; SignalBars = -1;
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
            if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
            var kind = ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? NetworkKind.Wifi
                     : ni.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet ? NetworkKind.Ethernet
                     : NetworkKind.Other;
            if (Network == NetworkKind.None || kind == NetworkKind.Wifi) Network = kind;
        }
    }

    public void RefreshNetwork()
    {
        if (!_details)
        {
            try { RefreshNetworkBasic(); } catch (Exception ex) { Log.Debug("Network refresh failed: " + ex.Message); }
            NetworkChanged?.Invoke();
            return;
        }
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
            {
                Network = NetworkKind.None; Ssid = null; SignalBars = -1;
            }
            else if (profile.IsWlanConnectionProfile)
            {
                Network = NetworkKind.Wifi;
                try { Ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); } catch { Ssid = null; }
                SignalBars = profile.GetSignalBars() ?? -1;
            }
            else
            {
                Network = profile.IsWwanConnectionProfile ? NetworkKind.Other : NetworkKind.Ethernet;
                Ssid = profile.ProfileName;
                SignalBars = -1;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Network refresh failed: " + ex.Message);
            Network = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? NetworkKind.Other : NetworkKind.None;
        }
        NetworkChanged?.Invoke();
    }

    public void RefreshPower()
    {
        if (NativeMethods.GetSystemPowerStatus(out var s))
        {
            HasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
            BatteryPercent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 0;
            Charging = s.ACLineStatus == 1 && (s.BatteryFlag & 8) != 0;
            BatterySaver = s.SystemStatusFlag == 1;
        }
        PowerChanged?.Invoke();
    }

    private async Task InitRadiosAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed)
            {
                Log.Info($"Radio access: {access}");
                return;
            }
            var radios = await Radio.GetRadiosAsync();
            _wifiRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            _btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_wifiRadio != null) _wifiRadio.StateChanged += (_, _) => Post(() => RadiosChanged?.Invoke());
            if (_btRadio != null) _btRadio.StateChanged += (_, _) => Post(() => RadiosChanged?.Invoke());
            Post(() => RadiosChanged?.Invoke());
        }
        catch (Exception ex)
        {
            Log.Info("Radios API unavailable: " + ex.Message);
        }
    }

    public async Task<bool> SetRadioAsync(bool wifi, bool on)
    {
        var radio = wifi ? _wifiRadio : _btRadio;
        if (radio == null) return false;
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            Log.Warn("Radio toggle failed", ex);
            return false;
        }
    }

    public void RefreshBrightnessAsync()
    {
        Task.Run(() =>
        {
            int? value = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo) value = Convert.ToInt32(mo["CurrentBrightness"]);
                    break;
                }
            }
            catch { /* desktop monitors: not supported through WMI */ }
            Post(() => { Brightness = value; BrightnessChanged?.Invoke(); });
        });
    }

    public void SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Brightness = percent;
        Task.Run(() =>
        {
            try
            {
                using var cls = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
                foreach (ManagementObject mo in cls.GetInstances())
                {
                    using (mo) mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                }
            }
            catch (Exception ex) { Log.Debug("Brightness change failed: " + ex.Message); }
        });
    }

    public void Dispose()
    {
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        if (_details) { try { NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged; } catch { } }
        _audio.Dispose();
    }
}
