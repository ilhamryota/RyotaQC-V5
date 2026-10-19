using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Popup dialog that asks the user for a time duration (hours + minutes).
/// Used for Battery Report and Running Test results.
/// Returns the total as a formatted string like "2J 30M" and also as
/// total minutes for threshold-based color coding.
/// </summary>
public partial class QcTimeInputPopupWindow : Window
{
    /// <summary>Formatted result string, e.g. "2J 30M".</summary>
    public string ResultText { get; private set; } = string.Empty;

    /// <summary>Total minutes entered (for threshold calculations).</summary>
    public int TotalMinutes { get; private set; }

    private readonly int _redBelowMinutes;
    private readonly int _yellowBelowMinutes;
    private readonly int _greenBelowMinutes;

    public QcTimeInputPopupWindow(
        string title = "Berapa lama hasil test?",
        string question = "Masukkan durasi hasil test:",
        int redBelowMinutes = 120,
        int yellowBelowMinutes = 180,
        int greenBelowMinutes = 240)
    {
        InitializeComponent();
        TitleText.Text = title;
        QuestionText.Text = question;
        _redBelowMinutes = redBelowMinutes;
        _yellowBelowMinutes = yellowBelowMinutes;
        _greenBelowMinutes = greenBelowMinutes;
        HoursBox.Focus();
    }

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        int hours = int.TryParse(HoursBox.Text, out var h) ? h : 0;
        int minutes = int.TryParse(MinutesBox.Text, out var m) ? m : 0;
        TotalMinutes = (hours * 60) + minutes;
        ResultText = $"{hours}J {minutes}M";
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultText = string.Empty;
        TotalMinutes = 0;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Determines the result color based on total minutes.
    /// Below red threshold = red, below yellow = yellow, above = green.
    /// </summary>
    public QcResultPopupWindow.QcResult GetResultColor()
    {
        if (TotalMinutes <= 0) return QcResultPopupWindow.QcResult.Skip;
        if (TotalMinutes < _redBelowMinutes) return QcResultPopupWindow.QcResult.Fail;
        if (TotalMinutes < _greenBelowMinutes) return QcResultPopupWindow.QcResult.Skip; // yellow zone
        return QcResultPopupWindow.QcResult.Pass;
    }

    /// <summary>
    /// Shows the time input popup and returns the formatted result.
    /// Returns empty string if cancelled.
    /// </summary>
    public static (string text, int totalMinutes) Show(
        string title = "Berapa lama hasil test?",
        string question = "Masukkan durasi hasil test:",
        int redBelowMinutes = 120,
        int yellowBelowMinutes = 180,
        int greenBelowMinutes = 240)
    {
        var popup = new QcTimeInputPopupWindow(title, question, redBelowMinutes, yellowBelowMinutes, greenBelowMinutes);
        if (popup.ShowDialog() == true)
        {
            return (popup.ResultText, popup.TotalMinutes);
        }
        return (string.Empty, 0);
    }
}
