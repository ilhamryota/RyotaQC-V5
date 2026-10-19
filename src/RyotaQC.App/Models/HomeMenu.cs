using CommunityToolkit.Mvvm.ComponentModel;

namespace RyotaQC.App.Models;

public sealed partial class HomeMenu : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Description { get; init; }
    public required string IconGlyph { get; init; }
    public required string WallpaperFile { get; init; }
    public required string ImageFile { get; init; }
    public string ImagePath { get; set; } = string.Empty;
    public required RyotaQC.App.Enums.AppPage Page { get; init; }

    [ObservableProperty]
    private bool isSelected;

    /// <summary>True when the active role is not allowed to open this menu (danger zone).</summary>
    [ObservableProperty]
    private bool isBlocked;
}
