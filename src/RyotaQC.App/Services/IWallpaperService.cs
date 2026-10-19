using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public interface IWallpaperService
{
    IReadOnlyList<WallpaperItemModel> GetWallpapers();

    string GetCurrentWallpaperPath();

    bool SetWallpaper(WallpaperItemModel wallpaper, out string error);

    void Refresh();
}
