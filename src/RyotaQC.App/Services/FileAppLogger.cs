using System.IO;
using System.Text;

namespace RyotaQC.App.Services;

public sealed class FileAppLogger : IAppLogger
{
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxArchiveFiles = 5;

    private readonly string _logPath;
    private readonly object _sync = new();

    public FileAppLogger()
    {
        _logPath = Path.Combine(ResolveLogFolder(), "app.log");
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message, ex);
    }

    private static string ResolveLogFolder()
    {
        var candidates = new[]
        {
            Path.Combine(WorkspacePathService.FindWorkspaceRoot(), "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RyotaQC", "logs"),
            Path.Combine(Path.GetTempPath(), "RyotaQC", "logs")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (Exception)
            {
                // Try the next fallback folder.
            }
        }

        return Path.GetTempPath();
    }

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
            if (ex is not null)
            {
                builder.Append($" | {ex.GetType().Name}: {ex.Message}");
            }

            lock (_sync)
            {
                RollIfNeeded();
                File.AppendAllText(_logPath, builder.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    /// <summary>
    /// Rolls the log file when it exceeds <see cref="MaxLogSizeBytes"/>.
    /// Keeps up to <see cref="MaxArchiveFiles"/> archived copies
    /// (app.log.1, app.log.2, ...). Older archives are deleted.
    /// </summary>
    private void RollIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath))
            {
                return;
            }

            var info = new FileInfo(_logPath);
            if (info.Length < MaxLogSizeBytes)
            {
                return;
            }

            var folder = Path.GetDirectoryName(_logPath);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            // Delete the oldest archive (it would shift out of the window).
            var oldest = Path.Combine(folder, $"app.log.{MaxArchiveFiles}");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            // Shift archives up: app.log.(n-1) -> app.log.n
            for (var i = MaxArchiveFiles - 1; i >= 1; i--)
            {
                var current = Path.Combine(folder, $"app.log.{i}");
                var next = Path.Combine(folder, $"app.log.{i + 1}");
                if (File.Exists(current))
                {
                    File.Move(current, next);
                }
            }

            // Rotate the active log to app.log.1
            File.Move(_logPath, Path.Combine(folder, "app.log.1"));
        }
        catch
        {
            // Rolling must never crash logging.
        }
    }
}
