using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RyotaQC.App.Controls;

public partial class HomeTile : UserControl
{
    private DispatcherTimer? _singleClickTimer;

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(HomeTile), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(HomeTile), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectCommandParameterProperty = DependencyProperty.Register(
        nameof(SelectCommandParameter), typeof(object), typeof(HomeTile), new PropertyMetadata(null));

    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(
        nameof(OpenCommand), typeof(ICommand), typeof(HomeTile), new PropertyMetadata(null));

    public static readonly DependencyProperty OpenCommandParameterProperty = DependencyProperty.Register(
        nameof(OpenCommandParameter), typeof(object), typeof(HomeTile), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(HomeTile),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty IsBlockedProperty = DependencyProperty.Register(
        nameof(IsBlocked), typeof(bool), typeof(HomeTile),
        new PropertyMetadata(false, OnIsBlockedChanged));

    public HomeTile()
    {
        InitializeComponent();
        UseLayoutRounding = true;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public object? SelectCommandParameter
    {
        get => GetValue(SelectCommandParameterProperty);
        set => SetValue(SelectCommandParameterProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    public object? OpenCommandParameter
    {
        get => GetValue(OpenCommandParameterProperty);
        set => SetValue(OpenCommandParameterProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsBlocked
    {
        get => (bool)GetValue(IsBlockedProperty);
        set => SetValue(IsBlockedProperty, value);
    }

    private static void OnIsBlockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HomeTile tile)
        {
            return;
        }

        var blocked = (bool)e.NewValue;
        tile.DangerOverlay.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        tile.Cursor = blocked ? Cursors.No : Cursors.Hand;
    }

    /// <summary>
    /// Single click/touch selects the menu AND opens it immediately.
    /// This satisfies the requirement: clicking or touching a menu opens
    /// THAT menu regardless of what was previously selected.
    /// Arrow keys still browse without opening; Enter opens the selected.
    /// </summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();

        if (IsBlocked)
        {
            AnimateBlockedShake();
            return;
        }

        // Any click (single or double) selects and opens THIS specific menu.
        _singleClickTimer?.Stop();
        Execute(SelectCommand, SelectCommandParameter);
        Execute(OpenCommand, OpenCommandParameter);
    }

    private void AnimateBlockedShake()
    {
        var shake = new DoubleAnimation
        {
            From = -5,
            To = 5,
            Duration = TimeSpan.FromMilliseconds(60),
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };

        var translate = new TranslateTransform();
        RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private void OnSingleClickTimerTick(object? sender, EventArgs e)
    {
        _singleClickTimer?.Stop();
        _singleClickTimer = null;
    }

    private static void Execute(ICommand? command, object? parameter = null)
    {
        if (command is null)
        {
            return;
        }

        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HomeTile tile)
        {
            return;
        }

        var selected = (bool)e.NewValue;

        if (!tile.IsLoaded)
        {
            tile.ApplyVisualState(selected, animate: false);
            tile.Loaded += (_, _) => tile.ApplyVisualState(selected, animate: true);
            return;
        }

        tile.ApplyVisualState(selected, animate: true);
    }

    public void ApplyVisualState(bool selected, bool animate)
    {
        var targetScale = selected ? 1.25 : 1.0;
        var targetBorder = selected ? 2.4 : 1.6;
        var targetGlow = selected ? 0.55 : 0.0;
        var targetBorderBrush = selected
            ? (Brush)new SolidColorBrush(Color.FromRgb(0x4C, 0xA7, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

        var duration = TimeSpan.FromMilliseconds(animate ? 220 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        TileScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(targetScale, duration) { EasingFunction = ease });
        TileScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(targetScale, duration) { EasingFunction = ease });
        GlowBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(targetGlow, duration) { EasingFunction = ease });
        GlowBorder.BeginAnimation(Border.BorderThicknessProperty,
            new ThicknessAnimation(new Thickness(targetBorder), duration));
        GlowBorder.BorderBrush = targetBorderBrush;
    }
}
