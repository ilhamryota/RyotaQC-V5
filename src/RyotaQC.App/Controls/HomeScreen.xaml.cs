using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using RyotaQC.App.ViewModels;

namespace RyotaQC.App.Controls;

public partial class HomeScreen : UserControl
{
    private readonly HomeScreenViewModel _viewModel;

    public HomeScreen()
    {
        InitializeComponent();
        _viewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<HomeScreenViewModel>(App.Services);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    public HomeScreen(HomeScreenViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    public HomeScreenViewModel ViewModel => _viewModel;

    private void Root_OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectIndex(_viewModel.SelectedIndex);
        SyncSelectionVisual();
        AnimateWallpaper(_viewModel.WallpaperPath);
        ScrollSelectedToCenter();
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeScreenViewModel.SelectedIndex):
                SyncSelectionVisual();
                break;
            case nameof(HomeScreenViewModel.WallpaperPath):
                AnimateWallpaper(_viewModel.WallpaperPath);
                break;
        }
    }

    private void SyncSelectionVisual()
    {
        for (var i = 0; i < _viewModel.Menus.Count; i++)
        {
            _viewModel.Menus[i].IsSelected = i == _viewModel.SelectedIndex;
        }

        if (TileScroller is not null && TileItems is not null)
        {
            ScrollSelectedToCenter();
        }
    }

    private void ScrollSelectedToCenter()
    {
        TileItems.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TileItems.UpdateLayout();

            var index = _viewModel.SelectedIndex;
            if (index < 0 || index >= _viewModel.Menus.Count)
            {
                return;
            }

            var item = _viewModel.Menus[index];
            if (TileItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
            {
                return;
            }

            var x = container.TranslatePoint(new Point(0, 0), TileItems).X;
            var centerOfItem = x + (container.ActualWidth / 2);

            var viewportWidth = TileScroller.ViewportWidth > 0
                ? TileScroller.ViewportWidth
                : TileScroller.ActualWidth;

            var targetOffset = centerOfItem - (viewportWidth / 2);
            var maxOffset = Math.Max(0, TileScroller.ScrollableWidth);
            targetOffset = Math.Clamp(targetOffset, 0, maxOffset);

            TileScroller.ScrollToHorizontalOffset(targetOffset);
        });
    }

    private static readonly Dictionary<string, BitmapImage> _wallpaperCache = new();

    private void AnimateWallpaper(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Cache wallpaper BitmapImages so we don't reload from disk on
        // every selection change — this eliminates the lag when switching menus.
        if (!_wallpaperCache.TryGetValue(path, out var bitmap))
        {
            bitmap = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute))
            {
                CacheOption = BitmapCacheOption.OnLoad
            };
            bitmap.Freeze(); // freeze for cross-thread performance
            _wallpaperCache[path] = bitmap;
        }

        WallpaperImage.Source = bitmap;
        WallpaperImage.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    public bool TryHandleKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                if (_viewModel.NavLeftCommand.CanExecute(null))
                {
                    _viewModel.NavLeftCommand.Execute(null);
                    return true;
                }
                return false;
            case Key.Right:
                if (_viewModel.NavRightCommand.CanExecute(null))
                {
                    _viewModel.NavRightCommand.Execute(null);
                    return true;
                }
                return false;
            case Key.Enter:
            case Key.Space:
                if (_viewModel.OpenSelectedCommand.CanExecute(null))
                {
                    _viewModel.OpenSelectedCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            ExitFullscreen();
            e.Handled = true;
        }
    }

    private Window? HostWindow => Window.GetWindow(this);

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } win)
        {
            win.WindowState = WindowState.Minimized;
        }
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is not { } win)
        {
            return;
        }

        win.WindowState = win.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeGlyph(win);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } win)
        {
            win.Close();
        }
    }

    private void UpdateMaximizeGlyph(Window win)
    {
        MaximizeButton.Content = win.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = win.WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    public void ToggleFullscreen()
    {
        var win = Window.GetWindow(this);
        if (win is null)
        {
            return;
        }

        if (win.WindowStyle == WindowStyle.None && win.WindowState == WindowState.Maximized)
        {
            win.WindowStyle = WindowStyle.SingleBorderWindow;
            win.ResizeMode = ResizeMode.CanResize;
            win.WindowState = WindowState.Normal;
            win.MaxHeight = double.PositiveInfinity;
            win.MaxWidth = double.PositiveInfinity;
        }
        else
        {
            win.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
            win.MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;
            win.WindowStyle = WindowStyle.None;
            win.ResizeMode = ResizeMode.NoResize;
            win.WindowState = WindowState.Maximized;
        }
    }

    public void ExitFullscreen()
    {
        var win = Window.GetWindow(this);
        if (win is null)
        {
            return;
        }

        if (win.WindowStyle == WindowStyle.None)
        {
            win.WindowStyle = WindowStyle.SingleBorderWindow;
            win.ResizeMode = ResizeMode.CanResize;
            win.WindowState = WindowState.Normal;
            win.MaxHeight = double.PositiveInfinity;
            win.MaxWidth = double.PositiveInfinity;
        }
    }
}
