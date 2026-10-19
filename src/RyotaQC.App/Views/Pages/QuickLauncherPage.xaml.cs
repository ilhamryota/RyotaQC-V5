using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RyotaQC.App.Models;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class QuickLauncherPage : Page
{
    private readonly QuickLauncherPageViewModel _viewModel;
    private DispatcherTimer? _countdownTimer;
    private TaskCompletionSource<bool>? _confirmTcs;
    private TaskCompletionSource<string>? _appMethodTcs;
    private int _countdownRemaining;

    public QuickLauncherPage(QuickLauncherPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.ConfirmRequested += OnConfirmRequested;
        _viewModel.AppMethodRequested += OnAppMethodRequested;
        _viewModel.WallpaperSelectionRequested += OnWallpaperSelectionRequested;
        _viewModel.RunAllCompleted += OnRunAllCompleted;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(QuickLauncherPageViewModel.ShowWallpaperGrid))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var showWallpaper = _viewModel.ShowWallpaperGrid;
                    WallpaperPanel.Visibility = showWallpaper ? Visibility.Visible : Visibility.Collapsed;
                    IdentityPanel.Visibility = showWallpaper ? Visibility.Collapsed : Visibility.Visible;
                });
            }
            else if (e.PropertyName == nameof(QuickLauncherPageViewModel.LogLines) ||
                     e.PropertyName == nameof(QuickLauncherPageViewModel.IsBusy))
            {
                ScrollLogToEnd();
            }
        };

        _viewModel.LogLines.CollectionChanged += (_, _) => ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        });
    }

    // ------------------------------------------------------------- keyboard

    private void Page_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleNumberKey(e.Key))
        {
            e.Handled = true;
        }
    }

    public bool TryHandleNumberKey(Key key)
    {
        if (_viewModel.IsBusy)
        {
            return false;
        }

        if (PopupOverlay.Visibility == Visibility.Visible)
        {
            return HandlePopupKey(key);
        }

        var option = key switch
        {
            Key.D1 => 1,
            Key.D2 => 2,
            Key.D3 => 3,
            Key.D4 => 4,
            Key.D5 => 5,
            Key.D6 => 6,
            Key.D7 => 7,
            Key.D8 => 8,
            Key.D9 => 9,
            _ => 0
        };

        if (option == 0)
        {
            return false;
        }

        _ = _viewModel.ExecuteOptionAsync(option);
        return true;
    }

    private bool HandlePopupKey(Key key)
    {
        if (AppMethodPopup.Visibility == Visibility.Visible)
        {
            switch (key)
            {
                case Key.D1: AppOffline_Click(this, new RoutedEventArgs()); return true;
                case Key.D2: AppOnline_Click(this, new RoutedEventArgs()); return true;
                case Key.Escape: AppCancel_Click(this, new RoutedEventArgs()); return true;
            }
        }
        else if (ConfirmPopup.Visibility == Visibility.Visible)
        {
            switch (key)
            {
                case Key.D1:
                case Key.Enter: ConfirmYes_Click(this, new RoutedEventArgs()); return true;
                case Key.D2:
                case Key.Escape: ConfirmNo_Click(this, new RoutedEventArgs()); return true;
            }
        }
        else if (ResultsPopup.Visibility == Visibility.Visible)
        {
            if (key is Key.Enter or Key.Escape)
            {
                ResultsClose_Click(this, new RoutedEventArgs());
                return true;
            }
        }

        return false;
    }

    // --------------------------------------------------------------- options

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string number } && int.TryParse(number, out var option))
        {
            _ = _viewModel.ExecuteOptionAsync(option);
        }
    }

    // ------------------------------------------------------------- popups

    private Task<bool> OnConfirmRequested(string message, int timeoutSeconds)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConfirmMessage.Text = message;
            PopupOverlay.Visibility = Visibility.Visible;
            ConfirmPopup.Visibility = Visibility.Visible;

            StartCountdown(timeoutSeconds, ConfirmCountdown, "Tanpa jawaban otomatis LANJUT dalam {0} dtk...", () =>
            {
                ConfirmYes_Click(this, new RoutedEventArgs());
            });

            return await _confirmTcs.Task;
        }).Task.Unwrap();
    }

    private Task<string> OnAppMethodRequested(int timeoutSeconds)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            _appMethodTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            PopupOverlay.Visibility = Visibility.Visible;
            AppMethodPopup.Visibility = Visibility.Visible;

            StartCountdown(timeoutSeconds, AppMethodCountdown, "Tanpa jawaban otomatis OFFLINE dalam {0} dtk...", () =>
            {
                AppOffline_Click(this, new RoutedEventArgs());
            });

            return await _appMethodTcs.Task;
        }).Task.Unwrap();
    }

    private Task OnWallpaperSelectionRequested()
    {
        // Grid visibility is already handled by ShowWallpaperGrid; nothing more to do.
        return Task.CompletedTask;
    }

    private void StartCountdown(int seconds, TextBlock target, string format, Action onTimeout)
    {
        _countdownTimer?.Stop();
        _countdownRemaining = seconds;
        target.Text = string.Format(format, _countdownRemaining);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdownRemaining--;
            if (_countdownRemaining <= 0)
            {
                _countdownTimer.Stop();
                onTimeout();
            }
            else
            {
                target.Text = string.Format(format, _countdownRemaining);
            }
        };
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
    }

    private void ConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        ConfirmPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
        _confirmTcs?.TrySetResult(true);
        _confirmTcs = null;
    }

    private void ConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        ConfirmPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
        _confirmTcs?.TrySetResult(false);
        _confirmTcs = null;
    }

    private void AppOffline_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        AppMethodPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
        _appMethodTcs?.TrySetResult("Offline");
        _appMethodTcs = null;
    }

    private void AppOnline_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        AppMethodPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
        _appMethodTcs?.TrySetResult("Online");
        _appMethodTcs = null;
    }

    private void AppCancel_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        AppMethodPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
        _appMethodTcs?.TrySetResult("Cancel");
        _appMethodTcs = null;
    }

    private void HideOverlayIfIdle()
    {
        if (ConfirmPopup.Visibility == Visibility.Collapsed &&
            AppMethodPopup.Visibility == Visibility.Collapsed &&
            ResultsPopup.Visibility == Visibility.Collapsed)
        {
            PopupOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------- results

    private void OnRunAllCompleted(List<QuickResultItem> results)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ResultsList.Children.Clear();

            var groups = new[]
            {
                ("Berhasil", "#34D399", "\u2713"),
                ("Sedang Berjalan", "#4CA7FF", "\u25F7"),
                ("Gagal", "#F87171", "\u2715"),
                ("Dilewati", "#94A3B8", "\u23ED")
            };

            foreach (var (category, color, symbol) in groups)
            {
                var items = results.Where(r => r.Category == category).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                var header = new TextBlock
                {
                    Text = $"{symbol}  {category.ToUpper()}: {items.Count}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 10, 0, 4)
                };
                ResultsList.Children.Add(header);

                foreach (var item in items)
                {
                    var row = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(item.Detail)
                            ? $"    {item.Name}"
                            : $"    {item.Name} — {item.Detail}",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AEB7CA")),
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    ResultsList.Children.Add(row);
                }
            }

            PopupOverlay.Visibility = Visibility.Visible;
            ResultsPopup.Visibility = Visibility.Visible;
        });
    }

    private void ResultsClose_Click(object sender, RoutedEventArgs e)
    {
        ResultsPopup.Visibility = Visibility.Collapsed;
        HideOverlayIfIdle();
    }
}
