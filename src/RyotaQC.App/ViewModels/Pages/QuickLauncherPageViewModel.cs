using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Models;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public enum QuickStepStatus
{
    Idle,
    Running,
    Done,
    Failed,
    Skipped,
    InProgress
}

public partial class QuickStepItem : ObservableObject
{
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required string IconGlyph { get; init; }

    [ObservableProperty]
    private QuickStepStatus status = QuickStepStatus.Idle;

    [ObservableProperty]
    private string statusText = "Idle";

    public void SetStatus(QuickStepStatus status, string text)
    {
        Status = status;
        StatusText = text;
    }
}

public partial class QuickLogItem : ObservableObject
{
    public required string Time { get; init; }
    public required string Message { get; init; }
    public required string Level { get; init; }
}

public partial class QuickWallpaperItem : ObservableObject
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
}

public sealed partial class QuickLauncherPageViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IQuickProvisionService _provision;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "Siap. Tekan 1-9 untuk memilih opsi.";

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private bool identityLoaded;

    [ObservableProperty]
    private bool showWallpaperGrid;

    // Identity fields
    [ObservableProperty] private string osName = "-";
    [ObservableProperty] private string osVersion = "-";
    [ObservableProperty] private string manufacturer = "-";
    [ObservableProperty] private string systemModel = "-";
    [ObservableProperty] private string serialNumber = "-";
    [ObservableProperty] private string cpuName = "-";
    [ObservableProperty] private string ramInstalled = "-";
    [ObservableProperty] private string ramSlotsUsed = "-";
    [ObservableProperty] private string ramSlotsFree = "-";
    [ObservableProperty] private string ramSpeed = "-";
    [ObservableProperty] private string diskType = "-";
    [ObservableProperty] private string diskInterface = "-";
    [ObservableProperty] private string diskTotal = "-";
    [ObservableProperty] private string lcdInfo = "-";
    [ObservableProperty] private string vgaInfo = "-";
    [ObservableProperty] private string unitName = "-";
    [ObservableProperty] private string driverPlanText = "-";

    private QuickIdentityInfo? _identity;

    /// <summary>Request a Yes/No confirmation with countdown (auto-Yes). Raised to the view.</summary>
    public event Func<string, int, Task<bool>>? ConfirmRequested;

    /// <summary>Request Online/Offline choice with countdown (auto-Offline). Raised to the view.</summary>
    public event Func<int, Task<string>>? AppMethodRequested;

    /// <summary>Request a wallpaper selection from the grid. Completes when user picks one.</summary>
    public event Func<Task>? WallpaperSelectionRequested;

    /// <summary>Final Run All results ready to display.</summary>
    public event Action<List<QuickResultItem>>? RunAllCompleted;

    public QuickLauncherPageViewModel(
        INavigationService navigationService,
        IQuickProvisionService provision)
    {
        _navigationService = navigationService;
        _provision = provision;

        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));

        Steps = new ObservableCollection<QuickStepItem>
        {
            new() { Number = "1", Title = "Identifikasi Informasi", IconGlyph = "\uE9D9" },
            new() { Number = "2", Title = "Hubungkan Wi-Fi", IconGlyph = "\uE839" },
            new() { Number = "3", Title = "Ganti Nama User", IconGlyph = "\uE716" },
            new() { Number = "4", Title = "Partisi Disk D", IconGlyph = "\uEDA2" },
            new() { Number = "5", Title = "Install Driver", IconGlyph = "\uE950" },
            new() { Number = "6", Title = "Install Microsoft Office", IconGlyph = "\uE8A5" },
            new() { Number = "7", Title = "Install Aplikasi", IconGlyph = "\uE8F1" },
            new() { Number = "8", Title = "Wallpaper", IconGlyph = "\uE91B" },
            new() { Number = "9", Title = "JALANKAN SEMUA (1-8)", IconGlyph = "\uE768" }
        };

        _provision.LogRaised += OnProvisionLog;

        WallpaperCommand = new AsyncRelayCommand<QuickWallpaperItem?>(ApplyWallpaperAsync);
    }

    public ObservableCollection<QuickStepItem> Steps { get; }

    public ObservableCollection<QuickLogItem> LogLines { get; } = [];

    public ObservableCollection<QuickWallpaperItem> Wallpapers { get; } = [];

    public IRelayCommand BackToHomeCommand { get; }

    public IAsyncRelayCommand<QuickWallpaperItem?> WallpaperCommand { get; }

    private void OnProvisionLog(string message, string level)
    {
        AppendLog(message, level);
    }

    private void AppendLog(string message, string level)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LogLines.Add(new QuickLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Message = message,
                Level = level
            });

            while (LogLines.Count > 400)
            {
                LogLines.RemoveAt(0);
            }
        });
    }

    private void SetStep(int number, QuickStepStatus status, string text)
    {
        var step = Steps.FirstOrDefault(s => s.Number == number.ToString());
        step?.SetStatus(status, text);
    }

    // ------------------------------------------------------------ entry points

    public async Task ExecuteOptionAsync(int number)
    {
        if (IsBusy)
        {
            AppendLog("Proses lain sedang berjalan...", "WARN");
            return;
        }

        switch (number)
        {
            case 1: await RunIdentityAsync(); break;
            case 2: await RunWifiAsync(); break;
            case 3: await RunRenameAsync(); break;
            case 4: await RunPartitionAsync(); break;
            case 5: await RunDriverAsync(confirmForOtherBrand: false); break;
            case 6: RunOffice(); break;
            case 7: await RunAppsAsync(); break;
            case 8: await ShowWallpaperGridAsync(); break;
            case 9: await RunAllAsync(); break;
        }
    }

    // ------------------------------------------------------------------ steps

    private async Task RunIdentityAsync()
    {
        IsBusy = true;
        StatusText = "Mengidentifikasi unit...";
        SetStep(1, QuickStepStatus.Running, "Mengidentifikasi...");
        try
        {
            _identity = await _provision.GetIdentityAsync();
            ApplyIdentity(_identity);
            IdentityLoaded = true;
            SetStep(1, QuickStepStatus.Done, _identity.UnitName);
            StatusText = "Identifikasi selesai.";
            AppendLog($"Identifikasi selesai: {_identity.UnitName}", "SUCCESS");
        }
        catch (Exception ex)
        {
            SetStep(1, QuickStepStatus.Failed, "Gagal");
            StatusText = "Identifikasi gagal.";
            AppendLog($"Identifikasi gagal: {ex.Message}", "ERROR");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyIdentity(QuickIdentityInfo info)
    {
        OsName = info.OsName;
        OsVersion = info.OsVersion;
        Manufacturer = info.Manufacturer;
        SystemModel = info.SystemModel;
        SerialNumber = info.SerialNumber;
        CpuName = info.CpuName;
        RamInstalled = info.RamInstalled;
        RamSlotsUsed = info.RamSlotsUsed;
        RamSlotsFree = info.RamSlotsFree;
        RamSpeed = info.RamSpeed;
        DiskType = info.DiskType;
        DiskInterface = info.DiskInterface;
        DiskTotal = info.DiskTotal;
        LcdInfo = info.LcdInfo;
        VgaInfo = info.VgaInfo;
        UnitName = info.UnitName;

        var plan = _provision.GetDriverPlan();
        DriverPlanText = plan.Description;
    }

    private async Task RunWifiAsync()
    {
        IsBusy = true;
        StatusText = "Menghubungkan Wi-Fi...";
        SetStep(2, QuickStepStatus.Running, "Menghubungkan...");
        var ok = await _provision.ConnectWifiAsync();
        SetStep(2, ok ? QuickStepStatus.Done : QuickStepStatus.Failed, ok ? "Terhubung" : "Gagal");
        StatusText = ok ? "Wi-Fi terhubung." : "Wi-Fi gagal.";
        IsBusy = false;
    }

    private async Task RunRenameAsync()
    {
        IsBusy = true;
        StatusText = "Mengganti nama akun...";
        if (_identity is null)
        {
            AppendLog("Identifikasi dulu (opsi 1)...", "WARN");
            _identity = await _provision.GetIdentityAsync();
            ApplyIdentity(_identity);
            IdentityLoaded = true;
        }

        SetStep(3, QuickStepStatus.Running, "Mengganti nama...");
        var ok = await _provision.RenameAccountAsync(_identity.UnitName);
        SetStep(3, ok ? QuickStepStatus.Done : QuickStepStatus.Failed, ok ? _identity.UnitName : "Gagal");
        StatusText = ok ? $"Nama akun: {_identity.UnitName}" : "Rename gagal.";
        IsBusy = false;
    }

    private async Task RunPartitionAsync()
    {
        IsBusy = true;
        StatusText = "Membuat partisi Data...";
        SetStep(4, QuickStepStatus.Running, "Mempartisi...");
        var (ok, message) = await _provision.PartitionDiskAsync();
        var skipped = message.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase);
        SetStep(4, skipped ? QuickStepStatus.Skipped : ok ? QuickStepStatus.Done : QuickStepStatus.Failed,
            skipped ? "Dilewati" : ok ? "Selesai" : "Gagal");
        StatusText = message;
        IsBusy = false;
    }

    private async Task RunDriverAsync(bool confirmForOtherBrand)
    {
        IsBusy = true;
        var plan = _provision.GetDriverPlan();
        SetStep(5, QuickStepStatus.Running, plan.Brand);

        if (plan.UsesLocalInstaller)
        {
            StatusText = $"Install driver {plan.Brand}...";
            var (ok, message) = await _provision.InstallVendorDriverAsync();
            SetStep(5, ok ? QuickStepStatus.Done : QuickStepStatus.Failed, ok ? plan.Brand : "Gagal");
            StatusText = message;
        }
        else
        {
            bool proceed = true;
            if (confirmForOtherBrand && ConfirmRequested is not null)
            {
                proceed = await ConfirmRequested(
                    $"Brand {plan.Brand} terdeteksi.\nLanjutkan update driver via Windows Update (driver-only)?",
                    15);
            }

            if (proceed)
            {
                StatusText = "Windows Update driver-only berjalan di background...";
                _provision.StartWindowsUpdateDriverOnly();
                SetStep(5, QuickStepStatus.InProgress, "WU berjalan");
            }
            else
            {
                SetStep(5, QuickStepStatus.Skipped, "Dilewati");
                StatusText = "Driver dilewati.";
            }
        }

        IsBusy = false;
    }

    private void RunOffice()
    {
        SetStep(6, QuickStepStatus.Running, "Meluncurkan...");
        _provision.LaunchOfficeSetup();
        SetStep(6, QuickStepStatus.InProgress, "Installer berjalan");
        StatusText = "Installer Office berjalan di background.";
    }

    private async Task RunAppsAsync()
    {
        string method;
        if (AppMethodRequested is not null)
        {
            StatusText = "Menunggu pilihan Online/Offline...";
            method = await AppMethodRequested(15);
        }
        else
        {
            method = "Offline";
        }

        if (method == "Cancel")
        {
            SetStep(7, QuickStepStatus.Skipped, "Dibatalkan");
            StatusText = "Install aplikasi dibatalkan.";
            return;
        }

        if (method == "Online")
        {
            SetStep(7, QuickStepStatus.Running, "Online...");
            StatusText = "AppPack online berjalan di background...";
            _provision.LaunchOnlineApps();
            SetStep(7, QuickStepStatus.InProgress, "AppPack berjalan");
            return;
        }

        IsBusy = true;
        SetStep(7, QuickStepStatus.Running, "Offline...");
        StatusText = "Menginstal aplikasi offline...";
        var results = await _provision.InstallOfflineAppsAsync();
        var ok = results.Count(r => r.Success);
        var fail = results.Count(r => !r.Success);
        SetStep(7, fail == 0 ? QuickStepStatus.Done : QuickStepStatus.Failed, $"{ok}/{results.Count} OK");
        StatusText = $"Aplikasi selesai: {ok} berhasil, {fail} gagal.";
        IsBusy = false;
    }

    private async Task ShowWallpaperGridAsync()
    {
        await LoadWallpapersAsync();
        ShowWallpaperGrid = true;
        StatusText = "Pilih wallpaper untuk dipasang...";
        SetStep(8, QuickStepStatus.Running, "Memilih...");
    }

    public async Task LoadWallpapersAsync()
    {
        var files = await _provision.GetWallpaperFilesAsync();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Wallpapers.Clear();
            foreach (var file in files)
            {
                Wallpapers.Add(new QuickWallpaperItem { FilePath = file, FileName = System.IO.Path.GetFileName(file) });
            }
        });
    }

    private async Task ApplyWallpaperAsync(QuickWallpaperItem? item)
    {
        if (item is null)
        {
            return;
        }

        var ok = await Task.Run(() => _provision.SetWallpaper(item.FilePath));
        SetStep(8, ok ? QuickStepStatus.Done : QuickStepStatus.Failed, ok ? item.FileName : "Gagal");
        StatusText = ok ? $"Wallpaper: {item.FileName}" : "Wallpaper gagal.";
        ShowWallpaperGrid = false;
        _wallpaperPicked?.TrySetResult(item.FileName);
    }

    private TaskCompletionSource<string>? _wallpaperPicked;

    // --------------------------------------------------------------- run all

    private async Task RunAllAsync()
    {
        IsBusy = true;
        ProgressPercent = 0;
        foreach (var step in Steps)
        {
            step.SetStatus(QuickStepStatus.Idle, "Idle");
        }

        var results = new List<QuickResultItem>();
        AppendLog("=== RUN ALL DIMULAI ===", "INFO");

        // 1. Identity
        StatusText = "Langkah 1/8: Identifikasi...";
        await RunIdentityForAll(results);
        ProgressPercent = 12;

        // 2. Wi-Fi
        StatusText = "Langkah 2/8: Wi-Fi...";
        SetStep(2, QuickStepStatus.Running, "Menghubungkan...");
        var wifiOk = await _provision.ConnectWifiAsync();
        SetStep(2, wifiOk ? QuickStepStatus.Done : QuickStepStatus.Failed, wifiOk ? "Terhubung" : "Gagal");
        results.Add(new QuickResultItem
        {
            Category = wifiOk ? "Berhasil" : "Gagal",
            Name = "Wi-Fi",
            Detail = wifiOk ? "Terhubung ke profil config" : "Semua profil gagal / tidak ada internet"
        });
        ProgressPercent = 25;

        // 3. Rename
        StatusText = "Langkah 3/8: Ganti nama akun...";
        var identity = _identity ?? await _provision.GetIdentityAsync();
        _identity = identity;
        ApplyIdentity(identity);
        IdentityLoaded = true;
        SetStep(3, QuickStepStatus.Running, "Mengganti nama...");
        var renameOk = await _provision.RenameAccountAsync(identity.UnitName);
        SetStep(3, renameOk ? QuickStepStatus.Done : QuickStepStatus.Failed, renameOk ? identity.UnitName : "Gagal");
        results.Add(new QuickResultItem
        {
            Category = renameOk ? "Berhasil" : "Gagal",
            Name = "Ganti Nama Akun",
            Detail = renameOk ? $"Menjadi '{identity.UnitName}'" : "Gagal mengubah FullName"
        });
        ProgressPercent = 37;

        // 4. Partition
        StatusText = "Langkah 4/8: Partisi disk...";
        SetStep(4, QuickStepStatus.Running, "Mempartisi...");
        var (partOk, partMsg) = await _provision.PartitionDiskAsync();
        var partSkipped = partMsg.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase);
        SetStep(4, partSkipped ? QuickStepStatus.Skipped : partOk ? QuickStepStatus.Done : QuickStepStatus.Failed,
            partSkipped ? "Dilewati" : partOk ? "Selesai" : "Gagal");
        results.Add(new QuickResultItem
        {
            Category = partSkipped ? "Dilewati" : partOk ? "Berhasil" : "Gagal",
            Name = "Partisi Disk",
            Detail = partMsg
        });
        ProgressPercent = 50;

        // 5. Driver
        StatusText = "Langkah 5/8: Driver...";
        var plan = _provision.GetDriverPlan();
        if (plan.UsesLocalInstaller)
        {
            SetStep(5, QuickStepStatus.Running, plan.Brand);
            var (drvOk, drvMsg) = await _provision.InstallVendorDriverAsync();
            SetStep(5, drvOk ? QuickStepStatus.Done : QuickStepStatus.Failed, drvOk ? plan.Brand : "Gagal");
            results.Add(new QuickResultItem
            {
                Category = drvOk ? "Berhasil" : "Gagal",
                Name = $"Driver {plan.Brand}",
                Detail = drvMsg
            });
        }
        else
        {
            SetStep(5, QuickStepStatus.Running, "Konfirmasi...");
            var proceed = ConfirmRequested is not null
                ? await ConfirmRequested(
                    $"Brand {plan.Brand} terdeteksi.\nLanjutkan update driver via Windows Update (driver-only)?",
                    15)
                : true;

            if (proceed)
            {
                _provision.StartWindowsUpdateDriverOnly();
                SetStep(5, QuickStepStatus.InProgress, "WU berjalan");
                results.Add(new QuickResultItem
                {
                    Category = "Sedang Berjalan",
                    Name = "Windows Update (Driver)",
                    Detail = "Driver-only, berjalan di background"
                });
            }
            else
            {
                SetStep(5, QuickStepStatus.Skipped, "Dilewati");
                results.Add(new QuickResultItem
                {
                    Category = "Dilewati",
                    Name = "Driver",
                    Detail = "User memilih tidak lanjut"
                });
            }
        }
        ProgressPercent = 62;

        // 6. Office (fire & forget)
        StatusText = "Langkah 6/8: Office...";
        SetStep(6, QuickStepStatus.Running, "Meluncurkan...");
        _provision.LaunchOfficeSetup();
        SetStep(6, QuickStepStatus.InProgress, "Installer berjalan");
        results.Add(new QuickResultItem
        {
            Category = "Sedang Berjalan",
            Name = "Microsoft Office",
            Detail = "Installer berjalan di background"
        });
        ProgressPercent = 75;

        // 7. Apps
        StatusText = "Langkah 7/8: Aplikasi...";
        string method = AppMethodRequested is not null ? await AppMethodRequested(15) : "Offline";
        if (method == "Cancel")
        {
            SetStep(7, QuickStepStatus.Skipped, "Dibatalkan");
            results.Add(new QuickResultItem { Category = "Dilewati", Name = "Aplikasi", Detail = "Dibatalkan user" });
        }
        else if (method == "Online")
        {
            SetStep(7, QuickStepStatus.Running, "Online...");
            _provision.LaunchOnlineApps();
            SetStep(7, QuickStepStatus.InProgress, "AppPack berjalan");
            results.Add(new QuickResultItem
            {
                Category = "Sedang Berjalan",
                Name = "Aplikasi (Online)",
                Detail = "AppPack berjalan di background"
            });
        }
        else
        {
            SetStep(7, QuickStepStatus.Running, "Offline...");
            var appResults = await _provision.InstallOfflineAppsAsync();
            foreach (var app in appResults)
            {
                results.Add(new QuickResultItem
                {
                    Category = app.Success ? "Berhasil" : "Gagal",
                    Name = app.Name,
                    Detail = app.Detail
                });
            }

            var okCount = appResults.Count(r => r.Success);
            SetStep(7, okCount == appResults.Count && appResults.Count > 0
                ? QuickStepStatus.Done
                : okCount > 0 ? QuickStepStatus.Failed : QuickStepStatus.Failed,
                $"{okCount}/{appResults.Count} OK");
        }
        ProgressPercent = 87;

        // 8. Wallpaper (wait for user selection)
        StatusText = "Langkah 8/8: Wallpaper...";
        await LoadWallpapersAsync();
        ShowWallpaperGrid = true;
        SetStep(8, QuickStepStatus.Running, "Memilih...");
        _wallpaperPicked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (WallpaperSelectionRequested is not null)
        {
            await WallpaperSelectionRequested();
        }

        var pickedName = await _wallpaperPicked.Task;
        results.Add(new QuickResultItem
        {
            Category = "Berhasil",
            Name = "Wallpaper",
            Detail = pickedName
        });
        ProgressPercent = 100;

        SetStep(9, QuickStepStatus.Done, "Selesai");
        StatusText = "Run All selesai.";
        AppendLog("=== RUN ALL SELESAI ===", "SUCCESS");
        IsBusy = false;

        RunAllCompleted?.Invoke(results);
    }

    private async Task RunIdentityForAll(List<QuickResultItem> results)
    {
        SetStep(1, QuickStepStatus.Running, "Mengidentifikasi...");
        try
        {
            _identity = await _provision.GetIdentityAsync();
            ApplyIdentity(_identity);
            IdentityLoaded = true;
            SetStep(1, QuickStepStatus.Done, _identity.UnitName);
            results.Add(new QuickResultItem
            {
                Category = "Berhasil",
                Name = "Identifikasi Unit",
                Detail = $"{_identity.Manufacturer} {_identity.UnitName}"
            });
        }
        catch (Exception ex)
        {
            SetStep(1, QuickStepStatus.Failed, "Gagal");
            results.Add(new QuickResultItem { Category = "Gagal", Name = "Identifikasi Unit", Detail = ex.Message });
        }
    }
}
