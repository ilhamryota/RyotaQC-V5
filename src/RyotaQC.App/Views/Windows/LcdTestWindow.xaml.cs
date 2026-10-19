using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RyotaQC.App.Views.Windows;

public partial class LcdTestWindow : Window
{
    private readonly Brush[] _colors =
    {
        Brushes.Red,
        Brushes.Lime,
        Brushes.Blue,
        Brushes.White,
        Brushes.Black
    };

    private int _index;

    public LcdTestWindow()
    {
        InitializeComponent();
        DataContext = this;
        CurrentBrush = _colors[_index];
        Focus();
    }

    public Brush CurrentBrush { get; private set; }

    private void NextColor()
    {
        _index = (_index + 1) % _colors.Length;
        CurrentBrush = _colors[_index];
        DataContext = null;
        DataContext = this;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key is Key.Space or Key.Enter)
        {
            NextColor();
        }
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        NextColor();
    }
}
