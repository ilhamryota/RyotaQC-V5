using System.Windows;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Popup dialog that asks the user for a failure note when a QC test
/// result is "X" (Fail). The entered note is stored alongside the test
/// result and included in the PDF report.
/// </summary>
public partial class QcNotePopupWindow : Window
{
    public string Note { get; private set; } = string.Empty;

    public QcNotePopupWindow(string question = "Mengapa test ini gagal (X)?")
    {
        InitializeComponent();
        QuestionText.Text = question;
        AnswerBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Note = AnswerBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Note = string.Empty;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows the note popup and returns the entered text.
    /// Returns empty string if cancelled.
    /// </summary>
    public static string Show(string question = "Mengapa test ini gagal (X)?")
    {
        var popup = new QcNotePopupWindow(question);
        if (popup.ShowDialog() == true)
        {
            return popup.Note;
        }
        return string.Empty;
    }
}
