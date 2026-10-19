using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly DashboardPageViewModel _viewModel;
    private ContentControl _heroFront;
    private ContentControl _heroBack;
    private double _lastIndex = -1;

    public DashboardPage(DashboardPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _heroFront = HeroFront;
        _heroBack = HeroBack;
        _heroFront.ContentTemplate = (DataTemplate)FindResource("HeroTemplate");
        _heroBack.ContentTemplate = (DataTemplate)FindResource("HeroTemplate");

        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void Page_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentTile is not null)
        {
            _lastIndex = _viewModel.SelectedTileIndex;
            _heroFront.Content = _viewModel.CurrentTile;
            _heroFront.Opacity = 1;
        }

        ScrollSelectedIntoView();
    }

    private void TileList_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleDashboardKey(e.Key))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles left/right/enter navigation for the tile carousel. Return true
    /// when the key is consumed so the host window does not re-route it.
    /// </summary>
    public bool TryHandleDashboardKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                if (_viewModel.PrevTileCommand.CanExecute(null))
                {
                    _viewModel.PrevTileCommand.Execute(null);
                    return true;
                }
                return false;

            case Key.Right:
                if (_viewModel.NextTileCommand.CanExecute(null))
                {
                    _viewModel.NextTileCommand.Execute(null);
                    return true;
                }
                return false;

            case Key.Enter:
            case Key.Space:
                if (_viewModel.OpenCurrentTileCommand.CanExecute(null))
                {
                    _viewModel.OpenCurrentTileCommand.Execute(null);
                    return true;
                }
                return false;

            default:
                return false;
        }
    }


    private void TileList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScrollSelectedIntoView();
        AnimateHeroIfNeeded();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardPageViewModel.CurrentTile))
        {
            return;
        }

        AnimateHeroIfNeeded();
    }

    private void AnimateHeroIfNeeded()
    {
        if (_viewModel.CurrentTile is null)
        {
            return;
        }

        var newIndex = _viewModel.SelectedTileIndex;
        if (newIndex < 0 || newIndex >= _viewModel.Tiles.Count)
        {
            return;
        }

        var direction = newIndex >= _lastIndex ? 1.0 : -1.0;
        _lastIndex = newIndex;

        var incoming = _heroBack;
        var outgoing = _heroFront;
        incoming.Content = _viewModel.CurrentTile;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        const double durationMs = 420;
        const double travel = 60.0;
        var duration = TimeSpan.FromMilliseconds(durationMs);

        incoming.Opacity = 1;
        incoming.RenderTransformOrigin = new Point(0.5, 0.5);
        incoming.RenderTransform = new TranslateTransform { X = direction * travel, Y = 0 };

        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = ease
        });
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = direction * travel,
            To = 0,
            Duration = duration,
            EasingFunction = ease
        });

        outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = duration,
            EasingFunction = ease
        });

        (_heroFront, _heroBack) = (_heroBack, _heroFront);
    }

    private void ScrollSelectedIntoView()
    {
        var item = TileList.SelectedItem;
        if (item is null || TileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
        {
            return;
        }

        container.BringIntoView();
        container.UpdateLayout();
    }
}
