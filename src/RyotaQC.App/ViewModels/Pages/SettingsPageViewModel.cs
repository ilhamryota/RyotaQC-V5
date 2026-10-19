using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly INavigationService _navigationService;
    private readonly IUiFeedbackService _feedbackService;

    public SettingsPageViewModel(
        IAppSettingsService settingsService,
        IStartupRegistrationService startupRegistrationService,
        INavigationService navigationService,
        IUiFeedbackService feedbackService)
    {
        _settingsService = settingsService;
        _startupRegistrationService = startupRegistrationService;
        _navigationService = navigationService;
        _feedbackService = feedbackService;

        AppTitle = settingsService.Current.AppTitle;
        Version = "V5";
        Copyright = "Copyright © 2026 RyotaQC. All rights reserved.";
        Description = "RyotaQC V5 adalah aplikasi Quality Control desktop untuk diagnosa sistem, launcher tools teknisi, wallpaper setup, dan workflow verifikasi perangkat.";

        launchOnStartup = settingsService.Current.LaunchOnStartup;

        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
    }

    public string AppTitle { get; }

    public string Version { get; }

    public string Description { get; }

    public string Copyright { get; }

    public IRelayCommand BackToHomeCommand { get; }

    [ObservableProperty]
    private bool launchOnStartup;

    partial void OnLaunchOnStartupChanged(bool value)
    {
        _settingsService.Current.LaunchOnStartup = value;

        if (_settingsService.Save(_settingsService.Current, out var error))
        {
            if (value)
            {
                _startupRegistrationService.EnsureStartupEnabled();
            }
            else
            {
                _startupRegistrationService.DisableStartup();
            }

            return;
        }

        launchOnStartup = !value;
        _feedbackService.ShowToast(string.IsNullOrWhiteSpace(error)
            ? "Gagal menyimpan pengaturan startup."
            : $"Gagal menyimpan pengaturan startup: {error}", false);
    }
}
