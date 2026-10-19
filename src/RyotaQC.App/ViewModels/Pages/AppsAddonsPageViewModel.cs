using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using Microsoft.Win32;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

[SupportedOSPlatform("windows")]
public partial class AppsAddonsPageViewModel : ObservableObject
{
    private readonly IUiFeedbackService _feedback;
    private readonly IAppLogger _logger;
    private readonly INavigationService _navigationService;
    private readonly bool _isRunningAsAdmin;
    private readonly string _workspaceRoot;
    private readonly string _onlineFolderPath;
    private readonly string _offlineFolderPath;
    private readonly string _officeFolderPath;
    private readonly string[] _tilesFolderCandidates;
    private readonly Stopwatch _installTimer = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly Dictionary<string, string[]> _appShortcutHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = ["chrome.exe"],
        ["firefox"] = ["firefox.exe"],
        ["vlc"] = ["vlc.exe"],
        ["winrar"] = ["winrar.exe"],
        ["zoom"] = ["zoom.exe"],
        ["canva"] = ["canva.exe"],
        ["teamviewer"] = ["teamviewer.exe"],
        ["office"] = ["winword.exe", "excel.exe"]
    };

    [ObservableProperty]
    private bool hasOnlineApps;

    [ObservableProperty]
    private bool hasOfflineApps;

    [ObservableProperty]
    private bool hasOfficeApps;

    [ObservableProperty]
    private string onlinePrimaryFileName = "-";

    [ObservableProperty]
    private string officePrimaryFileName = "-";

    [ObservableProperty]
    private string selectedCategory = "online";

    [ObservableProperty]
    private bool isOnlineSelected = true;

    [ObservableProperty]
    private bool isOfflineSelected;

    [ObservableProperty]
    private bool isOfficeSelected;

    [ObservableProperty]
    private string onlineCardImagePath = string.Empty;

    [ObservableProperty]
    private string offlineCardImagePath = string.Empty;

    [ObservableProperty]
    private string officeCardImagePath = string.Empty;

    [ObservableProperty]
    private bool isOfflineInstallRunning;

    [ObservableProperty]
    private double offlineInstallProgress;

    [ObservableProperty]
    private string offlineInstallStatus = "Idle";

    [ObservableProperty]
    private string offlineCurrentItem = "-";

    [ObservableProperty]
    private string offlineNextItem = "-";

    [ObservableProperty]
    private string offlineElapsedText = "00:00";

    [ObservableProperty]
    private int selectedOfflineCount;

    [ObservableProperty]
    private string selectedOfflineItemsText = "-";

    public AppsAddonsPageViewModel(
        IUiFeedbackService feedback,
        IAppLogger logger,
        INavigationService navigationService)
    {
        _feedback = feedback;
        _logger = logger;
        _navigationService = navigationService;
        _isRunningAsAdmin = IsCurrentProcessElevated();

        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        _onlineFolderPath = Path.Combine(_workspaceRoot, "tools", "addons", "online");
        _offlineFolderPath = Path.Combine(_workspaceRoot, "tools", "addons", "offline");
        _officeFolderPath = Path.Combine(_workspaceRoot, "tools", "addons", "Office");
        _tilesFolderCandidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles"),
            Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Tiles")
        ];

        OnlineApps = new ObservableCollection<AddonToolItem>();
        OfflineApps = new ObservableCollection<AddonToolItem>();
        OfficeApps = new ObservableCollection<AddonToolItem>();
        OfflineCompletedItems = new ObservableCollection<string>();

        RefreshCommand = new RelayCommand(Load);
        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
        SelectCategoryCommand = new RelayCommand<string>(SelectCategory);
        RunOnlinePrimaryCommand = new AsyncRelayCommand(RunOnlinePrimaryAsync, () => HasOnlineApps && !IsOfflineInstallRunning);
        InstallSelectedOfflineCommand = new AsyncRelayCommand(InstallSelectedOfflineAsync, () => SelectedOfflineCount > 0 && !IsOfflineInstallRunning);
        InstallAllOfflineCommand = new AsyncRelayCommand(InstallAllOfflineAsync, () => HasOfflineApps && !IsOfflineInstallRunning);
        RunOfficePrimaryCommand = new AsyncRelayCommand(RunOfficePrimaryAsync, () => HasOfficeApps && !IsOfflineInstallRunning);

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) =>
        {
            if (_installTimer.IsRunning)
            {
                OfflineElapsedText = $"{(int)_installTimer.Elapsed.TotalMinutes:00}:{_installTimer.Elapsed.Seconds:00}";
            }
        };

        Load();
    }

    public ObservableCollection<AddonToolItem> OnlineApps { get; }

    public ObservableCollection<AddonToolItem> OfflineApps { get; }

    public ObservableCollection<AddonToolItem> OfficeApps { get; }

    public ObservableCollection<string> OfflineCompletedItems { get; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand BackToHomeCommand { get; }

    public IRelayCommand<string> SelectCategoryCommand { get; }

    public IAsyncRelayCommand RunOnlinePrimaryCommand { get; }

    public IAsyncRelayCommand InstallSelectedOfflineCommand { get; }

    public IAsyncRelayCommand InstallAllOfflineCommand { get; }

    public IAsyncRelayCommand RunOfficePrimaryCommand { get; }

    partial void OnSelectedCategoryChanged(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "online";
        IsOnlineSelected = normalized is not "offline" and not "office";
        IsOfflineSelected = normalized == "offline";
        IsOfficeSelected = normalized == "office";
    }

    partial void OnIsOfflineInstallRunningChanged(bool value)
    {
        RunOnlinePrimaryCommand.NotifyCanExecuteChanged();
        InstallSelectedOfflineCommand.NotifyCanExecuteChanged();
        InstallAllOfflineCommand.NotifyCanExecuteChanged();

        foreach (var item in OnlineApps)
        {
            item.LaunchCommand?.NotifyCanExecuteChanged();
        }

        foreach (var item in OfflineApps)
        {
            item.LaunchCommand?.NotifyCanExecuteChanged();
        }

        foreach (var item in OfficeApps)
        {
            item.LaunchCommand?.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedOfflineCountChanged(int value)
    {
        InstallSelectedOfflineCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasOnlineAppsChanged(bool value)
    {
        RunOnlinePrimaryCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasOfflineAppsChanged(bool value)
    {
        InstallAllOfflineCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasOfficeAppsChanged(bool value)
    {
        RunOfficePrimaryCommand.NotifyCanExecuteChanged();
    }

    private void SelectCategory(string? category)
    {
        var normalized = category?.Trim().ToLowerInvariant();
        SelectedCategory = normalized switch
        {
            "offline" => "offline",
            "office" => "office",
            _ => "online"
        };
    }

    private void Load()
    {
        EnsureFolder(_onlineFolderPath);
        EnsureFolder(_offlineFolderPath);

        OnlineCardImagePath = ResolveCardImagePath("online-apps-card", "online-apps", "online", "apps-addons");
        OfflineCardImagePath = ResolveCardImagePath("offline-installer-card", "offline-installer", "offline", "apps-addons");
        OfficeCardImagePath = ResolveCardImagePath("office-card", "office", "apps-addons");

        LoadOnlineApps();
        LoadOfflineApps();
        LoadOfficeApps();

        HasOnlineApps = OnlineApps.Count > 0;
        HasOfflineApps = OfflineApps.Count > 0;
        HasOfficeApps = OfficeApps.Count > 0;
        UpdateOfflineSelectionSummary();
    }

    private void LoadOnlineApps()
    {
        OnlineApps.Clear();

        var files = Directory.EnumerateFiles(_onlineFolderPath, "*.exe", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var file in files)
        {
            var item = new AddonToolItem
            {
                Name = file.Name,
                FullPath = file.FullName,
                Category = "online"
            };
            item.LaunchCommand = new AsyncRelayCommand(() => InstallSingleItemAsync(item), () => !IsOfflineInstallRunning);
            OnlineApps.Add(item);
        }

        OnlinePrimaryFileName = OnlineApps.Count > 0 ? OnlineApps[0].Name : "-";
    }

    private void LoadOfflineApps()
    {
        foreach (var existing in OfflineApps)
        {
            existing.PropertyChanged -= OfflineItemOnPropertyChanged;
        }

        OfflineApps.Clear();

        var directories = new DirectoryInfo(_offlineFolderPath)
            .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
            .OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in directories)
        {
            var targetExe = ResolveOfflineTargetExe(folder);
            if (string.IsNullOrWhiteSpace(targetExe))
            {
                continue;
            }

            var item = new AddonToolItem
            {
                Name = folder.Name,
                FullPath = targetExe,
                Category = "offline",
                IconPath = ResolveOfflineAppIcon(folder.Name)
            };
            item.LaunchCommand = new AsyncRelayCommand(() => InstallSingleItemAsync(item), () => !IsOfflineInstallRunning);
            item.ToggleSelectedCommand = new RelayCommand(() => item.IsSelected = !item.IsSelected);
            item.PropertyChanged += OfflineItemOnPropertyChanged;
            OfflineApps.Add(item);
        }
    }

    private string ResolveOfflineAppIcon(string folderName)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Icons", "Apps"),
            Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Icons", "Apps")
        };

        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Canva"] = "canva.png",
            ["Chrome"] = "chrome.png",
            ["DirectX"] = "directX.png",
            ["Firefox"] = "firefox.png",
            ["TeamViewer"] = "teamviwer.png",
            ["VC++"] = "vc++.png",
            ["VLC"] = "vlc.png",
            ["WinRAR"] = "Winrar.jpg",
            ["Zoom"] = "Zoom.png"
        };

        if (!fileMap.TryGetValue(folderName, out var fileName))
        {
            return string.Empty;
        }

        foreach (var dir in candidates)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private string? ResolveOfflineTargetExe(DirectoryInfo folder)
    {
        if (folder.Name.Equals("DirectX", StringComparison.OrdinalIgnoreCase))
        {
            var directxSetup = Path.Combine(folder.FullName, "DXSETUP.exe");
            if (File.Exists(directxSetup))
            {
                return directxSetup;
            }

            var directxNested = folder.EnumerateFiles("DXSETUP.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (directxNested is not null)
            {
                return directxNested.FullName;
            }
        }

        var exes = folder.EnumerateFiles("*.exe", SearchOption.AllDirectories).ToList();
        if (exes.Count == 0)
        {
            return null;
        }

        var folderName = folder.Name;
        var matchByName = exes.FirstOrDefault(file =>
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(file.Name);
            return nameNoExt.Equals(folderName, StringComparison.OrdinalIgnoreCase)
                   || nameNoExt.Contains(folderName, StringComparison.OrdinalIgnoreCase);
        });
        if (matchByName is not null)
        {
            return matchByName.FullName;
        }

        foreach (var priority in new[] { "setup", "install", "installer" })
        {
            var match = exes.FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file.Name).Contains(priority, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.FullName;
            }
        }

        return exes
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .First()
            .FullName;
    }

    private void LoadOfficeApps()
    {
        OfficeApps.Clear();

        var targetExe = ResolveOfficeTargetExe();
        if (!string.IsNullOrWhiteSpace(targetExe))
        {
            var item = new AddonToolItem
            {
                Name = "Microsoft Office",
                FullPath = targetExe,
                Category = "office"
            };
            item.LaunchCommand = new AsyncRelayCommand(() => InstallSingleItemAsync(item), () => !IsOfflineInstallRunning);
            OfficeApps.Add(item);
        }

        OfficePrimaryFileName = OfficeApps.Count > 0 ? OfficeApps[0].Name : "-";
    }

    private string? ResolveOfficeTargetExe()
    {
        if (!Directory.Exists(_officeFolderPath))
        {
            return null;
        }

        var rootSetup = Path.Combine(_officeFolderPath, "Setup.exe");
        if (File.Exists(rootSetup))
        {
            return rootSetup;
        }

        var nestedSetup = Path.Combine(_officeFolderPath, "Office", "Setup.exe");
        if (File.Exists(nestedSetup))
        {
            return nestedSetup;
        }

        List<FileInfo> exes;
        try
        {
            exes = Directory.EnumerateFiles(_officeFolderPath, "*.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .ToList();
        }
        catch (Exception)
        {
            return null;
        }

        if (exes.Count == 0)
        {
            return null;
        }

        foreach (var priority in new[] { "setup64", "setup32" })
        {
            var match = exes.FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file.Name).Equals(priority, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.FullName;
            }
        }

        return exes
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .First()
            .FullName;
    }

    private void OfflineItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddonToolItem.IsSelected))
        {
            UpdateOfflineSelectionSummary();
        }
    }

    private void UpdateOfflineSelectionSummary()
    {
        var selected = OfflineApps.Where(x => x.IsSelected).ToList();
        SelectedOfflineCount = selected.Count;
        SelectedOfflineItemsText = selected.Count == 0
            ? "-"
            : string.Join(", ", selected.Select(x => x.Name));
    }

    private async Task RunOnlinePrimaryAsync()
    {
        if (OnlineApps.Count == 0)
        {
            _feedback.ShowToast("File online tidak ditemukan.", false);
            return;
        }

        await InstallSingleItemAsync(OnlineApps[0]);
    }

    private async Task RunOfficePrimaryAsync()
    {
        if (OfficeApps.Count == 0)
        {
            _feedback.ShowToast("Installer Office tidak ditemukan.", false);
            return;
        }

        await InstallSingleItemAsync(OfficeApps[0]);
    }

    private async Task InstallSelectedOfflineAsync()
    {
        var selectedItems = OfflineApps.Where(x => x.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _feedback.ShowToast("Pilih aplikasi offline dulu.", false);
            return;
        }

        await RunOfflineBatchAsync(selectedItems, "Install Selected");
    }

    private async Task InstallAllOfflineAsync()
    {
        if (!HasOfflineApps)
        {
            _feedback.ShowToast("Aplikasi offline belum tersedia.", false);
            return;
        }

        await RunOfflineBatchAsync(OfflineApps.ToList(), "Install All");
    }

    private async Task RunOfflineBatchAsync(IReadOnlyList<AddonToolItem> items, string modeName)
    {
        if (IsOfflineInstallRunning)
        {
            return;
        }

        IsOfflineInstallRunning = true;
        OfflineInstallProgress = 0;
        OfflineInstallStatus = $"{modeName} dimulai...";
        OfflineCurrentItem = "-";
        OfflineNextItem = items.Count > 0 ? items[0].Name : "-";
        OfflineElapsedText = "00:00";
        OfflineCompletedItems.Clear();
        _installTimer.Restart();
        _uiTimer.Start();

        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                OfflineCurrentItem = item.Name;
                OfflineNextItem = i + 1 < items.Count ? items[i + 1].Name : "Selesai";
                OfflineInstallStatus = $"Installing {item.Name} ({i + 1}/{items.Count})";

                var result = await LaunchExecutableAsync(item.FullPath, waitForExit: true);
                if (!result.Success)
                {
                    OfflineInstallStatus = $"Gagal install {item.Name}";
                    _feedback.ShowToast(result.Message, false);
                    return;
                }

                await CreateShortcutsAfterInstallAsync(item);
                OfflineCompletedItems.Add(item.Name);
                OfflineInstallProgress = ((i + 1) * 100.0d) / items.Count;
            }

            OfflineInstallStatus = $"{modeName} selesai.";
            _feedback.ShowToast($"{modeName} selesai.", true);
        }
        finally
        {
            _installTimer.Stop();
            _uiTimer.Stop();
            IsOfflineInstallRunning = false;
        }
    }

    private async Task InstallSingleItemAsync(AddonToolItem item)
    {
        if (IsOfflineInstallRunning)
        {
            _feedback.ShowToast("Tunggu proses batch selesai.", false);
            return;
        }

        var result = await LaunchExecutableAsync(item.FullPath, waitForExit: true);
        if (!result.Success)
        {
            _feedback.ShowToast(result.Message, false);
            return;
        }

        await CreateShortcutsAfterInstallAsync(item);
        _feedback.ShowToast($"{item.Name} selesai dijalankan.", true);
    }

    private async Task<LaunchResult> LaunchExecutableAsync(string fullPath, bool waitForExit)
    {
        if (!File.Exists(fullPath))
        {
            return new LaunchResult(false, "File installer tidak ditemukan.");
        }

        try
        {
            _feedback.ShowLoading($"Launching {Path.GetFileName(fullPath)}...");

            var startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? _workspaceRoot,
                UseShellExecute = true,
                Verb = _isRunningAsAdmin ? string.Empty : "runas"
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new LaunchResult(false, "Installer gagal dijalankan.");
            }

            // Tutup overlay segera setelah proses berhasil start agar UI tidak terlihat stuck
            // saat installer UAC/admin berjalan.
            _feedback.HideLoading();

            if (waitForExit)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode is not (0 or 1641 or 3010))
                {
                    _logger.Error($"Installer exit code non-success: {fullPath} => {process.ExitCode}");
                    return new LaunchResult(false, "Installer gagal dijalankan.");
                }
            }

            return new LaunchResult(true, "OK");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Error($"UAC dibatalkan user: {fullPath}", ex);
            return new LaunchResult(false, "Proses dibatalkan.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Gagal launch file: {fullPath}", ex);
            return new LaunchResult(false, "Installer gagal dijalankan.");
        }
        finally
        {
            _feedback.HideLoading();
        }
    }

    private async Task CreateShortcutsAfterInstallAsync(AddonToolItem item)
    {
        await Task.Run(() =>
        {
            // Hanya buat shortcut desktop jika executable asli aplikasi ditemukan.
            // Jika tidak ketemu (mis. Microsoft Office hanya punya uninstaller di Control Panel),
            // jangan bikin shortcut Control Panel palsu di desktop.
            TryCreateShortcutByStartMenu(item.Name);
            TryCreateShortcutByExecutableHints(item.Name);
        });
    }

    private bool TryCreateShortcutByExecutableHints(string appName)
    {
        foreach (var pair in _appShortcutHints)
        {
            if (!appName.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var exe in pair.Value)
            {
                var foundPath = TryResolveExecutablePath(exe);
                if (string.IsNullOrWhiteSpace(foundPath))
                {
                    continue;
                }

                if (TryCreateDesktopShortcut(appName, foundPath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string? TryResolveExecutablePath(string exeName)
    {
        foreach (var path in EnumerateAppPathRegistryCandidates(exeName))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        foreach (var candidate in BuildCommonExecutableCandidates(exeName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateAppPathRegistryCandidates(string exeName)
    {
        var keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";
        var result = new List<string>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appKey = baseKey.OpenSubKey(keyPath);
                    var path = appKey?.GetValue(string.Empty)?.ToString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        result.Add(path.Trim('"'));
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        return result;
    }

    private IEnumerable<string> BuildCommonExecutableCandidates(string exeName)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            Path.Combine(programFiles, "Google", "Chrome", "Application", exeName),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", exeName),
            Path.Combine(programFiles, "Mozilla Firefox", exeName),
            Path.Combine(programFilesX86, "Mozilla Firefox", exeName),
            Path.Combine(programFiles, "VideoLAN", "VLC", exeName),
            Path.Combine(programFilesX86, "VideoLAN", "VLC", exeName),
            Path.Combine(programFiles, "WinRAR", exeName),
            Path.Combine(programFilesX86, "WinRAR", exeName),
            Path.Combine(localAppData, "Programs", "zoom", "bin", exeName)
        };
    }

    private bool TryCreateShortcutByStartMenu(string appName)
    {
        var normalizedAppName = NormalizeToken(appName);
        foreach (var link in EnumerateStartMenuShortcuts())
        {
            var fileName = NormalizeToken(Path.GetFileNameWithoutExtension(link));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            // Lewati shortcut uninstaller / repair / control panel walau namanya mirip,
            // agar tidak salah taruh shortcut Control Panel di desktop dengan nama aplikasi.
            if (LooksLikeUninstallerShortcut(fileName, link))
            {
                continue;
            }

            if (!fileName.Contains(normalizedAppName, StringComparison.OrdinalIgnoreCase) &&
                !normalizedAppName.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CopyShortcutToDesktop(appName, link))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeUninstallerShortcut(string displayName, string linkPath)
    {
        var name = displayName ?? string.Empty;
        if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Repair", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Change Only", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("(Installed Apps)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Cek target .lnk: kalau mengarah ke control.exe / appwiz.cpl, anggap uninstaller.
        var target = TryResolveShortcutTarget(linkPath);
        if (!string.IsNullOrWhiteSpace(target))
        {
            var lower = target.ToLowerInvariant();
            if (lower.EndsWith("\\control.exe", StringComparison.Ordinal) ||
                lower.Contains("\\system32\\control.exe", StringComparison.Ordinal) ||
                lower.Contains("\\syswow64\\control.exe", StringComparison.Ordinal) ||
                lower.Contains("appwiz.cpl", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryResolveShortcutTarget(string linkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            object? shell = null;
            object? shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell is null)
                {
                    return null;
                }

                shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    [linkPath]);
                if (shortcut is null)
                {
                    return null;
                }

                var value = shortcut.GetType().InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null)?.ToString();
                return value;
            }
            finally
            {
                if (shortcut is not null && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }

                if (shell is not null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateStartMenuShortcuts()
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var link in links)
            {
                yield return link;
            }
        }
    }

    private bool CopyShortcutToDesktop(string shortcutName, string sourceShortcutPath)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var targetShortcutPath = Path.Combine(desktopPath, $"{SanitizeFileName(shortcutName)}.lnk");
            File.Copy(sourceShortcutPath, targetShortcutPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Gagal copy shortcut start menu: {sourceShortcutPath}", ex);
            return false;
        }
    }

    private bool TryCreateDesktopShortcut(string shortcutName, string targetPath, string arguments = "", string? iconPath = null)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var linkPath = Path.Combine(desktopPath, $"{SanitizeFileName(shortcutName)}.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [linkPath]);
            if (shortcut is null)
            {
                return false;
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, [arguments]);
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(targetPath) ?? _workspaceRoot]);

            var iconValue = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : targetPath;
            shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, [iconValue]);
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Gagal membuat shortcut desktop: {shortcutName}", ex);
            return false;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private string ResolveCardImagePath(params string[] baseFileNames)
    {
        var names = baseFileNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in _tilesFolderCandidates.Where(Directory.Exists))
        {
            foreach (var name in names)
            {
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
                {
                    var path = Path.Combine(folder, $"{name}{ext}");
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }

        foreach (var folder in _tilesFolderCandidates.Where(Directory.Exists))
        {
            foreach (var ext in new[] { "*.jpg", "*.jpeg", "*.png" })
            {
                var firstMatch = Directory.EnumerateFiles(folder, ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstMatch))
                {
                    return firstMatch;
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Shortcut" : sanitized;
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
    }

    private readonly record struct LaunchResult(bool Success, string Message);
}

public partial class AddonToolItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string IconPath { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    public IAsyncRelayCommand? LaunchCommand { get; set; }

    public IRelayCommand? ToggleSelectedCommand { get; set; }
}

