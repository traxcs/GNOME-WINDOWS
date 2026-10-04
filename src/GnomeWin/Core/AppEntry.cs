using System.Windows.Media;
using GnomeWin.Services.Settings;

namespace GnomeWin.Core;

public sealed class AppEntry : ObservableObject
{
    private ImageSource? _icon;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ExePath { get; set; }
    public bool IdIsAumid { get; init; }
    public bool IsTransient { get; init; }
    public string? Description { get; set; }

    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    public string SearchName { get; set; } = string.Empty;
    public string SearchExe { get; set; } = string.Empty;

    public string LaunchTarget => IsTransient && ExePath != null ? ExePath : @"shell:AppsFolder\" + Id;

    public override string ToString() => $"{Name} ({Id})";
}
