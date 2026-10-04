using System.Collections.ObjectModel;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.Services.Settings;

namespace GnomeWin.Shell.Dock;

public sealed class DockItemViewModel : ObservableObject
{
    private bool _isPinned, _isActive, _isLaunching, _isSelected, _isDropTarget;
    private int _windowCount;
    private AppEntry? _app;

    public DockItemViewModel(string id) => Id = id;

    public string Id { get; }
    public bool IsShowApps => Id == ShowAppsId;
    public const string ShowAppsId = "::apps";

    public AppEntry? App
    {
        get => _app;
        set
        {
            if (ReferenceEquals(_app, value)) return;
            if (_app != null) _app.PropertyChanged -= OnAppChanged;
            _app = value;
            if (_app != null) _app.PropertyChanged += OnAppChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(Name));
        }
    }

    private void OnAppChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppEntry.Icon)) OnPropertyChanged(nameof(Icon));
    }

    public string Name => IsShowApps ? UI.Loc.T("ShowApplications") : App?.Name ?? Id;
    public ImageSource? Icon => App?.Icon ?? FallbackIcon;
    public ImageSource? FallbackIcon { get; set; }

    public List<WindowInfo> Windows { get; private set; } = new();

    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public bool IsRunning => _windowCount > 0;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsLaunching { get => _isLaunching; set => Set(ref _isLaunching, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }

    public int WindowCount
    {
        get => _windowCount;
        private set
        {
            if (Set(ref _windowCount, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                Dots.Clear();
                for (int i = 0; i < Math.Min(value, 4); i++) Dots.Add(i);
            }
        }
    }

    public ObservableCollection<int> Dots { get; } = new();

    public void SetWindows(List<WindowInfo> windows)
    {
        Windows = windows;
        WindowCount = windows.Count;
        IsActive = windows.Any(w => w.IsActive);
        if (windows.Count > 0) IsLaunching = false;
    }
}
