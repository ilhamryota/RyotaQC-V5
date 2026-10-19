using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IUiFeedbackService _feedbackService;
    private readonly IAppSettingsService _settingsService;
    private readonly ISystemInfoService _systemInfoService;

    private int _toastVersion;

    [ObservableProperty]
    private string appTitle = "RyotaQC V5";

    [ObservableProperty]
    private string subtitleText = "Quality Control Utility for Windows";

    [ObservableProperty]
    private string currentUserName = "ilhamryota";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string loadingMessage = "Launching...";

    [ObservableProperty]
    private bool isToastVisible;

    [ObservableProperty]
    private string toastMessage = string.Empty;

    [ObservableProperty]
    private bool toastSuccess;

    [ObservableProperty]
    private string dashboardSystemModel = "Loading...";

    [ObservableProperty]
    private string dashboardSerialNumber = "Loading...";

    [ObservableProperty]
    private string appInfoText = "RyotaQC V5 adalah software Quality Control untuk teknisi Windows yang mempercepat proses cek unit, instalasi tool, dan validasi hasil uji. Fitur utamanya meliputi System Information, Driver Pack, Apps Add-ons, Wallpaper Desktop, License & Activation, serta Quality Control checklist dan benchmark.";

    [ObservableProperty]
    private string copyrightText = "Created By IlhamRyota | Copyright © RyotaQC V5";

    [ObservableProperty]
    private string instagramName = "@ilhamryota";

    [ObservableProperty]
    private string youtubeName = "IlhamRyota Channel";

    public MainWindowViewModel(
        INavigationService navigationService,
        IUiFeedbackService feedbackService,
        IAppSettingsService settingsService,
        ISystemInfoService systemInfoService)
    {
        _navigationService = navigationService;
        _feedbackService = feedbackService;
        _settingsService = settingsService;
        _systemInfoService = systemInfoService;

        NavigateQualityCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.QualityControl));
        NavigateSettingsCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Settings));
        NavigateSystemInfoCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.SystemInformation));
        NavigateDriverPackCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.DriverPack));
        NavigateAppsAddonsCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.AppsAddons));
        NavigateWallpaperCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Wallpaper));

        OpenInstagramCommand = new RelayCommand(() => OpenUrl("https://instagram.com/ilhamryota"));
        OpenYoutubeCommand = new RelayCommand(() => OpenUrl("https://youtube.com/@ilhamryota"));
        RefreshDashboardCommand = new AsyncRelayCommand(LoadDashboardInfoAsync);

        _feedbackService.LoadingStateChanged += OnLoadingStateChanged;
        _feedbackService.ToastRequested += OnToastRequested;
        _settingsService.SettingsChanged += settings =>
        {
            AppTitle = settings.AppTitle;
        };

        AppTitle = _settingsService.Current.AppTitle;

        _ = LoadDashboardInfoAsync();
    }

    public IRelayCommand NavigateQualityCommand { get; }

    public IRelayCommand NavigateSettingsCommand { get; }

    public IRelayCommand NavigateSystemInfoCommand { get; }

    public IRelayCommand NavigateDriverPackCommand { get; }

    public IRelayCommand NavigateAppsAddonsCommand { get; }

    public IRelayCommand NavigateWallpaperCommand { get; }

    public IRelayCommand OpenInstagramCommand { get; }

    public IRelayCommand OpenYoutubeCommand { get; }

    public IAsyncRelayCommand RefreshDashboardCommand { get; }

    private void OnLoadingStateChanged(bool visible, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsLoading = visible;
            LoadingMessage = string.IsNullOrWhiteSpace(message) ? "Launching..." : message;
        });
    }

    private void OnToastRequested(string message, bool success)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            _toastVersion++;
            var version = _toastVersion;

            ToastMessage = message;
            ToastSuccess = success;
            IsToastVisible = true;

            await Task.Delay(2400);
            if (version == _toastVersion)
            {
                IsToastVisible = false;
            }
        });
    }

    private async Task LoadDashboardInfoAsync()
    {
        try
        {
            var info = await _systemInfoService.GetSystemInfoAsync();
            DashboardSystemModel = string.IsNullOrWhiteSpace(info.SystemModel) ? "N/A" : info.SystemModel;
            DashboardSerialNumber = string.IsNullOrWhiteSpace(info.BiosSerialNumber) ? "N/A" : info.BiosSerialNumber;
        }
        catch
        {
            DashboardSystemModel = "N/A";
            DashboardSerialNumber = "N/A";
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            _feedbackService.ShowToast("Gagal membuka link.", false);
        }
    }
}
