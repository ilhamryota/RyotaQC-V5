using System.IO;

namespace RyotaQC.App.Services;

public static class WorkspacePathService
{
    public static string FindWorkspaceRoot()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var candidate in candidates)
        {
            var root = WalkUpToWorkspace(candidate);
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }
        }

        return AppContext.BaseDirectory;
    }

    private static string? WalkUpToWorkspace(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return null;
        }

        var hops = 0;

        while (directory is not null && hops < 10)
        {
            try
            {
                var toolsPath = Path.Combine(directory.FullName, "tools");
                if (Directory.Exists(toolsPath))
                {
                    return directory.FullName;
                }
            }
            catch
            {
                // Ignore directories that cannot be inspected.
            }

            directory = directory.Parent;
            hops++;
        }

        return null;
    }
}
