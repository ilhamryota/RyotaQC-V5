using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public partial class DashboardPageViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IExternalToolLauncher _launcher;
    private readonly IAppSettingsService _settingsService;
    private readonly IUiFeedbackService _feedbackService;
    private readonly string _workspaceRoot;

    public DashboardPageViewModel(
        INavigationService navigationService,
        IExternalToolLauncher launcher,
        IAppSettingsService settingsService,
        IUiFeedbackService feedbackService)
    {
        _navigationService = navigationService;
        _launcher = launcher;
        _settingsService = settingsService;
        _feedbackService = feedbackService;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();

        Tiles = new ObservableCollection<DashboardTileItem>
        {
            CreateTile("System Information", "OS, disk, user, serial", "system-information.jpg", "&#xE9D9;", () => _navigationService.Navigate(AppPage.SystemInformation), page: AppPage.SystemInformation),
            CreateTile("Driver Pack", "Dell, Lenovo, HP", "driver-pack-system.jpg", "&#xE950;", () => _navigationService.Navigate(AppPage.DriverPack), page: AppPage.DriverPack),
            CreateTile("Software Center", "Tools & applications", "apps-addons.jpg", "&#xE8F1;", () => _navigationService.Navigate(AppPage.AppsAddons), page: AppPage.AppsAddons),
            CreateTile("Wallpaper", "Set desktop wallpaper", "wallpaper-desktop.jpg", "&#xE91B;", () => _navigationService.Navigate(AppPage.Wallpaper), page: AppPage.Wallpaper),
            CreateTile("Software Licensing", "License & activation", "license-activation.jpg", "&#xE8D7;", () => _launcher.LaunchById("windows_activation")),
            CreateTile("Quality Control", "Benchmark & checklist", "quality-control.jpg", "&#xEA18;", () => _navigationService.Navigate(AppPage.QualityControl), page: AppPage.QualityControl)
        };

        NavigateSystemInformationCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.SystemInformation));
        PrevTileCommand = new RelayCommand(() => MoveTileRelative(-1), () => CanGoPrev);
        NextTileCommand = new RelayCommand(() => MoveTileRelative(1), () => CanGoNext);
        OpenCurrentTileCommand = new RelayCommand(OpenCurrentTile);
        SelectTileCommand = new RelayCommand<DashboardTileItem?>(SelectTile);

        if (Tiles.Count > 0)
        {
            CurrentTile = Tiles[0];
        }
    }

    public IRelayCommand NavigateSystemInformationCommand { get; }

    public IRelayCommand PrevTileCommand { get; }

    public IRelayCommand NextTileCommand { get; }

    public IRelayCommand OpenCurrentTileCommand { get; }

    public IRelayCommand<DashboardTileItem?> SelectTileCommand { get; }

    public ObservableCollection<DashboardTileItem> Tiles { get; }

    public bool CanGoPrev => SelectedTileIndex > 0;

    public bool CanGoNext => SelectedTileIndex < Tiles.Count - 1;

    [ObservableProperty]
    private int selectedTileIndex;

    [ObservableProperty]
    private DashboardTileItem? currentTile;

    partial void OnSelectedTileIndexChanged(int value)
    {
        var clamped = value;
        if (Tiles.Count > 0)
        {
            clamped = Math.Clamp(value, 0, Tiles.Count - 1);
            if (clamped != value)
            {
                SelectedTileIndex = clamped;
            }
        }

        var tile = (clamped >= 0 && clamped < Tiles.Count) ? Tiles[clamped] : null;
        if (!ReferenceEquals(CurrentTile, tile))
        {
            CurrentTile = tile;
        }

        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        PrevTileCommand.NotifyCanExecuteChanged();
        NextTileCommand.NotifyCanExecuteChanged();

        UpdateCurrentMarkers(clamped);
    }

    private void UpdateCurrentMarkers(int selectedIndex)
    {
        for (var i = 0; i < Tiles.Count; i++)
        {
            var isCurrent = i == selectedIndex;
            if (Tiles[i].IsCurrent != isCurrent)
            {
                Tiles[i].IsCurrent = isCurrent;
            }
        }
    }

    partial void OnCurrentTileChanged(DashboardTileItem? value)
    {
        if (value is null)
        {
            return;
        }

        var index = Tiles.IndexOf(value);
        if (index >= 0 && index != SelectedTileIndex)
        {
            SelectedTileIndex = index;
        }
    }

    private void MoveTileRelative(int delta)
    {
        var target = SelectedTileIndex + delta;
        if (target < 0 || target >= Tiles.Count)
        {
            return;
        }

        SelectedTileIndex = target;
    }

    private void SelectTile(DashboardTileItem? tile)
    {
        if (tile is null)
        {
            return;
        }

        var index = Tiles.IndexOf(tile);
        if (index >= 0)
        {
            SelectedTileIndex = index;
        }

        OpenCurrentTile();
    }

    private void OpenCurrentTile()
    {
        if (CurrentTile is null)
        {
            return;
        }

        CurrentTile.Command.Execute(null);
    }

    public string HeroImagePath { get; } = ResolveHeroImagePath("system-information.jpg");

    public string HeroTitle { get; } = "System Information";

    public string HeroSubtitle { get; } = "OS, disk, user, serial";

    public string HeroDescription { get; } = "Lihat informasi lengkap sistem, pengguna, disk, dan serial number.";

    private DashboardTileItem CreateTile(
        string title,
        string subtitle,
        string imageName,
        string iconGlyph,
        Action action,
        bool showTitle = true,
        bool showSubtitle = true,
        AppPage? page = null)
    {
        var imagePath = ResolveTileImagePath(imageName);

        DashboardTileItem item = null!;
        item = new DashboardTileItem
        {
            Title = title,
            Subtitle = subtitle,
            ImagePath = imagePath,
            IconGlyph = iconGlyph,
            ShowTitle = showTitle,
            ShowSubtitle = showSubtitle,
            Page = page,
            Command = new RelayCommand(() => action())
        };

        return item;
    }

    private static string ResolveHeroImagePath(string imageName)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles", imageName),
            Path.Combine("Assets", "Images", "Tiles", imageName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles", imageName);
    }

    private string ResolveTileImagePath(string imageName)
    {
        var configuredBasePath = _settingsService.Current.TileBackgroundImagePath;
        if (string.IsNullOrWhiteSpace(configuredBasePath))
        {
            configuredBasePath = "Assets/Images/Tiles";
        }

        var normalizedBasePath = configuredBasePath
            .Trim()
            .TrimEnd('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var candidateDirectories = new List<string>();
        if (Path.IsPathRooted(normalizedBasePath))
        {
            candidateDirectories.Add(normalizedBasePath);
        }
        else
        {
            candidateDirectories.Add(Path.Combine(AppContext.BaseDirectory, normalizedBasePath));
            candidateDirectories.Add(Path.Combine(_workspaceRoot, "src", "RyotaQC.App", normalizedBasePath));
        }

        foreach (var directory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(directory, imageName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        var runtimeFallback = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Tiles", "quality-control.jpg");
        if (File.Exists(runtimeFallback))
        {
            return runtimeFallback;
        }

        return Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Tiles", "quality-control.jpg");
    }
}

public sealed partial class DashboardTileItem : ObservableObject
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string ImagePath { get; init; }

    public required string IconGlyph { get; init; }

    public System.Windows.Media.Brush GlyphBrush { get; } = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0xC1, 0xF4));

    public bool ShowTitle { get; init; } = true;

    public bool ShowSubtitle { get; init; } = true;

    public AppPage? Page { get; init; }

    public required IRelayCommand Command { get; init; }

    [ObservableProperty]
    private bool isLocked;

    [ObservableProperty]
    private bool isCurrent;
}
