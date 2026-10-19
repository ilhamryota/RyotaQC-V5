using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Models;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public partial class DriverPackPageViewModel : ObservableObject
{
    private readonly IToolConfigService _toolConfigService;
    private readonly IExternalToolLauncher _launcher;
    private readonly IUiFeedbackService _feedback;
    private readonly IAppLogger _logger;
    private readonly INavigationService _navigationService;
    private readonly string _driverInstallStampPath;

    private readonly string[] _dellInstallOrder =
    {
        "dell_netsdk",
        "dell_netruntime",
        "dell_aspnetcore",
        "dell_wdruntime",
        "dell_dell"
    };

    [ObservableProperty]
    private bool isDellInstallRunning;

    [ObservableProperty]
    private double dellInstallProgress;

    [ObservableProperty]
    private string dellInstallStatus = "Idle";

    [ObservableProperty]
    private string dellCurrentItem = "-";

    [ObservableProperty]
    private string dellNextItem = "-";

    public DriverPackPageViewModel(
        IToolConfigService toolConfigService,
        IExternalToolLauncher launcher,
        IUiFeedbackService feedback,
        IAppLogger logger,
        INavigationService navigationService)
    {
        _toolConfigService = toolConfigService;
        _launcher = launcher;
        _feedback = feedback;
        _logger = logger;
        _navigationService = navigationService;
        var workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        _driverInstallStampPath = Path.Combine(workspaceRoot, "logs", "driver-pack-install.txt");

        Vendors = new ObservableCollection<DriverVendorItem>();
        DellCompletedItems = new ObservableCollection<string>();

        RefreshCommand = new RelayCommand(LoadItems);
        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
        InstallAllDellCommand = new AsyncRelayCommand(InstallAllDellAsync, () => !IsDellInstallRunning);
        ToggleVendorCommand = new RelayCommand<DriverVendorItem?>((vendor) =>
        {
            if (vendor is null)
            {
                return;
            }

            if (!ReferenceEquals(ActiveVendor, vendor))
            {
                ActiveVendor = vendor;
            }
        });

        LoadItems();
    }

    [ObservableProperty]
    private DriverVendorItem? activeVendor;

    public IRelayCommand<DriverVendorItem?> ToggleVendorCommand { get; }

    public ObservableCollection<DriverVendorItem> Vendors { get; }

    public ObservableCollection<string> DellCompletedItems { get; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand BackToHomeCommand { get; }

    public IAsyncRelayCommand InstallAllDellCommand { get; }

    private void LoadItems()
    {
        Vendors.Clear();

        Vendors.Add(BuildWindowsUpdateVendor());
        Vendors.Add(BuildLenovoVendor());
        Vendors.Add(BuildDellVendor());
        Vendors.Add(BuildHpVendor());

        ActiveVendor = Vendors.FirstOrDefault();
    }

    private DriverVendorItem BuildHpVendor()
    {
        var vendor = new DriverVendorItem
        {
            VendorName = "HP",
            VendorSubtitle = "Khusus HP / Windows Update driver-only",
            IsExpanded = false,
            ShowInstallAll = false,
            Tools = new ObservableCollection<DriverPackToolItem>()
        };

        var hpTool = BuildTool("driver_hp", "HP");
        if (hpTool is not null)
        {
            vendor.Tools.Add(hpTool);
        }

        vendor.Tools.Add(new DriverPackToolItem
        {
            Name = "HP Brightness Fix",
            LaunchCommand = new AsyncRelayCommand(HpBrightnessFixAsync)
        });

        return vendor;
    }

    private DriverVendorItem BuildLenovoVendor()
    {
        var vendor = new DriverVendorItem
        {
            VendorName = "Lenovo",
            VendorSubtitle = "System Update via Lenovo",
            IsExpanded = false,
            ShowInstallAll = false,
            Tools = new ObservableCollection<DriverPackToolItem>()
        };

        var lenovoTool = BuildTool("driver_lenovo", "Lenovo Update");
        if (lenovoTool is not null)
        {
            vendor.Tools.Add(lenovoTool);
        }

        return vendor;
    }

    private DriverVendorItem BuildWindowsUpdateVendor()
    {
        return new DriverVendorItem
        {
            VendorName = "Windows Update",
            VendorSubtitle = "Driver Only (tanpa security patch)",
            IsExpanded = true,
            ShowInstallAll = false,
            Tools = new ObservableCollection<DriverPackToolItem>
            {
                new() { Name = "Update Windows", LaunchCommand = new AsyncRelayCommand(UpdateWindowsAsync) },
                new() { Name = "Disable Windows", LaunchCommand = new AsyncRelayCommand(DisableWindowsUpdateAsync) },
                new() { Name = "Enable Windows", LaunchCommand = new AsyncRelayCommand(EnableWindowsUpdateAsync) },
                new() { Name = "Optional Update", LaunchCommand = new AsyncRelayCommand(OptionalUpdateAsync) },
                new() { Name = "Retry Download", LaunchCommand = new AsyncRelayCommand(RetryDownloadAsync) }
            }
        };
    }

    private DriverVendorItem BuildDellVendor()
    {
        var vendor = new DriverVendorItem
        {
            VendorName = "Dell",
            VendorSubtitle = "Command Update via tools\\dell",
            IsExpanded = true,
            ShowInstallAll = true,
            Tools = new ObservableCollection<DriverPackToolItem>()
        };

        var ordered = new[]
        {
            ("dell_netsdk", "NetSDK"),
            ("dell_netruntime", "NetRuntime"),
            ("dell_aspnetcore", "ASPnetCore"),
            ("dell_wdruntime", "WD Runtime"),
            ("dell_dell", "Dell")
        };

        foreach (var (id, displayName) in ordered)
        {
            var tool = BuildTool(id, displayName);
            if (tool is not null)
            {
                vendor.Tools.Add(tool);
            }
        }

        return vendor;
    }

    private DriverPackToolItem? BuildTool(string id, string displayName)
    {
        var tool = _toolConfigService.GetToolById(id);
        if (tool is null)
        {
            return null;
        }

        return new DriverPackToolItem
        {
            Name = displayName,
            LaunchCommand = new RelayCommand(() => LaunchVendorTool(tool.Id))
        };
    }

    private void LaunchVendorTool(string toolId)
    {
        var result = _launcher.LaunchById(toolId);
        if (result.Success)
        {
            WriteDriverInstallStamp();
        }
    }

    private void WriteDriverInstallStamp()
    {
        try
        {
            var directory = Path.GetDirectoryName(_driverInstallStampPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_driverInstallStampPath, DateTime.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.Error("Gagal menyimpan timestamp install driver.", ex);
        }
    }

    private async Task InstallAllDellAsync()
    {
        if (IsDellInstallRunning)
        {
            return;
        }

        var tools = _dellInstallOrder
            .Select(id => _toolConfigService.GetToolById(id))
            .Where(tool => tool is not null)
            .Cast<ToolConfig>()
            .ToList();

        if (tools.Count == 0)
        {
            _feedback.ShowToast("Dell tools tidak ditemukan di config.", false);
            return;
        }

        IsDellInstallRunning = true;
        InstallAllDellCommand.NotifyCanExecuteChanged();
        DellInstallProgress = 0;
        DellInstallStatus = "Memulai install Dell Driver Pack...";
        DellCurrentItem = "-";
        DellNextItem = tools.Count > 0 ? tools[0].Name : "-";
        DellCompletedItems.Clear();

        try
        {
            for (var i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                DellCurrentItem = tool.Name;
                DellNextItem = i + 1 < tools.Count ? tools[i + 1].Name : "Selesai";
                DellInstallStatus = $"Installing {tool.Name} ({i + 1}/{tools.Count})";

                var success = await LaunchAndWaitAsync(tool);
                if (!success)
                {
                    DellInstallStatus = $"Gagal pada {tool.Name}. Proses dihentikan.";
                    _feedback.ShowToast(DellInstallStatus, false);
                    return;
                }

                DellCompletedItems.Add(tool.Name);
                DellInstallProgress = ((i + 1) * 100.0) / tools.Count;
                WriteDriverInstallStamp();
            }

            DellInstallStatus = "Semua installer Dell selesai.";
            _feedback.ShowToast("Install all Dell selesai.", true);
        }
        finally
        {
            IsDellInstallRunning = false;
            InstallAllDellCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task<bool> LaunchAndWaitAsync(ToolConfig tool)
    {
        try
        {
            var resolvedPath = _toolConfigService.ResolvePath(tool.RelativePath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || (!File.Exists(resolvedPath) && !IsCommandStyle(resolvedPath)))
            {
                _feedback.ShowToast($"File tidak ditemukan: {tool.Name}", false);
                return false;
            }

            var startInfo = BuildStartInfo(tool, resolvedPath);
            if (startInfo is null)
            {
                _feedback.ShowToast($"Type launcher tidak didukung: {tool.Type}", false);
                return false;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode is not (0 or 1641 or 3010))
            {
                _logger.Error($"Installer gagal: {tool.Id} => exit code {process.ExitCode}");
                _feedback.ShowToast($"Installer gagal: {tool.Name} (exit code {process.ExitCode}).", false);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Gagal run installer: {tool.Id}", ex);
            return false;
        }
    }

    private ProcessStartInfo? BuildStartInfo(ToolConfig tool, string resolvedPath)
    {
        var type = tool.Type.Trim().ToLowerInvariant();
        var args = tool.Arguments?.Trim() ?? string.Empty;

        var workingDir = !string.IsNullOrWhiteSpace(tool.WorkingDirectory)
            ? _toolConfigService.ResolvePath(tool.WorkingDirectory)
            : (File.Exists(resolvedPath) ? Path.GetDirectoryName(resolvedPath) ?? AppContext.BaseDirectory : AppContext.BaseDirectory);

        return type switch
        {
            "exe" => new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = workingDir,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            "bat" => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{resolvedPath}\" {args}".Trim(),
                UseShellExecute = true,
                WorkingDirectory = workingDir,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            "cmd" => new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = workingDir,
                Verb = tool.RunAsAdmin ? "runas" : string.Empty
            },
            _ => null
        };
    }

    private static bool IsCommandStyle(string path)
    {
        return !path.Contains(Path.DirectorySeparatorChar) && !path.Contains(Path.AltDirectorySeparatorChar);
    }

    private async Task UpdateWindowsAsync()
    {
        // Driver-only Windows Update (no security/quality patches).
        const string script = "$ErrorActionPreference='Continue'; " +
                              "$session = New-Object -ComObject Microsoft.Update.Session; " +
                              "$searcher = $session.CreateUpdateSearcher(); " +
                              "$result = $searcher.Search(\"IsInstalled=0 and Type='Driver'\"); " +
                              "Write-Output ('DRIVER_UPdates_FOUND: ' + $result.Updates.Count); " +
                              "if ($result.Updates.Count -eq 0) { Write-Output 'NO_DRIVER_UPDATES'; exit 0 }; " +
                              "$collection = New-Object -ComObject Microsoft.Update.UpdateColl; " +
                              "foreach ($update in $result.Updates) { [void]$collection.Add($update) }; " +
                              "$downloader = $session.CreateUpdateDownloader(); " +
                              "$downloader.Updates = $collection; " +
                              "$downloader.Download(); " +
                              "$installer = $session.CreateUpdateInstaller(); " +
                              "$installer.Updates = $collection; " +
                              "$installResult = $installer.Install(); " +
                              "Write-Output ('RESULT_CODE: ' + $installResult.ResultCode)";

        await RunPowerShellAsync(script, runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("Windows Update (Driver Only) dijalankan.", true);
    }

    private async Task DisableWindowsUpdateAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Disable Windows Update akan menonaktifkan update otomatis lalu MEMULAI ULANG PC dalam 30 detik.\n\nSimpan dulu semua pekerjaan Anda sebelum melanjutkan. Lanjutkan?",
            "RyotaQC V - Konfirmasi Restart",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        const string script = "$ErrorActionPreference='SilentlyContinue'; " +
                              "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Force | Out-Null; " +
                              "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Force | Out-Null; " +
                              "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name NoAutoUpdate -Type DWord -Value 1; " +
                              "Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name AUOptions -ErrorAction SilentlyContinue; " +
                              "$services = 'wuauserv','bits','dosvc','UsoSvc','WaaSMedicSvc'; " +
                              "foreach ($svc in $services) { try { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue } catch { }; try { Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue } catch { } }; " +
                              "gpupdate /target:computer /force | Out-Null; " +
                              "shutdown.exe /r /t 30 /c \"RyotaQC V: Windows Update dinonaktifkan permanen. Restart dalam 30 detik.\" /f";

        await RunPowerShellAsync(script, runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("Disable Windows diterapkan. PC restart dalam 30 detik.", true);
    }

    private async Task OptionalUpdateAsync()
    {
        await RunCommandAsync("cmd.exe", "/c start ms-settings:windowsupdate-optionalupdates", runAsAdmin: false, waitForExit: false);
        await RunCommandAsync("cmd.exe", "/c UsoClient StartScan", runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("Optional Update dibuka.", true);
    }

    private async Task EnableWindowsUpdateAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Enable Windows Update akan mengaktifkan kembali policy dan service update. Lanjutkan?",
            "RyotaQC V",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        const string script = "$ErrorActionPreference='SilentlyContinue'; " +
                              "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Force | Out-Null; " +
                              "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Force | Out-Null; " +
                              "Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name NoAutoUpdate -ErrorAction SilentlyContinue; " +
                              "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name AUOptions -Type DWord -Value 4; " +
                              "Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Name DisableWindowsUpdateAccess -ErrorAction SilentlyContinue; " +
                              "Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate' -Name SetDisableUXWUAccess -ErrorAction SilentlyContinue; " +
                              "$services = 'wuauserv','bits','dosvc','UsoSvc','WaaSMedicSvc'; " +
                              "foreach ($svc in $services) { try { Set-Service -Name $svc -StartupType Automatic -ErrorAction SilentlyContinue } catch { } }; " +
                              "foreach ($svc in $services) { try { Start-Service -Name $svc -ErrorAction SilentlyContinue } catch { } }; " +
                              "gpupdate /target:computer /force | Out-Null; " +
                              "Start-Process 'ms-settings:windowsupdate'; " +
                              "Start-Process UsoClient -ArgumentList StartScan";

        await RunPowerShellAsync(script, runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("Enable Windows diterapkan. Windows Update diaktifkan kembali.", true);
    }

    private async Task RetryDownloadAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Retry Download akan reset komponen Windows Update, restart service, lalu MEMULAI ULANG PC dalam 30 detik.\n\nSimpan dulu semua pekerjaan Anda sebelum melanjutkan. Lanjutkan?",
            "RyotaQC V - Konfirmasi Restart",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        const string script = "$ErrorActionPreference='SilentlyContinue'; " +
                              "$stopServices = 'bits','wuauserv','cryptsvc','msiserver','dosvc','UsoSvc'; " +
                              "foreach ($svc in $stopServices) { try { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue } catch { } }; " +
                              "if (Test-Path \"$env:windir\\SoftwareDistribution\") { Rename-Item \"$env:windir\\SoftwareDistribution\" (\"SoftwareDistribution.bak_\" + (Get-Date -Format yyyyMMddHHmmss)) -ErrorAction SilentlyContinue }; " +
                              "if (Test-Path \"$env:windir\\System32\\catroot2\") { Rename-Item \"$env:windir\\System32\\catroot2\" (\"catroot2.bak_\" + (Get-Date -Format yyyyMMddHHmmss)) -ErrorAction SilentlyContinue }; " +
                              "$startServices = 'cryptsvc','bits','wuauserv','dosvc','UsoSvc'; " +
                              "foreach ($svc in $startServices) { try { Set-Service -Name $svc -StartupType Automatic -ErrorAction SilentlyContinue } catch { }; try { Start-Service -Name $svc -ErrorAction SilentlyContinue } catch { } }; " +
                              "Start-Process 'ms-settings:windowsupdate'; " +
                              "Start-Process UsoClient -ArgumentList StartScan; " +
                              "Start-Process UsoClient -ArgumentList StartDownload; " +
                              "Start-Process UsoClient -ArgumentList StartInstall; " +
                              "shutdown.exe /r /t 30 /c \"RyotaQC V: Retry Download Windows Update dijalankan. Restart dalam 30 detik.\" /f";

        await RunPowerShellAsync(script, runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("Retry Download dijalankan. PC restart dalam 30 detik.", true);
    }

    private async Task HpBrightnessFixAsync()
    {
        const string script = "$svc = Get-Service | Where-Object { $_.DisplayName -like '*HP Hotkey UWP Service*' -or $_.Name -like '*HP*Hotkey*UWP*' } | Select-Object -First 1; " +
                              "if ($svc) { if ($svc.Status -ne 'Stopped') { Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue }; Set-Service -Name $svc.Name -StartupType Disabled -ErrorAction SilentlyContinue }; " +
                              "Start-Process services.msc";

        await RunPowerShellAsync(script, runAsAdmin: true, waitForExit: false);
        _feedback.ShowToast("HP Brightness Fix dijalankan. Cek Services.", true);
    }

    private async Task RunPowerShellAsync(string script, bool runAsAdmin, bool waitForExit)
    {
        var escapedScript = script.Replace("\"", "`\"");
        await RunCommandAsync(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{escapedScript}\"",
            runAsAdmin,
            waitForExit);
    }

    private async Task RunCommandAsync(string fileName, string arguments, bool runAsAdmin, bool waitForExit)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runas" : string.Empty
            };

            using var process = Process.Start(startInfo);
            if (waitForExit && process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Error($"Run command dibatalkan user: {fileName}", ex);
            _feedback.ShowToast("Proses dibatalkan.", false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Run command gagal: {fileName} {arguments}", ex);
            _feedback.ShowToast("Command gagal dijalankan.", false);
        }
    }
}

public sealed partial class DriverVendorItem : ObservableObject
{
    public string VendorName { get; init; } = string.Empty;

    public string VendorSubtitle { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isExpanded;

    public bool ShowInstallAll { get; init; }

    public ObservableCollection<DriverPackToolItem> Tools { get; init; } = [];
}

public sealed class DriverPackToolItem
{
    public string Name { get; init; } = string.Empty;

    public IRelayCommand LaunchCommand { get; init; } = null!;
}
