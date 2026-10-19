using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Models;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels;

public partial class HomeScreenViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IExternalToolLauncher _launcher;
    private readonly ISystemInfoService _systemInfoService;
    private readonly IUiFeedbackService _feedbackService;
    private readonly DispatcherTimer _clockTimer;

    [ObservableProperty]
    private string clockText = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private HomeMenu? selectedMenu;

    [ObservableProperty]
    private int selectedIndex;

    [ObservableProperty]
    private string wallpaperPath = string.Empty;

    [ObservableProperty]
    private string currentUserName = "ilhamryota";

    [ObservableProperty]
    private string systemModel = "Loading...";

    [ObservableProperty]
    private string serialNumber = "Loading...";

    [ObservableProperty]
    private string instagramName = "IlhamRyota";

    public HomeScreenViewModel(
        INavigationService navigationService,
        IExternalToolLauncher launcher,
        IAppSettingsService settingsService,
        ISystemInfoService systemInfoService,
        IUiFeedbackService feedbackService)
    {
        _navigationService = navigationService;
        _launcher = launcher;
        _systemInfoService = systemInfoService;
        _feedbackService = feedbackService;

        Menus = new ObservableCollection<HomeMenu>
        {
            new() { Key = "system", Title = "System Information", Subtitle = "OS, disk, user, serial", Description = "Lihat informasi lengkap sistem, pengguna, disks, dan serial number unit.", IconGlyph = "\uE9D9", WallpaperFile = "WP1.jpg", ImageFile = "system-information.jpg", Page = AppPage.SystemInformation },
            new() { Key = "driver", Title = "Driver Pack", Subtitle = "Dell, Lenovo, HP", Description = "Pilih driver pack sesuai merek dan model laptop yang sedang diuji.", IconGlyph = "\uE950", WallpaperFile = "WP2.jpg", ImageFile = "driver-pack-system.jpg", Page = AppPage.DriverPack },
            new() { Key = "software", Title = "Software Center", Subtitle = "Tools & applications", Description = "Pusat instalasi aplikasi, utility teknisi, add-ons, dan software pendukung QC.", IconGlyph = "\uE8F1", WallpaperFile = "WP3.jpg", ImageFile = "apps-addons.jpg", Page = AppPage.AppsAddons },
            new() { Key = "wallpaper", Title = "Wallpaper", Subtitle = "Set desktop wallpaper", Description = "Pilih dan terapkan wallpaper desktop standar Ryota QC.", IconGlyph = "\uE91B", WallpaperFile = "WP4.jpg", ImageFile = "wallpaper-desktop.jpg", Page = AppPage.Wallpaper },
            new() { Key = "license", Title = "Software Licensing", Subtitle = "License & activation", Description = "Kelola aktivasi, lisensi, dan status software pada unit.", IconGlyph = "\uE8D7", WallpaperFile = "WP5.jpg", ImageFile = "license-activation.jpg", Page = AppPage.Settings },
            new() { Key = "quick", Title = "Quick", Subtitle = "Akses cepat tool", Description = "Launcher cepat untuk tools & utilitas Windows yang sering dipakai.", IconGlyph = "\uE710", WallpaperFile = "WP4.jpg", ImageFile = "online-apps-card.jpg", Page = AppPage.Quick },
            new() { Key = "qc", Title = "Quality Control", Subtitle = "Benchmark & checklist", Description = "Jalankan checklist QC, benchmark, validasi hasil uji, dan laporan teknisi.", IconGlyph = "\uEA18", WallpaperFile = "WP4.jpg", ImageFile = "quality-control.jpg", Page = AppPage.QualityControl }
        };

        foreach (var menu in Menus)
        {
            menu.ImagePath = ResolveTileImage(menu.ImageFile);
        }

        BackCommand = new RelayCommand(GoBack);
        NavLeftCommand = new RelayCommand(() => MoveRelative(-1), () => CanGoPrev);
        NavRightCommand = new RelayCommand(() => MoveRelative(1), () => CanGoNext);
        OpenSelectedCommand = new RelayCommand(OpenSelected);
        OpenMenuCommand = new RelayCommand<HomeMenu?>(OpenMenu);
        OpenModuleByKeyCommand = new RelayCommand<string>(OpenModule);
        SelectMenuCommand = new RelayCommand<HomeMenu?>(menu =>
        {
            if (menu is null)
            {
                return;
            }

            var idx = Menus.IndexOf(menu);
            if (idx < 0)
            {
                return;
            }

            SelectIndex(idx, animate: true);
        });
        OpenSettingsCommand = new RelayCommand(() => Navigate(AppPage.Settings));
        OpenSearchCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.SystemInformation));
        OpenInstagramCommand = new RelayCommand(() => OpenUrl("https://www.instagram.com/ilhamryota23/"));

        _systemInfoService.GetSystemInfoAsync().ContinueWith(t =>
        {
            var info = t.Result;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SystemModel = string.IsNullOrWhiteSpace(info.SystemModel) ? "N/A" : info.SystemModel;
                SerialNumber = string.IsNullOrWhiteSpace(info.BiosSerialNumber) ? "N/A" : info.BiosSerialNumber;
            });
        }, TaskScheduler.Default);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText = DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();

        SelectedIndex = 0;
        SelectIndex(0, animate: false);
    }

    public ObservableCollection<HomeMenu> Menus { get; }

    public IRelayCommand<HomeMenu?> SelectMenuCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand NavLeftCommand { get; }

    public IRelayCommand NavRightCommand { get; }

    public IRelayCommand OpenSelectedCommand { get; }

    /// <summary>
    /// Opens a specific menu item by selecting it first, then navigating.
    /// Used by double-click and touch so the CLICKED menu opens, not the
    /// previously selected one.
    /// </summary>
    public IRelayCommand<HomeMenu?> OpenMenuCommand { get; }

    public IRelayCommand<string> OpenModuleByKeyCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand OpenSearchCommand { get; }

    public IRelayCommand OpenInstagramCommand { get; }

    public bool CanGoPrev => true;

    public bool CanGoNext => true;

    public bool IsHomeVisible { get; private set; } = true;

    public void SelectIndex(int index, bool animate = false)
    {
        if (index < 0 || index >= Menus.Count)
        {
            return;
        }

        var menu = Menus[index];
        if (!ReferenceEquals(SelectedMenu, menu))
        {
            SelectedMenu = menu;
        }

        if (SelectedIndex != index)
        {
            SelectedIndex = index;
        }

        WallpaperPath = ResolveWallpaper(menu.WallpaperFile);
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        NavLeftCommand.NotifyCanExecuteChanged();
        NavRightCommand.NotifyCanExecuteChanged();
    }

    public void MoveRelative(int delta)
    {
        if (Menus.Count == 0)
        {
            return;
        }

        var target = (SelectedIndex + delta) % Menus.Count;
        if (target < 0)
        {
            target += Menus.Count;
        }

        SelectIndex(target, animate: true);
    }

    public void OpenSelected()
    {
        if (SelectedMenu is null)
        {
            return;
        }

        if (TryLaunchSpecialMenu(SelectedMenu))
        {
            return;
        }

        Navigate(SelectedMenu.Page);
    }

    /// <summary>
    /// Selects the given menu and immediately opens it. This ensures the
    /// CLICKED menu is the one that opens, regardless of what was previously
    /// selected (fixes the bug where double-clicking Driver Pack while System
    /// Information was selected would open System Information instead).
    /// </summary>
    public void OpenMenu(HomeMenu? menu)
    {
        if (menu is null)
        {
            return;
        }

        var idx = Menus.IndexOf(menu);
        if (idx < 0)
        {
            return;
        }

        SelectIndex(idx, animate: false);

        if (TryLaunchSpecialMenu(menu))
        {
            return;
        }

        Navigate(menu.Page);
    }

    /// <summary>
    /// Central router / open_module(key). Maps a menu key to the existing
    /// application page via the navigation service.
    /// </summary>
    public void OpenModule(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        foreach (var menu in Menus)
        {
            if (!string.Equals(menu.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SelectedIndex = Menus.IndexOf(menu);

            if (TryLaunchSpecialMenu(menu))
            {
                return;
            }

            Navigate(menu.Page);
            return;
        }
    }

    /// <summary>
    /// Handler khusus untuk menu yang TIDAK navigasi ke page tertentu,
    /// melainkan langsung me-launch tool external (mis. "license" → MAS_AIO.cmd).
    /// Return true jika menu ditangani di sini (pemanggil tidak perlu Navigate).
    /// </summary>
    private bool TryLaunchSpecialMenu(HomeMenu menu)
    {
        if (string.Equals(menu.Key, "license", StringComparison.OrdinalIgnoreCase))
        {
            // Software Licensing → langsung buka tools/MAS_AIO.cmd sebagai admin.
            // Bukan navigasi ke Settings/About page.
            _launcher.LaunchById("windows_activation");
            return true;
        }

        return false;
    }

    private void Navigate(AppPage page)
    {
        _navigationService.Navigate(page);
    }

    private void GoBack()
    {
        _navigationService.Navigate(AppPage.Dashboard);
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _feedbackService.ShowToast("Gagal membuka link: " + ex.Message, false);
        }
    }

    private string ResolveTileImage(string fileName)
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles", fileName),
            System.IO.Path.Combine("Assets", "Images", "Tiles", fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var fallback = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles", "system-information.jpg");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return "Assets/Images/Tiles/system-information.jpg";
    }

    private string ResolveWallpaper(string fileName)
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Wallpapers", fileName),
            System.IO.Path.Combine("Assets", "Images", "Wallpapers", fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var fallback = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Wallpapers", "WP1.jpg");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return "Assets/Images/Wallpapers/WP1.jpg";
    }
}
