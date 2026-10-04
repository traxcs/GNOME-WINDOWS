using System.Windows.Media;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Settings;

namespace GnomeWin.Core;

public sealed class WindowInfo : ObservableObject
{
    private string _title = string.Empty;
    private bool _isActive, _isMinimized, _isMaximized;
    private ImageSource? _icon;
    private RECT _bounds;

    public WindowInfo(IntPtr handle) => Handle = handle;

    public IntPtr Handle { get; }
    public uint ProcessId { get; set; }
    public string? ProcessPath { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string? Aumid { get; set; }
    public string AppId { get; set; } = string.Empty;
    public AppEntry? App { get; set; }

    public string Title { get => _title; set { if (Set(ref _title, value)) OnPropertyChanged(nameof(DisplayTitle)); } }
    public string DisplayTitle => !string.IsNullOrWhiteSpace(_title) ? _title : App?.Name ?? Path.GetFileNameWithoutExtension(ProcessPath) ?? "?";
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }
    public bool IsMaximized { get => _isMaximized; set => Set(ref _isMaximized, value); }
    public RECT Bounds { get => _bounds; set => Set(ref _bounds, value); }
    public IntPtr Monitor { get; set; }
    public Guid DesktopId { get; set; }
    public bool IsCloakedByShell { get; set; }
    public long ActivationStamp { get; set; }
    public long CreationStamp { get; set; }

    public ImageSource? Icon
    {
        get => App?.Icon ?? _icon;
        set => Set(ref _icon, value);
    }

    public bool HasOwnIcon => _icon != null;

    public void NotifyAppChanged()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    public override string ToString() => $"0x{Handle.ToInt64():X} '{Title}' [{AppId}]";
}
