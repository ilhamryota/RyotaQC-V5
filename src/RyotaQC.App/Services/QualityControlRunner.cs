using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using RyotaQC.App.Views.Windows;

namespace RyotaQC.App.Services;

public sealed class QualityControlRunner : IQualityControlRunner
{
    private readonly IUiFeedbackService _feedback;
    private readonly IAppLogger _logger;
    private readonly string _qcRoot;
    private readonly object _runningTestSync = new();
    private CancellationTokenSource? _runningTestMonitorCts;

    public QualityControlRunner(
        IUiFeedbackService feedback,
        IAppLogger logger)
    {
        _feedback = feedback;
        _logger = logger;

        var workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        _qcRoot = Path.Combine(workspaceRoot, "tools", "qc");
    }

    public void RunSentinel() => RunFolderExecutable("Hard Disk Sentinel", ["HDSentinel.exe"]);

    public void RunBatteryHealth() => RunFolderExecutable("Battery", ["batteryinfo.exe"]);

    public void RunLcdTest() => ShowDialog(new LcdTestWindow());

    public void RunKeyboardTest() => RunFolderExecutable("Keyboard Test");

    public void RunCameraTest() => LaunchUri("microsoft.windows.camera:", "Camera Test");

    public void RunAudioTest()
    {
        var folderPath = FindQcFolder("Audio Test");

        string? audio1 = null;
        string? audio2 = null;
        string? left = null;
        string? right = null;

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var allFiles = Directory
                .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(path => IsAudioVideoExtension(Path.GetExtension(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var videoFiles = allFiles.Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)).ToList();
            var audioFiles = allFiles.Where(path => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                                    Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)).ToList();

            audio1 = FindByName(videoFiles, "AudioTest.mp4")
                     ?? videoFiles.FirstOrDefault(path => Path.GetFileName(path).Contains("audiotest", StringComparison.OrdinalIgnoreCase) &&
                                                          !Path.GetFileName(path).Contains("2", StringComparison.OrdinalIgnoreCase))
                     ?? videoFiles.FirstOrDefault();

            audio2 = FindByName(videoFiles, "AudioTest 2.mp4")
                     ?? videoFiles.FirstOrDefault(path => Path.GetFileName(path).Contains("audiotest 2", StringComparison.OrdinalIgnoreCase))
                     ?? videoFiles.Skip(1).FirstOrDefault()
                     ?? audio1;

            left = FindByName(audioFiles, "Left.mp3")
                   ?? audioFiles.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains("left", StringComparison.OrdinalIgnoreCase));

