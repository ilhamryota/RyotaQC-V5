using System.Windows;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Reusable popup dialog for QC test results. Shows three options:
/// OK (green), - (neutral/white), X (red). Returns the selected result.
/// Used after each QC test tool is closed to capture pass/fail/skip.
/// </summary>
public partial class QcResultPopupWindow : Window
{
    public enum QcResult
    {
        None,
        Pass,    // OK (green)
        Skip,    // - (neutral)
        Fail     // X (red)
    }

    public QcResult Result { get; private set; } = QcResult.None;

    public QcResultPopupWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = QcResult.Pass;
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Result = QcResult.Skip;
        DialogResult = true;
        Close();
    }

    private void Fail_Click(object sender, RoutedEventArgs e)
    {
        Result = QcResult.Fail;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Shows the popup and waits for the user to select a result.
    /// Returns Pass, Skip, or Fail.
    /// </summary>
    public static QcResult Show(string title, string message)
    {
        var popup = new QcResultPopupWindow(title, message);
        popup.ShowDialog();
        return popup.Result;
    }
}
