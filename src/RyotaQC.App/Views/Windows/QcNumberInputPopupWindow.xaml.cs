using System.Windows;
using System.Windows.Input;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Popup dialog that asks the user for a number (e.g., SSD Health percentage).
/// Used for Storage Health test where the user reads the value from
/// Hard Disk Sentinel and enters it manually.
/// </summary>
public partial class QcNumberInputPopupWindow : Window
{
    /// <summary>The number entered by the user (-1 if cancelled).</summary>
    public int Value { get; private set; } = -1;

    public QcNumberInputPopupWindow(string title = "Input Hasil", string question = "Masukkan nilai:")
    {
        InitializeComponent();
        TitleText.Text = title;
        QuestionText.Text = question;
        ValueBox.Focus();
    }

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        Value = int.TryParse(ValueBox.Text, out var v) ? v : 0;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Value = -1;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows the number input popup and returns the entered value.
    /// Returns -1 if cancelled.
    /// </summary>
    public static int Show(string title = "Input Hasil", string question = "Masukkan nilai:")
    {
        var popup = new QcNumberInputPopupWindow(title, question);
        if (popup.ShowDialog() == true)
        {
            return popup.Value;
        }
        return -1;
    }
}
