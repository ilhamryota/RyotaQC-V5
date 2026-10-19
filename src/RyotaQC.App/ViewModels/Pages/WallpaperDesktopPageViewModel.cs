using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyotaQC.App.Enums;
using RyotaQC.App.Models;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public partial class WallpaperDesktopPageViewModel : ObservableObject
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    private readonly IWallpaperService _wallpaperService;
    private readonly IUiFeedbackService _feedback;
    private readonly INavigationService _navigationService;
    private readonly string _workspaceRoot;
    private readonly List<FileSystemWatcher> _wallpaperWatchers = [];
    private DateTime _lastAutoRefreshAtUtc = DateTime.MinValue;
    private bool _isRefreshing;

    [ObservableProperty]
    private WallpaperItemModel? selectedWallpaper;

    [ObservableProperty]
    private string activeWallpaperPath = string.Empty;

    [ObservableProperty]
    private bool hasItems;

    [ObservableProperty]
    private string previewWallpaperPath = string.Empty;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public WallpaperDesktopPageViewModel(
        IWallpaperService wallpaperService,
        IUiFeedbackService feedback,
        INavigationService navigationService)
    {
        _wallpaperService = wallpaperService;
        _feedback = feedback;
        _navigationService = navigationService;
        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();

        Wallpapers = new ObservableCollection<WallpaperItemModel>();
        DesktopShortcuts = new ObservableCollection<DesktopShortcutItem>();

        RefreshCommand = new RelayCommand(Refresh);
        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
        SetWallpaperCommand = new RelayCommand<WallpaperItemModel>(SetWallpaper);

        SetupWallpaperWatchers();
        Refresh();
    }

    public ObservableCollection<WallpaperItemModel> Wallpapers { get; }

    public ObservableCollection<DesktopShortcutItem> DesktopShortcuts { get; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand BackToHomeCommand { get; }

    public IRelayCommand<WallpaperItemModel> SetWallpaperCommand { get; }

    private void Refresh()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            _wallpaperService.Refresh();

            Wallpapers.Clear();
            foreach (var wallpaper in _wallpaperService.GetWallpapers())
            {
                Wallpapers.Add(wallpaper);
            }

            SelectedWallpaper = Wallpapers.FirstOrDefault();
            ActiveWallpaperPath = _wallpaperService.GetCurrentWallpaperPath();
            PreviewWallpaperPath = ResolvePreviewPath();
            LoadDesktopShortcuts();
            HasItems = Wallpapers.Count > 0;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void SetWallpaper(WallpaperItemModel? wallpaper)
    {
        if (wallpaper is null)
        {
            return;
        }

        SelectedWallpaper = wallpaper;
        if (!_wallpaperService.SetWallpaper(wallpaper, out var error))
        {
            _feedback.ShowToast(error, false);
            return;
        }

        ActiveWallpaperPath = _wallpaperService.GetCurrentWallpaperPath();
        PreviewWallpaperPath = ResolvePreviewPath();
        LoadDesktopShortcuts();
        _feedback.ShowToast("Wallpaper desktop berhasil diperbarui.", true);
    }

    private string ResolvePreviewPath()
    {
        if (!string.IsNullOrWhiteSpace(ActiveWallpaperPath) && File.Exists(ActiveWallpaperPath))
        {
            return ActiveWallpaperPath;
        }

        if (SelectedWallpaper is not null && File.Exists(SelectedWallpaper.AbsolutePath))
        {
            return SelectedWallpaper.AbsolutePath;
        }

        return string.Empty;
    }

    private void LoadDesktopShortcuts()
    {
        DesktopShortcuts.Clear();

        var desktopDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        var shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in desktopDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    shortcuts.TryAdd(name, file);
                }
            }
        }

        foreach (var item in shortcuts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            DesktopShortcuts.Add(new DesktopShortcutItem
            {
                Name = item.Key,
                Icon = GetShortcutIcon(item.Value)
            });
        }
    }

    private void SetupWallpaperWatchers()
    {
        var runtimeDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Wallpapers");
        var sourceDir = Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Wallpapers");

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(runtimeDir))
        {
            directories.Add(runtimeDir);
        }

        if (Directory.Exists(sourceDir))
        {
            directories.Add(sourceDir);
        }

        foreach (var dir in directories)
        {
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            watcher.Created += OnWallpaperFolderChanged;
            watcher.Deleted += OnWallpaperFolderChanged;
            watcher.Changed += OnWallpaperFolderChanged;
            watcher.Renamed += OnWallpaperFolderChanged;
            watcher.EnableRaisingEvents = true;
            _wallpaperWatchers.Add(watcher);
        }
    }

    private void OnWallpaperFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedWallpaperExtension(e.FullPath))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastAutoRefreshAtUtc).TotalMilliseconds < 350)
        {
            return;
        }

        _lastAutoRefreshAtUtc = now;
        App.Current.Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private static bool IsSupportedWallpaperExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource? GetShortcutIcon(string shortcutPath)
    {
        var result = SHGetFileInfo(
            shortcutPath,
            0,
            out var info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiSmallIcon);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}

public sealed class DesktopShortcutItem
{
    public string Name { get; init; } = string.Empty;

    public ImageSource? Icon { get; init; }
}
