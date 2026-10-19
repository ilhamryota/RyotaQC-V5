using System.IO;
using System.Linq;

namespace RyotaQC.App.Services;

public static class BackgroundImageResolver
{
    public static string? ResolveMainBackgroundPath()
    {
        var workspace = WorkspacePathService.FindWorkspaceRoot();

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Backgrounds", "bg.jpg"),
            Path.Combine(workspace, "src", "RyotaQC.App", "Assets", "Images", "Backgrounds", "bg.jpg"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Wallpapers", "WP1.jpg"),
            Path.Combine(workspace, "src", "RyotaQC.App", "Assets", "Images", "Wallpapers", "WP1.jpg")
        };

        return candidates.FirstOrDefault(IsValidImageFile);
    }

    private static bool IsValidImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Length > 0;
    }
}
