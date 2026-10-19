using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RyotaQC.App.Services;

namespace RyotaQC.App.Views.Windows;

public partial class TouchpadTestWindow : Window
{
    private const double MovementThresholdPx = 120d;

    private Point? _lastRealPoint;
    private Point? _virtualPoint;
    private double _totalDistance;
    private int _leftClickCount;
    private int _rightClickCount;
    private int _scrollCount;

    public TouchpadTestResult Result { get; private set; }

    public TouchpadTestWindow()
    {
        InitializeComponent();

        SensitivitySlider.ValueChanged += (_, _) =>
        {
            SensitivityText.Text = $"{SensitivitySlider.Value:0}/10 (x{GetSensitivityGain():0.0})";
        };
        SensitivityText.Text = $"{SensitivitySlider.Value:0}/10 (x{GetSensitivityGain():0.0})";

        UpdateResult();
    }

    private void TestSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(TestSurface);
        if (_lastRealPoint is not null)
        {
            var rawDelta = current - _lastRealPoint.Value;
            var gain = GetSensitivityGain();

            var previousVirtual = _virtualPoint ?? _lastRealPoint.Value;
            var currentVirtual = new Point(
                Math.Clamp(previousVirtual.X + (rawDelta.X * gain), 0, Math.Max(0, TestSurface.ActualWidth - 1)),
                Math.Clamp(previousVirtual.Y + (rawDelta.Y * gain), 0, Math.Max(0, TestSurface.ActualHeight - 1)));

            var distance = (currentVirtual - previousVirtual).Length;
            _totalDistance += distance;
            MoveDistanceText.Text = $"{_totalDistance:0} px";

            PlotTrailSegment(previousVirtual, currentVirtual, Colors.DeepSkyBlue, Math.Clamp(gain * 2.0, 1.5, 6.0));
            PlotPoint(currentVirtual, Colors.DeepSkyBlue, Math.Clamp(gain * 6.0, 4, 18));
            _virtualPoint = currentVirtual;
        }
        else
        {
            _virtualPoint = current;
        }

        _lastRealPoint = current;
        HideHint();
        UpdateResult();
    }

    private void TestSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _leftClickCount++;
        LeftClickText.Text = _leftClickCount.ToString();
        PlotPoint(e.GetPosition(TestSurface), Colors.LimeGreen, 16);
        HideHint();
        UpdateResult();
    }

    private void TestSurface_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _rightClickCount++;
        RightClickText.Text = _rightClickCount.ToString();
        PlotPoint(e.GetPosition(TestSurface), Colors.OrangeRed, 16);
        HideHint();
        UpdateResult();
        e.Handled = true;
    }

    private void TestSurface_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _scrollCount += e.Delta > 0 ? 1 : -1;
        ScrollText.Text = _scrollCount.ToString();
        PlotPoint(e.GetPosition(TestSurface), Colors.Gold, 14);
        HideHint();
        UpdateResult();
        e.Handled = true;
    }

    private void TestSurface_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            e.Handled = true;
        }
    }

    private void PlotTrailSegment(Point start, Point end, Color color, double thickness)
    {
        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B)),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        TrailCanvas.Children.Add(line);
    }

    private void PlotPoint(Point position, Color color, double size)
    {
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(marker, position.X - size / 2);
        Canvas.SetTop(marker, position.Y - size / 2);
        TrailCanvas.Children.Add(marker);

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TrailCanvas.Children.Remove(marker);
        };

        timer.Start();
    }

    private void HideHint()
    {
        HintText.Visibility = Visibility.Collapsed;
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        _lastRealPoint = null;
        _virtualPoint = null;
        _totalDistance = 0;
        _leftClickCount = 0;
        _rightClickCount = 0;
        _scrollCount = 0;

        LeftClickText.Text = "0";
        RightClickText.Text = "0";
        ScrollText.Text = "0";
        MoveDistanceText.Text = "0 px";
        TrailCanvas.Children.Clear();
        HintText.Visibility = Visibility.Visible;
        UpdateResult();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateResult();
        DialogResult = Result.Passed;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        UpdateResult();
        base.OnClosing(e);
    }

    private void UpdateResult()
    {
        var hasMovement = _totalDistance >= MovementThresholdPx;
        var hasLeft = _leftClickCount > 0;
        var hasRight = _rightClickCount > 0;
        var hasScroll = _scrollCount != 0;

        Result = new TouchpadTestResult(
            HasMovement: hasMovement,
            HasLeftClick: hasLeft,
            HasRightClick: hasRight,
            HasScroll: hasScroll,
            DistancePx: _totalDistance,
            LeftClicks: _leftClickCount,
            RightClicks: _rightClickCount,
            ScrollSteps: _scrollCount);

        ResultStatusText.Text = Result.Passed
            ? "Status: OK (movement + left/right click + scroll terdeteksi)."
            : $"Status: {BuildPendingMessage(hasMovement, hasLeft, hasRight, hasScroll)}";
    }

    private static string BuildPendingMessage(bool hasMovement, bool hasLeft, bool hasRight, bool hasScroll)
    {
        var missing = new List<string>();
        if (!hasMovement)
        {
            missing.Add("movement");
        }

        if (!hasLeft)
        {
            missing.Add("left click");
        }

        if (!hasRight)
        {
            missing.Add("right click");
        }

        if (!hasScroll)
        {
            missing.Add("scroll");
        }

        return missing.Count == 0 ? "OK" : $"belum lengkap ({string.Join(", ", missing)})";
    }

    private double GetSensitivityGain()
    {
        return Math.Clamp(SensitivitySlider.Value / 5.0, 0.2, 3.0);
    }
}
