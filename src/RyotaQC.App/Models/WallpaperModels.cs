namespace RyotaQC.App.Models;

public sealed class WallpaperMapConfig
{
    public List<string> Wallpapers { get; init; } = [];
}

public sealed class WallpaperItemModel
{
    public string Name { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string AbsolutePath { get; init; } = string.Empty;

    public bool Exists { get; init; }
}