            right = FindByName(audioFiles, "Right.mp3")
                    ?? audioFiles.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains("right", StringComparison.OrdinalIgnoreCase));
        }

        ShowDialog(new AudioTestWindow(audio1, audio2, left, right));
    }

    public void RunRunningTest()
    {
        var folderPath = FindQcFolder("Running Test");
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ShowFailure("Running Test belum tersedia.");
            return;
        }

        var mediaPath = ResolveMedia(folderPath, ["RT.mp4"]);
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            ShowFailure("Running Test belum tersedia.");
            return;
        }

        try
        {
            _feedback.ShowLoading("Launching Running Test...");

            // Prevent screen sleep / display off / screensaver during video.
            // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
            SetThreadExecutionState(0x80000003);

            var vlcPath = ResolveVlcExecutable();
            Process? process;

            if (!string.IsNullOrWhiteSpace(vlcPath))
            {
                // Use VLC with:
                // --fullscreen      : start fullscreen
                // --loop            : loop video so it plays continuously until battery dies
                // --hwdec=auto      : hardware decoding (fixes stuttering/heavy playback)
                // --no-video-title-show : hide title overlay
                var startInfo = new ProcessStartInfo
                {
                    FileName = vlcPath,
                    Arguments = $"--fullscreen --loop --hwdec=auto --no-video-title-show \"{mediaPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(vlcPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = true
                };
                process = Process.Start(startInfo);
                _feedback.ShowToast("Running Test dibuka di VLC.", true);
                _logger.Info("QC Running Test launched in VLC (hardware decoding + loop).");
            }
            else
            {
                // VLC not found — try Windows Media Player, then default
                var wmpPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Windows Media Player", "wmplayer.exe");

                if (File.Exists(wmpPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = wmpPath,
                        Arguments = $"\"{mediaPath}\"",
                        UseShellExecute = true
                    };
                    process = Process.Start(startInfo);
                    _feedback.ShowToast("Running Test dibuka di Windows Media Player.", true);
                    _logger.Info("QC Running Test launched in Windows Media Player (VLC not found).");
                }
                else
                {
                    // Fallback: Windows default video player (Movies & TV)
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = mediaPath,
                        UseShellExecute = true,
                        Verb = "open"
                    };
                    process = Process.Start(startInfo);
                    _feedback.ShowToast("Running Test dibuka dengan video player default.", true);
                    _logger.Info("QC Running Test launched with default video player (VLC + WMP not found).");
                }
            }

            StartRunningTestLogger(mediaPath, process);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Error("QC Running Test canceled by user.", ex);
            ShowFailure("Proses dibatalkan.");
            // Restore sleep settings
            SetThreadExecutionState(0x80000000);
        }
        catch (Exception ex)
        {
            _logger.Error("QC Running Test launch failed.", ex);
            ShowFailure("Gagal membuka Running Test.");
            // Restore sleep settings
            SetThreadExecutionState(0x80000000);
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public bool RunTouchscreenTest()
    {
        var window = new TouchscreenTestWindow();
        ShowDialog(window);
        return window.AllTapped;
    }

    public TouchpadTestResult RunTouchpadTest()
    {
        var window = new TouchpadTestWindow();
        ShowDialog(window);
        return window.Result;
    }

    public bool RunMicTest()
    {
        var window = new MicTestWindow();
        ShowDialog(window);
        return window.Passed;
    }

    public async Task RunBatteryReportAsync()
    {
        try
        {
            _feedback.ShowLoading("Generating battery report...");
            var reportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"RyotaQC_BatteryReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            var command = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c powercfg /batteryreport /output \"{reportPath}\"",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(command);
            if (process is not null)
            {
                await Task.Run(() => process.WaitForExit(15000));
            }

            if (!File.Exists(reportPath))
            {
                _feedback.ShowToast("Battery report gagal dibuat.", false);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });

            _feedback.ShowToast("Battery report dibuat.", true);
            _logger.Info($"Battery report generated: {reportPath}");
        }
        catch (Exception ex)
        {
            _logger.Error("Battery report failed.", ex);
            _feedback.ShowToast("Battery report gagal dijalankan.", false);
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    public void RunExternalTarget(string fullPath, bool runAsAdmin = false, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            ShowFailure("Target belum tersedia.");
            return;
        }

        if (Uri.TryCreate(fullPath, UriKind.Absolute, out var uri) &&
            uri.IsAbsoluteUri &&
            uri.Scheme != Uri.UriSchemeFile)
        {
            LaunchUri(fullPath, displayName ?? fullPath);
            return;
        }

        var resolvedPath = Path.GetFullPath(fullPath);
        if (!File.Exists(resolvedPath))
        {
            ShowFailure("File target tidak ditemukan.");
            return;
        }

        try
        {
            var label = displayName ?? Path.GetFileName(resolvedPath);
            _feedback.ShowLoading($"Launching {label}...");

            var startInfo = BuildStartInfo(resolvedPath, runAsAdmin);
            Process.Start(startInfo);

            _feedback.ShowToast($"{label} dijalankan.", true);
            _logger.Info($"QC launch success: {resolvedPath}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Error($"QC launch canceled by user: {resolvedPath}", ex);
            ShowFailure("Proses dibatalkan.");
        }
        catch (Exception ex)
        {
            _logger.Error($"QC launch failed: {resolvedPath}", ex);
            ShowFailure($"Gagal menjalankan {Path.GetFileName(resolvedPath)}.");
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    private void RunFolderExecutable(string folderName, IReadOnlyList<string>? preferredNames = null)
    {
        var folderPath = FindQcFolder(folderName);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ShowFailure($"{folderName} belum tersedia.");
            return;
        }

        var target = ResolveExecutable(folderPath, preferredNames);
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowFailure($"{folderName} belum tersedia.");
            return;
        }

        RunExternalTarget(target, runAsAdmin: false, displayName: Path.GetFileNameWithoutExtension(target));
    }

    private static ProcessStartInfo BuildStartInfo(string fullPath, bool runAsAdmin)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var workingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;

        ProcessStartInfo startInfo;
        switch (extension)
        {
            case ".bat":
            case ".cmd":
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{fullPath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                break;
            case ".ps1":
                startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{fullPath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                break;
            default:
                startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                break;
        }

        if (runAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    private string? FindQcFolder(string requestedName)
    {
        if (!Directory.Exists(_qcRoot))
        {
            return null;
        }

        var exactPath = Path.Combine(_qcRoot, requestedName);
        if (Directory.Exists(exactPath))
        {
            return exactPath;
        }

        var token = NormalizeToken(requestedName);

        return Directory
            .EnumerateDirectories(_qcRoot)
            .FirstOrDefault(path => NormalizeToken(Path.GetFileName(path)) == token)
            ?? Directory
                .EnumerateDirectories(_qcRoot)
                .FirstOrDefault(path => NormalizeToken(Path.GetFileName(path)).Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveExecutable(string folderPath, IReadOnlyList<string>? preferredNames = null)
    {
        var exeFiles = Directory
            .EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exeFiles.Count == 0)
        {
            return null;
        }

        if (preferredNames is not null)
        {
            foreach (var preferred in preferredNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var exact = exeFiles.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), preferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(exact))
                {
                    return exact;
                }
            }
        }

        return exeFiles
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string? ResolveMedia(string folderPath, IReadOnlyList<string>? preferredNames = null)
    {
        var mediaFiles = ResolveMediaFiles(folderPath).ToList();

        if (mediaFiles.Count == 0)
        {
            return null;
        }

        if (preferredNames is not null)
        {
            foreach (var preferred in preferredNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var exact = mediaFiles.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), preferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(exact))
                {
                    return exact;
                }
            }
        }

        return mediaFiles
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static IReadOnlyList<string> ResolveMediaFiles(string folderPath)
    {
        return Directory
            .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(path => IsAudioVideoExtension(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FindByName(IEnumerable<string> files, string fileName)
    {
        return files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveVlcExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VideoLAN", "VLC", "vlc.exe")
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\vlc.exe");
                    var value = appKey?.GetValue(string.Empty)?.ToString()?.Trim('"');
                    if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                    {
                        return value;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, "vlc.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid path segments
            }
        }

        return null;
    }

    private void LaunchUri(string uri, string label)
    {
        try
        {
            _feedback.ShowLoading($"Launching {label}...");

            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });

            _feedback.ShowToast($"{label} dibuka.", true);
            _logger.Info($"QC launch URI success: {uri}");
        }
        catch (Exception ex)
        {
            _logger.Error($"QC launch URI failed: {uri}", ex);
            ShowFailure($"Gagal membuka {label}.");
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    private void ShowFailure(string message)
    {
        _feedback.ShowToast(message, false);
    }

    private void StartRunningTestLogger(string mediaPath, Process? vlcProcess)
    {
        var startTime = DateTime.Now;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var logPath = Path.Combine(desktop, $"RyotaQC_RunningTest_{startTime:yyyyMMdd_HHmmss}.txt");

        lock (_runningTestSync)
        {
            _runningTestMonitorCts?.Cancel();
            _runningTestMonitorCts = new CancellationTokenSource();
            var token = _runningTestMonitorCts.Token;
            _ = Task.Run(() => MonitorRunningTestAsync(startTime, mediaPath, logPath, vlcProcess, token), token);
        }

        _feedback.ShowToast("Log Running Test dibuat di Desktop.", true);
        _logger.Info($"Running test logger started: {logPath}");
    }

    private async Task MonitorRunningTestAsync(
        DateTime startTime,
        string mediaPath,
        string logPath,
        Process? vlcProcess,
        CancellationToken token)
    {
        try
        {
            using var writer = new StreamWriter(logPath, false, Encoding.UTF8)
            {
                AutoFlush = true
            };

            writer.WriteLine("RYOTA QC V5 - RUNNING TEST BATTERY LOG");
            writer.WriteLine($"File Video: {mediaPath}");
            writer.WriteLine($"Mulai video dari jam: {startTime:dd MMMM yyyy HH:mm:ss}");
            writer.WriteLine();

            DateTime? lowBatteryAt = null;
            DateTime? tenSecondsBeforeOffAt = null;
            DateTime? closedAt = null;
            SystemPowerStatus? lastStatus = null;
            DateTime? lastStatusAt = null;

            while (!token.IsCancellationRequested)
            {
                if (!GetSystemPowerStatus(out var status))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                var now = DateTime.Now;
                lastStatus = status;
                lastStatusAt = now;

                var onBattery = status.ACLineStatus != 1;
                var lifeSeconds = status.BatteryLifeTime;
                var lifeText = lifeSeconds > 0 ? FormatDuration(TimeSpan.FromSeconds(lifeSeconds)) : "N/A";

                if (!lowBatteryAt.HasValue && onBattery && status.BatteryLifePercent <= 10)
                {
                    lowBatteryAt = now;
                    writer.WriteLine($"Low Battery terdeteksi pada: {now:dd MMMM yyyy HH:mm:ss} (Battery {status.BatteryLifePercent}%, estimasi sisa {lifeText})");
                }

                if (!tenSecondsBeforeOffAt.HasValue && onBattery && lifeSeconds > 0 && lifeSeconds <= 10)
                {
                    tenSecondsBeforeOffAt = now;
                    writer.WriteLine($"10 detik akhir sebelum laptop mati (estimasi): {now:dd MMMM yyyy HH:mm:ss}");
                    break;
                }

                if (vlcProcess is not null && vlcProcess.HasExited)
                {
                    closedAt = now;
                    writer.WriteLine($"Selesai Close Running Test (Video Player Exit): {now:dd MMMM yyyy HH:mm:ss}");
                    // Restore sleep settings — allow screen sleep/screensaver again
                    SetThreadExecutionState(0x80000000);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            var endForSummary = closedAt ?? tenSecondsBeforeOffAt ?? lowBatteryAt ?? lastStatusAt ?? DateTime.Now;
            writer.WriteLine();
            writer.WriteLine("RINGKASAN");
            writer.WriteLine($"Mulai video dari jam berapa: {startTime:dd MMMM yyyy HH:mm:ss}");
            writer.WriteLine($"Selesai Close Running Test di jam berapa: {(closedAt.HasValue ? closedAt.Value.ToString("dd MMMM yyyy HH:mm:ss") : "-")}");
            writer.WriteLine($"Berakhir di notifikasi low battery sampai jam berapa: {(lowBatteryAt.HasValue ? lowBatteryAt.Value.ToString("dd MMMM yyyy HH:mm:ss") : "-")}");
            writer.WriteLine($"Berakhir di 10 detik akhir sebelum laptop mati di jam berapa: {(tenSecondsBeforeOffAt.HasValue ? tenSecondsBeforeOffAt.Value.ToString("dd MMMM yyyy HH:mm:ss") : "-")}");
            writer.WriteLine($"Total dari 0 sampai notifikasi low battery: {(lowBatteryAt.HasValue ? FormatDuration(lowBatteryAt.Value - startTime) : "-")}");
            writer.WriteLine($"Total dari 0 sampai 10 detik akhir: {(tenSecondsBeforeOffAt.HasValue ? FormatDuration(tenSecondsBeforeOffAt.Value - startTime) : "-")}");
            writer.WriteLine($"Total waktu dari mulai sampai close VLC: {(closedAt.HasValue ? FormatDuration(closedAt.Value - startTime) : "-")}");
            writer.WriteLine($"Total waktu running test (last capture): {FormatDuration(endForSummary - startTime)}");

            if (lastStatus.HasValue)
            {
                writer.WriteLine();
                writer.WriteLine($"Snapshot terakhir: Battery {lastStatus.Value.BatteryLifePercent}% | AC={(lastStatus.Value.ACLineStatus == 1 ? "ON" : "OFF")} | Estimasi sisa={(lastStatus.Value.BatteryLifeTime > 0 ? FormatDuration(TimeSpan.FromSeconds(lastStatus.Value.BatteryLifeTime)) : "N/A")}");
            }
        }
        catch (OperationCanceledException)
        {
            // monitor restarted
        }
        catch (Exception ex)
        {
            _logger.Error("Running test logger failed.", ex);
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalDays = (int)duration.TotalDays;
        return $"{totalDays} hari {duration:hh\\:mm\\:ss}";
    }

    private static bool IsAudioVideoExtension(string extension)
    {
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private static void ShowDialog(Window window)
    {
        if (Application.Current?.MainWindow is not null)
        {
            window.Owner = Application.Current.MainWindow;
        }

        window.ShowDialog();
    }
}



