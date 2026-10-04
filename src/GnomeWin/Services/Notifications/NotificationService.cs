using System.Collections.ObjectModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.Notifications;

public enum NotificationSource { Windows, Shell }

public sealed class ShellNotification
{
    public required string Key { get; init; }
    public uint WindowsId { get; init; }
    public NotificationSource Source { get; init; }
    public string AppName { get; init; } = string.Empty;
    public string? AppId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTimeOffset Time { get; init; }
    public System.Windows.Media.ImageSource? Icon { get; set; }
    public string TimeText => Time.LocalDateTime.ToString(Time.LocalDateTime.Date == DateTime.Today ? "HH:mm" : "dd/MM HH:mm");
}

public sealed class NotificationService
{
    private readonly SynchronizationContext _ui;
    private UserNotificationListener? _listener;
    private bool _eventsSupported;

    public ObservableCollection<ShellNotification> Items { get; } = new();
    public bool WindowsAccessGranted { get; private set; }
    public event Action? Changed;

    public NotificationService()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public bool Initialized { get; private set; }

    public async Task InitializeAsync()
    {
        if (Initialized) return;
        Initialized = true;
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            WindowsAccessGranted = status == UserNotificationListenerAccessStatus.Allowed;
            Log.Info($"Notification listener access: {status}");
            if (!WindowsAccessGranted) return;
            try
            {
                _listener.NotificationChanged += (_, _) => _ui.Post(_ => _ = RefreshAsync(), null);
                _eventsSupported = true;
            }
            catch (Exception ex)
            {
                Log.Info("Notification change events unavailable (refresh on demand): " + ex.Message);
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            WindowsAccessGranted = false;
            Log.Info("UserNotificationListener unavailable: " + ex.Message);
        }
    }

    public bool LiveUpdates => _eventsSupported;

    public async Task RefreshAsync()
    {
        if (_listener == null || !WindowsAccessGranted) { Changed?.Invoke(); return; }
        try
        {
            var toasts = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var fresh = new List<ShellNotification>();
            foreach (var n in toasts)
            {
                string title = string.Empty, body = string.Empty;
                var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding != null)
                {
                    var texts = binding.GetTextElements().Select(t => t.Text).ToList();
                    title = texts.FirstOrDefault() ?? string.Empty;
                    body = string.Join(Environment.NewLine, texts.Skip(1));
                }
                string appName = string.Empty, appId = string.Empty;
                try { appName = n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty; appId = n.AppInfo?.AppUserModelId ?? string.Empty; } catch { }
                var item = new ShellNotification
                {
                    Key = "win:" + n.Id,
                    WindowsId = n.Id,
                    Source = NotificationSource.Windows,
                    AppName = appName,
                    AppId = appId,
                    Title = title,
                    Body = body,
                    Time = n.CreationTime,
                };
                fresh.Add(item);
                _ = LoadLogoAsync(n, item);
            }
            _ui.Post(_ =>
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                    if (Items[i].Source == NotificationSource.Windows) Items.RemoveAt(i);
                foreach (var f in fresh.OrderByDescending(f => f.Time)) Items.Add(f);
                Sort();
                Changed?.Invoke();
            }, null);
        }
        catch (Exception ex)
        {
            Log.Warn("Reading notifications failed", ex);
        }
    }

    private async Task LoadLogoAsync(UserNotification n, ShellNotification target)
    {
        try
        {
            var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
            if (logo == null) return;
            using var stream = await logo.OpenReadAsync();
            using var net = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
            var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            _ui.Post(_ =>
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    target.Icon = bmp;
                    Changed?.Invoke();
                }
                catch { }
            }, null);
        }
        catch { /* logo is optional */ }
    }

    public void AddShellNotice(string title, string body)
    {
        Items.Insert(0, new ShellNotification
        {
            Key = "shell:" + Guid.NewGuid(),
            Source = NotificationSource.Shell,
            AppName = "GnomeWin",
            Title = title,
            Body = body,
            Time = DateTimeOffset.Now,
        });
        Changed?.Invoke();
    }

    public void Dismiss(ShellNotification n)
    {
        Items.Remove(n);
        if (n.Source == NotificationSource.Windows && _listener != null)
        {
            try { _listener.RemoveNotification(n.WindowsId); } catch (Exception ex) { Log.Debug("RemoveNotification: " + ex.Message); }
        }
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        Items.Clear();
        if (_listener != null && WindowsAccessGranted)
        {
            try { _listener.ClearNotifications(); } catch (Exception ex) { Log.Debug("ClearNotifications: " + ex.Message); }
        }
        Changed?.Invoke();
    }

    private void Sort()
    {
        var sorted = Items.OrderByDescending(i => i.Time).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int cur = Items.IndexOf(sorted[i]);
            if (cur != i) Items.Move(cur, i);
        }
    }
}
