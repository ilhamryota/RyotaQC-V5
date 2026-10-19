using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RyotaQC.App.Models;

namespace RyotaQC.App.Services;

public sealed class ExternalToolLauncher : IExternalToolLauncher
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwRestore = 9;

    private readonly IToolConfigService _toolConfigService;
    private readonly IAppLogger _logger;
    private readonly IUiFeedbackService _feedback;

    public ExternalToolLauncher(
        IToolConfigService toolConfigService,
        IAppLogger logger,
        IUiFeedbackService feedback)
    {
        _toolConfigService = toolConfigService;
        _logger = logger;
        _feedback = feedback;
    }

    public ToolLaunchResult LaunchById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail("Tool id kosong.", id);
        }

        var tool = _toolConfigService.GetToolById(id);
        if (tool is null)
        {
            return Fail($"Tool id '{id}' tidak ditemukan di tool-map.json.", id);
        }

        return Launch(tool);
    }

    public ToolLaunchResult Launch(ToolConfig tool)
    {
        if (!_toolConfigService.IsSupportedType(tool.Type))
        {
            return Fail($"Type launcher tidak didukung: {tool.Type}", tool.Id);
        }

        if (string.IsNullOrWhiteSpace(tool.RelativePath))
        {
            return Fail($"Path launcher kosong untuk tool: {tool.Name}", tool.Id);
        }

        var resolvedPath = _toolConfigService.ResolvePath(tool.RelativePath);
        var normalizedType = tool.Type.Trim().ToLowerInvariant();

        if (!ValidateTarget(tool, normalizedType, resolvedPath, out var validationError))
        {
            return Fail(validationError, tool.Id);
        }

        var startInfo = BuildStartInfo(tool, normalizedType, resolvedPath);
        if (startInfo is null)
        {
            return Fail($"Gagal membuat ProcessStartInfo untuk tool: {tool.Name}", tool.Id);
        }

        try
        {
            _feedback.ShowLoading($"Launching {tool.Name}...");
            var process = Process.Start(startInfo);
            _feedback.ShowToast($"{tool.Name} launched.", true);
            _logger.Info($"Launch success: id={tool.Id}, type={tool.Type}, path={tool.RelativePath}");

            // Bawa jendela launcher ke foreground supaya tidak tertutup
            // oleh jendela fullscreen RyotaQC (terutama untuk cmd.exe MAS).
            BringProcessToForeground(process);

            return new ToolLaunchResult
            {
                Success = true,
                Message = $"{tool.Name} launched.",
                ToolId = tool.Id
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Launch failed: id={tool.Id}, type={tool.Type}, path={tool.RelativePath}", ex);
            return ex is Win32Exception win32 && win32.NativeErrorCode == 1223 ? Fail("Proses dibatalkan.", tool.Id) : Fail($"Gagal menjalankan {tool.Name}.", tool.Id);
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    private ProcessStartInfo? BuildStartInfo(ToolConfig tool, string type, string resolvedPath)
    {
        var args = tool.Arguments?.Trim() ?? string.Empty;
        var workingDir = ResolveWorkingDirectory(tool, resolvedPath);

        return type switch
        {
            "exe" => new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            "bat" => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = BuildBatArguments(resolvedPath, args, tool.KeepOpen),
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            "cmd" => BuildCmdStartInfo(tool, resolvedPath, args, workingDir),
            "powershell" => BuildPowerShellStartInfo(tool, resolvedPath, args, workingDir),
            "msc" => new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            "url" => new ProcessStartInfo
            {
                FileName = resolvedPath,
                UseShellExecute = true
            },
            _ => null
        };
    }

    private static ProcessStartInfo BuildCmdStartInfo(ToolConfig tool, string resolvedPath, string args, string workingDir)
    {
        if (resolvedPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = BuildCommandArguments(resolvedPath, args),
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            };
        }

        return new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = true,
            Verb = tool.RunAsAdmin ? "runas" : string.Empty
        };
    }

    private static ProcessStartInfo BuildPowerShellStartInfo(ToolConfig tool, string resolvedPath, string args, string workingDir)
    {
        if (resolvedPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var fullArgs = $"-ExecutionPolicy Bypass -File \"{resolvedPath}\"";
            if (!string.IsNullOrWhiteSpace(args))
            {
                fullArgs += $" {args}";
            }

            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = fullArgs,
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            };
        }

        return new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = true,
            Verb = tool.RunAsAdmin ? "runas" : string.Empty
        };
    }

    private bool ValidateTarget(ToolConfig tool, string type, string resolvedPath, out string error)
    {
        if (type == "url")
        {
            if (!Uri.TryCreate(resolvedPath, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = $"URL tidak valid untuk tool '{tool.Name}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (!ValidateExtension(type, resolvedPath))
        {
            error = $"Extension tidak sesuai untuk type '{type}' pada tool '{tool.Name}'.";
            return false;
        }

        if (IsCommandStylePath(resolvedPath) && type != "bat")
        {
            error = string.Empty;
            return true;
        }

        if (!File.Exists(resolvedPath))
        {
            error = "File launcher tidak ditemukan.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateExtension(string type, string path)
    {
        if (IsCommandStylePath(path))
        {
            return type is "cmd" or "powershell" or "msc";
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return type switch
        {
            "exe" => extension == ".exe",
            "bat" => extension is ".bat" or ".cmd",
            "cmd" => extension is ".cmd" or ".exe",
            "powershell" => extension is ".ps1" or ".exe",
            "msc" => extension == ".msc",
            _ => false
        };
    }

    private string ResolveWorkingDirectory(ToolConfig tool, string resolvedPath)
    {
        if (!string.IsNullOrWhiteSpace(tool.WorkingDirectory))
        {
            var configured = _toolConfigService.ResolvePath(tool.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }

        if (File.Exists(resolvedPath))
        {
            return Path.GetDirectoryName(resolvedPath) ?? WorkspacePathService.FindWorkspaceRoot();
        }

        return WorkspacePathService.FindWorkspaceRoot();
    }

    private static bool IsCommandStylePath(string value)
    {
        return !value.Contains(Path.DirectorySeparatorChar) && !value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static string BuildCommandArguments(string path, string args)
    {
        return !string.IsNullOrWhiteSpace(args) ? $"/c \"{path}\" {args}" : $"/c \"{path}\"";
    }

    /// <summary>
    /// Builds cmd.exe arguments for a .bat/.cmd file.
    /// Uses /k when keepOpen is true so the console window stays open.
    /// </summary>
    private static string BuildBatArguments(string path, string args, bool keepOpen)
    {
        var flag = keepOpen ? "/k" : "/c";
        return string.IsNullOrWhiteSpace(args)
            ? $"{flag} \"{path}\""
            : $"{flag} \"{path}\" {args}";
    }

    /// <summary>
    /// Bawa jendela process yang baru di-launch ke foreground.
    /// Penting untuk launcher (mis. MAS_AIO.cmd) supaya tidak tertutup
    /// oleh jendela fullscreen RyotaQC. Tunggu sebentar agar window handle
    /// sudah ter-create, lalu paksa Show + SetForegroundWindow.
    /// </summary>
    private static void BringProcessToForeground(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            // cmd.exe adalah console process; WaitForInputIdle melempar exception,
            // jadi poll MainWindowHandle sampai tersedia (max ~3 detik).
            var handle = IntPtr.Zero;
            for (var i = 0; i < 30; i++)
            {
                process.Refresh();
                handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    break;
                }

                System.Threading.Thread.Sleep(100);
            }

            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
            }
        }
        catch
        {
            // Best-effort; jangan gagalkan launch kalau foreground gagal.
        }
    }

    private ToolLaunchResult Fail(string message, string toolId)
    {
        _feedback.ShowToast(message, false);
        _logger.Error(message);

        return new ToolLaunchResult
        {
            Success = false,
            Message = message,
            ToolId = toolId
        };
    }
}


