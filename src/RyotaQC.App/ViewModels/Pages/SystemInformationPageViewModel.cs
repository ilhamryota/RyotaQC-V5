using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Models;
using RyotaQC.App.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace RyotaQC.App.ViewModels.Pages;

public sealed partial class SystemInformationPageViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfoService;
    private readonly IExternalToolLauncher _launcher;
    private readonly INavigationService _navigationService;
    private readonly DispatcherTimer _clockTimer;

    [ObservableProperty]
    private bool isBusy;

    // Header
    [ObservableProperty]
    private string clockText = System.DateTime.Now.ToString("HH:mm");

    // Overview
    [ObservableProperty] private string osName = "-";
    [ObservableProperty] private string osVersion = "-";
    [ObservableProperty] private string systemModel = "-";
    [ObservableProperty] private string serialNumber = "-";
    [ObservableProperty] private string userName = "-";

    // CPU
    [ObservableProperty] private string cpuName = "-";
    [ObservableProperty] private string cpuClock = string.Empty;

    // RAM
    [ObservableProperty] private string ramInstalled = "-";
    [ObservableProperty] private string ramTechnology = "-";
    [ObservableProperty] private string ramSpeed = "-";
    [ObservableProperty] private string ramTotalSlots = "-";
    [ObservableProperty] private string ramOnboard = "-";

    // Memory card
    [ObservableProperty] private string ramSlotsFree = "-";
    [ObservableProperty] private string ramSlotsUsed = "-";

    // Storage / Display (legacy summary strings)
    [ObservableProperty] private string storageDetail = "-";
    [ObservableProperty] private string displayDetail = "-";

    // Structured storage
    public ObservableCollection<StorageDriveInfo> StorageDrives { get; } = new();

    // Structured display
    public ObservableCollection<DisplayAdapterInfo> DisplayAdapters { get; } = new();

    [ObservableProperty] private string displayGpuSummary = "-";
    [ObservableProperty] private string monitorResolutionText = "-";
    [ObservableProperty] private string monitorTier = "-";
    [ObservableProperty] private string monitorRefreshRate = "-";
    [ObservableProperty] private string monitorBitsPerPixel = "-";
    [ObservableProperty] private bool hasDisplayAdapters;

    public SystemInformationPageViewModel(
        ISystemInfoService systemInfoService,
        IExternalToolLauncher launcher,
        INavigationService navigationService)
    {
        _systemInfoService = systemInfoService;
        _launcher = launcher;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
        OpenDiskManagementCommand = new RelayCommand(() => _launcher.LaunchById("disk_management"));
        OpenUserAccountsCommand = new RelayCommand(() => _launcher.LaunchById("user_accounts"));
        OpenSearchCommand = new RelayCommand(() => System.Windows.MessageBox.Show("Fitur pencarian belum tersedia.", "Search"));
        OpenSettingsCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Settings));

        _clockTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText = System.DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();

        _ = RefreshAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand BackToHomeCommand { get; }

    public IRelayCommand OpenDiskManagementCommand { get; }

    public IRelayCommand OpenUserAccountsCommand { get; }

    public IRelayCommand OpenSearchCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var data = await _systemInfoService.GetSystemInfoAsync();

            OsName = data.OsName;
            OsVersion = data.OsVersion;
            SystemModel = data.SystemModel;
            SerialNumber = data.BiosSerialNumber;
            UserName = data.UserName;

            CpuName = data.CpuName;
            CpuClock = data.CpuClock;

            RamInstalled = data.RamInstalled;
            RamTechnology = data.RamTechnology;
            RamSpeed = data.RamSpeed;
            RamTotalSlots = data.RamTotalSlots;
            RamSlotsFree = data.RamSlotsFree;
            RamSlotsUsed = data.RamSlotsUsed;
            RamOnboard = data.RamOnboard;

            StorageDetail = data.StorageDetail;
            DisplayDetail = data.DisplayDetail;

            // Structured storage
            StorageDrives.Clear();
            foreach (var drive in data.StorageDrives)
            {
                StorageDrives.Add(drive);
            }

            // Structured display
            DisplayAdapters.Clear();
            foreach (var adapter in data.DisplayAdapters)
            {
                DisplayAdapters.Add(adapter);
            }
            HasDisplayAdapters = DisplayAdapters.Count > 0;
            DisplayGpuSummary = string.IsNullOrWhiteSpace(data.DisplayGpuSummary) ? "-" : data.DisplayGpuSummary;

            var monitor = data.PrimaryMonitor;
            MonitorResolutionText = monitor?.ResolutionText ?? "-";
            MonitorTier = string.IsNullOrWhiteSpace(monitor?.Tier) ? "-" : monitor!.Tier;
            MonitorRefreshRate = monitor is { RefreshRate: > 0 } ? $"{monitor.RefreshRate} Hz" : "-";
            MonitorBitsPerPixel = monitor is { BitsPerPixel: > 0 } ? $"{monitor.BitsPerPixel} bit" : "-";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
