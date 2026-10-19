using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Fullscreen touchscreen grid test — divides the entire screen into a grid
/// of small boxes. Each box turns green when tapped and shows the tap count.
/// No title, no buttons — pure fullscreen test. Close with ESC only.
/// When all boxes are green, shows "Test Done Bang Aman" and auto-closes.
/// </summary>
public partial class TouchscreenTestWindow : Window
{
    private const int GridCols = 12;
    private const int GridRows = 8;
    private readonly Border[,] _cells;
    private readonly int[,] _tapCounts;
    private int _tappedCells;
    private readonly DispatcherTimer? _autoCloseTimer;

    /// <summary>True if all cells were tapped (all green) when closed.</summary>
    public bool AllTapped { get; private set; }

    public TouchscreenTestWindow()
    {
        InitializeComponent();
        _cells = new Border[GridCols, GridRows];
        _tapCounts = new int[GridCols, GridRows];

        BuildGrid();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            AllTapped = true;
            Close();
        };
    }

    private void BuildGrid()
    {
        var grid = new Grid();

        for (var c = 0; c < GridCols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < GridRows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (var c = 0; c < GridCols; c++)
        {
            for (var r = 0; r < GridRows; r++)
            {
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0x12, 0x16, 0x25)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x5A, 0x87, 0xE6)),
                    BorderThickness = new Thickness(0.8),
                    Margin = new Thickness(1.5),
                    Cursor = Cursors.Hand,
                    Tag = (c, r)
                };

                var label = new TextBlock
                {
                    Text = "",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cell.Child = label;

                cell.TouchDown += Cell_OnTapped;
                cell.PreviewMouseLeftButtonDown += Cell_OnTapped;

                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r);
                grid.Children.Add(cell);
                _cells[c, r] = cell;
            }
        }

        TestGridContainer.Children.Add(grid);
    }

    private void Cell_OnTapped(object? sender, InputEventArgs e)
    {
        if (sender is not Border cell) return;
        if (cell.Tag is not ValueTuple<int, int> tag) return;

        var (c, r) = tag;
        _tapCounts[c, r]++;

        if (_tapCounts[c, r] == 1)
            _tappedCells++;

        // Update cell: green background + tap count
        cell.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x7A, 0x4B));
        cell.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x39, 0xC8, 0x6B));

        if (cell.Child is TextBlock label)
        {
            label.Text = _tapCounts[c, r].ToString();
            // If tap count > 1, show yellow text (potential ghost/spam touch)
            if (_tapCounts[c, r] > 1)
                label.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xE0, 0xA8));
        }

        e.Handled = true;

        // Check if all cells are tapped
        if (_tappedCells >= GridCols * GridRows)
        {
            DoneOverlay.Visibility = Visibility.Visible;
            _autoCloseTimer?.Start();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AllTapped = _tappedCells >= GridCols * GridRows;
            Close();
        }
    }
}
