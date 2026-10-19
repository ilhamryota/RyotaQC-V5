using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RyotaQC.App.Enums;
using RyotaQC.App.Services;
using RyotaQC.App.ViewModels;
using RyotaQC.App.Views.Pages;

namespace RyotaQC.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;
    private readonly IAppSettingsService _settingsService;
    private readonly IUiFeedbackService _feedbackService;
    private readonly HomeScreenViewModel _homeViewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceProvider serviceProvider,
        INavigationService navigationService,
        IAppSettingsService settingsService,
        IUiFeedbackService feedbackService,
        HomeScreenViewModel homeViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _feedbackService = feedbackService;
        _homeViewModel = homeViewModel;
        _navigationService.NavigationRequested += NavigateTo;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyGotKeyboardFocus), true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyLostKeyboardFocus), true);
        SizeChanged += (_, _) => UpdateSelectionBadge(Keyboard.FocusedElement as DependencyObject);
        LocationChanged += (_, _) => UpdateSelectionBadge(Keyboard.FocusedElement as DependencyObject);

        ApplyBackgroundImage();
        Loaded += Window_OnLoaded;
        ContentFrame.Navigated += ContentFrame_OnNavigated;
    }

    /// <summary>
    /// Central router (open_module). Routes a menu key to the existing page.
    /// </summary>
    public void OpenModuleRoute(string key)
    {
        HomeScreenControl.ViewModel.OpenModule(key);
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialWindowBounds();
        _navigationService.Navigate(AppPage.Dashboard);

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusFirstControlInFrame));
    }

    private void ApplyBackgroundImage()
    {
        var backgroundPath = BackgroundImageResolver.ResolveMainBackgroundPath();
        if (string.IsNullOrWhiteSpace(backgroundPath))
        {
            return;
        }

        MainBackgroundImage.Source = new BitmapImage(new Uri(backgroundPath, UriKind.Absolute));
    }

    private void ApplyInitialWindowBounds()
    {
        // Fullscreen above the Windows taskbar — uses WorkArea so the
        // taskbar stays visible and the app doesn't overlap it.
        var workArea = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
    }

    private void NavigateTo(AppPage page)
    {
        try
        {
            var target = ResolvePage(page);
            if (target is null)
            {
                return;
            }

            var isHome = page == AppPage.Dashboard;

            HomeScreenControl.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
            ContentFrame.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;

            if (isHome)
            {
                ModuleWallpaperImage.Visibility = Visibility.Collapsed;
                ModuleWallpaperOverlay.Visibility = Visibility.Collapsed;
                MainBackgroundImage.Opacity = 0.5;
            }
            else
            {
                ApplyModuleWallpaper();
            }

            ContentFrame.Opacity = 0;
            ContentFrame.Navigate(target);
        }
        catch (Exception ex)
        {
            // Log navigation errors instead of crashing silently
            try
            {
                var logger = new Services.FileAppLogger();
                logger.Error($"Navigation error to page {page}: {ex.Message}", ex);
            }
            catch { }
            MessageBox.Show(
                $"Gagal membuka halaman {page}.\n\nError: {ex.Message}\n\nDetail dicatat di log.",
                "RyotaQC V5",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static readonly Dictionary<string, BitmapImage> _moduleWallpaperCache = new();

    private void ApplyModuleWallpaper()
    {
        var path = _homeViewModel.WallpaperPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Cache module wallpaper to avoid reloading on every navigation.
        if (!_moduleWallpaperCache.TryGetValue(path, out var bitmap))
        {
            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _moduleWallpaperCache[path] = bitmap;
        }

        ModuleWallpaperImage.Source = bitmap;
        ModuleWallpaperImage.Visibility = Visibility.Visible;
        ModuleWallpaperOverlay.Visibility = Visibility.Visible;
        MainBackgroundImage.Opacity = 0;
    }

    private Page? ResolvePage(AppPage page)
    {
        return page switch
        {
            AppPage.Dashboard => _serviceProvider.GetRequiredService<DashboardPage>(),
            AppPage.SystemInformation => _serviceProvider.GetRequiredService<SystemInformationPage>(),
            AppPage.DriverPack => _serviceProvider.GetRequiredService<DriverPackPage>(),
            AppPage.AppsAddons => _serviceProvider.GetRequiredService<AppsAddonsPage>(),
            AppPage.Wallpaper => _serviceProvider.GetRequiredService<WallpaperDesktopPage>(),
            AppPage.QualityControl => _serviceProvider.GetRequiredService<QualityControlPage>(),
            AppPage.Quick => _serviceProvider.GetRequiredService<QuickLauncherPage>(),
            AppPage.Settings => _serviceProvider.GetRequiredService<SettingsPage>(),
            _ => null
        };
    }

    private void ContentFrame_OnNavigated(object? sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (_settingsService.Current.LightweightMode || !_settingsService.Current.EnablePageTransition)
        {
            ResetFrameTransform();
            ContentFrame.Opacity = 1;
            FocusFirstControl(e.Content as DependencyObject);
            return;
        }

        var durationMs = Math.Clamp(_settingsService.Current.PageTransitionMs, 0, 1000);
        if (durationMs <= 0)
        {
            ResetFrameTransform();
            ContentFrame.Opacity = 1;
            return;
        }

        // Smooth fade-only transition (removing scale+slide eliminates the
        // "heavy/laggy" feel when switching menus).
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };

        ContentFrame.BeginAnimation(OpacityProperty, fade);

        FocusFirstControl(e.Content as DependencyObject);
    }

    private void ResetFrameTransform()
    {
        FrameScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FrameScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FrameScale.ScaleX = 1;
        FrameScale.ScaleY = 1;
        FrameTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        FrameTranslate.Y = 0;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsLoading && e.Key == Key.Space)
        {
            _feedbackService.HideLoading();
            _navigationService.Navigate(AppPage.Dashboard);
            _feedbackService.ShowToast("Loading dibatalkan. Kembali ke Main Menu.", false);
            e.Handled = true;
            return;
        }

        var focusedElement = Keyboard.FocusedElement;

        // Home screen arrow navigation must work regardless of focus.
        if (HomeScreenControl.Visibility == Visibility.Visible)
        {
            if ((e.Key is Key.Left or Key.Right or Key.Enter or Key.Space)
                && HomeScreenControl.TryHandleKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                HomeScreenControl.ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                HomeScreenControl.ExitFullscreen();
                e.Handled = true;
                return;
            }

            return;
        }

        // Quick page numeric options (D1-D9, top row keys).
        if (_navigationService.CurrentPage == AppPage.Quick &&
            ContentFrame.Content is QuickLauncherPage quickPage &&
            e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5
                or Key.D6 or Key.D7 or Key.D8 or Key.D9)
        {
            if (quickPage.TryHandleNumberKey(e.Key))
            {
                e.Handled = true;
                return;
            }
        }

        if (focusedElement is null || ReferenceEquals(focusedElement, this) || ReferenceEquals(focusedElement, ContentFrame))
        {
            FocusFirstControlInFrame();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Escape or Key.Back)
        {
            if (e.Key == Key.Escape && focusedElement is ComboBox openCombo && openCombo.IsDropDownOpen)
            {
                return;
            }

            if (e.Key == Key.Back && IsEditableInputFocused(focusedElement))
            {
                return;
            }

            if (_navigationService.NavigateBack())
            {
                e.Handled = true;
                return;
            }

            if (_navigationService.CurrentPage != AppPage.Dashboard)
            {
                _navigationService.Navigate(AppPage.Dashboard);
                e.Handled = true;
            }

            return;
        }

        if (ShouldSkipGlobalNavigation(focusedElement, e.Key))
        {
            return;
        }

        if (TryRouteDashboardKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                e.Handled = MoveFocus(FocusNavigationDirection.Left);
                break;
            case Key.Right:
                e.Handled = MoveFocus(FocusNavigationDirection.Right);
                break;
            case Key.Up:
                e.Handled = MoveFocus(FocusNavigationDirection.Up);
                break;
            case Key.Down:
                e.Handled = MoveFocus(FocusNavigationDirection.Down);
                break;
            case Key.Tab:
                e.Handled = MoveFocus((Keyboard.Modifiers & ModifierKeys.Shift) != 0
                    ? FocusNavigationDirection.Previous
                    : FocusNavigationDirection.Next);
                break;
            case Key.Enter:
            case Key.Space:
                e.Handled = ActivateFocusedElement(focusedElement);
                break;
        }
    }

    private bool TryRouteDashboardKey(Key key)
    {
        if (_navigationService.CurrentPage != AppPage.Dashboard)
        {
            return false;
        }

        if (HomeScreenControl.Visibility == Visibility.Visible)
        {
            return HomeScreenControl.TryHandleKey(key);
        }

        if (ContentFrame.Content is not DashboardPage dashboard)
        {
            return false;
        }

        return dashboard.TryHandleDashboardKey(key);
    }

    private static bool MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is not UIElement focused)
        {
            return false;
        }

        if (focused.MoveFocus(new TraversalRequest(direction)))
        {
            return true;
        }

        return direction switch
        {
            FocusNavigationDirection.Left or FocusNavigationDirection.Up => focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)),
            FocusNavigationDirection.Right or FocusNavigationDirection.Down => focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
            _ => false
        };
    }

    private static bool ActivateFocusedElement(IInputElement focusedElement)
    {
        if (focusedElement is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;
            return true;
        }

        if (focusedElement is Expander expander)
        {
            expander.IsExpanded = !expander.IsExpanded;
            return true;
        }

        if (focusedElement is ButtonBase button)
        {
            if (button.Command is not null)
            {
                var parameter = button.CommandParameter;
                if (button.Command.CanExecute(parameter))
                {
                    button.Command.Execute(parameter);
                    return true;
                }
            }

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return true;
        }

        return false;
    }

    private static bool IsEditableInputFocused(IInputElement focusedElement)
    {
        if (focusedElement is TextBoxBase or PasswordBox or RichTextBox)
        {
            return true;
        }

        if (focusedElement is ComboBox comboBox)
        {
            return comboBox.IsEditable || comboBox.IsDropDownOpen;
        }

        return false;
    }

    private static bool ShouldSkipGlobalNavigation(IInputElement focusedElement, Key key)
    {
        if (IsEditableInputFocused(focusedElement))
        {
            return true;
        }

        if (focusedElement is ComboBox comboBox)
        {
            if (comboBox.IsDropDownOpen)
            {
                return true;
            }

            if (key is Key.Up or Key.Down)
            {
                return true;
            }
        }

        if (focusedElement is ListBox or ListView or TreeView || focusedElement is Slider or ScrollBar)
        {
            if (key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Space or Key.Enter)
            {
                return true;
            }
        }

        return false;
    }

    private void FocusFirstControlInFrame()
    {
        FocusFirstControl(ContentFrame.Content as DependencyObject);
    }

    private static void FocusFirstControl(DependencyObject? root)
    {
        if (root is not DependencyObject dependencyRoot)
        {
            return;
        }

        void TryFocus()
        {
            var first = FindFirstFocusable(dependencyRoot);
            if (first is not null)
            {
                Keyboard.Focus(first);
                first.Focus();
            }
        }

        var dispatcher = Application.Current.Dispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(TryFocus));
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(TryFocus));
    }

    private static Control? FindFirstFocusable(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current is Control control && control.Focusable && control.IsEnabled && control.IsVisible && control.IsTabStop)
            {
                return control;
            }

            var childrenCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childrenCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
    }

    private void OnAnyGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateSelectionBadge(e.NewFocus as DependencyObject);
    }

    private void OnAnyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            UpdateSelectionBadge(Keyboard.FocusedElement as DependencyObject);
        }));
    }

    private void UpdateSelectionBadge(DependencyObject? focusedObject)
    {
        if (focusedObject is not UIElement element ||
            !element.IsVisible ||
            element.RenderSize.Width <= 1 ||
            element.RenderSize.Height <= 1 ||
            !IsInsideContentFrame(element))
        {
            KeyboardSelectedBadge.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var topLeft = element.TranslatePoint(new Point(0, 0), this);
            var x = topLeft.X;
            var y = topLeft.Y;

            const double margin = 8;
            const double minY = 56; // keep below custom title bar
            var targetX = x + element.RenderSize.Width - KeyboardSelectedBadge.ActualWidth - 6;
            var targetY = Math.Max(minY, y - 26);

            if (double.IsNaN(targetX) || double.IsInfinity(targetX))
            {
                targetX = margin;
            }

            if (double.IsNaN(targetY) || double.IsInfinity(targetY))
            {
                targetY = minY;
            }

            var maxX = Math.Max(margin, ActualWidth - KeyboardSelectedBadge.ActualWidth - margin);
            targetX = Math.Max(margin, Math.Min(targetX, maxX));

            Canvas.SetLeft(KeyboardSelectedBadge, targetX);
            Canvas.SetTop(KeyboardSelectedBadge, targetY);
            KeyboardSelectedBadge.Visibility = Visibility.Visible;
        }
        catch
        {
            KeyboardSelectedBadge.Visibility = Visibility.Collapsed;
        }
    }

    private bool IsInsideContentFrame(DependencyObject target)
    {
        DependencyObject? current = target;
        while (current is not null)
        {
            if (ReferenceEquals(current, ContentFrame))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
