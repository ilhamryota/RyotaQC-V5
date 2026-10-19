using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RyotaQC.App.Views.Windows;

/// <summary>
/// Popup window that shows a checklist of all QC test results.
/// Includes Save PDF and Save Notepad buttons at the bottom.
/// </summary>
public partial class QcResultsPopupWindow : Window
{
    /// <summary>Called when the user clicks "Save PDF".</summary>
    public Action? SavePdfCallback { get; set; }

    /// <summary>Called when the user clicks "Save Notepad".</summary>
    public Action? SaveNotepadCallback { get; set; }

    public QcResultsPopupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populates the results list from a collection of result rows.
    /// </summary>
    public void PopulateResults(IEnumerable<QcResultRow> rows)
    {
        ResultsList.Children.Clear();

        var rowList = rows.ToList();
        var passCount = rowList.Count(r => r.Status == "PASS");
        var failCount = rowList.Count(r => r.Status == "FAIL");
        var skipCount = rowList.Count(r => r.Status == "SKIP");
        var noResult = rowList.Count(r => string.IsNullOrEmpty(r.Status));

        SummaryText.Text = $"Total: {rowList.Count}  |  OK: {passCount}  |  -: {skipCount}  |  X: {failCount}  |  Belum: {noResult}";

        foreach (var row in rowList)
        {
            var itemBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x15, 0x1E, 0x35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x47, 0x7C, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            // Column 0: Test name
            var nameText = new TextBlock
            {
                Text = row.Name,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);

            // Column 1: Result badge
            var resultColor = row.ResultColor;
            var badge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(resultColor) && resultColor != "Transparent")
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(resultColor));
            }
            else
            {
                badge.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x44, 0x44, 0x44));
            }

            var badgeText = new TextBlock
            {
                Text = string.IsNullOrEmpty(row.ResultLabel) ? "-" : row.ResultLabel,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = badgeText;
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);

            // Column 2: Note
            var noteText = new TextBlock
            {
                Text = row.Note ?? "",
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xAE, 0xB7, 0xCA)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(noteText, 2);
            grid.Children.Add(noteText);

            itemBorder.Child = grid;
            ResultsList.Children.Add(itemBorder);
        }
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        // Close the popup first so the save dialog is not hidden behind it
        Close();
        SavePdfCallback?.Invoke();
    }

    private void SaveNotepad_Click(object sender, RoutedEventArgs e)
    {
        // Close the popup first so the save dialog is not hidden behind it
        Close();
        SaveNotepadCallback?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Shows the QC results popup with the given result rows.
    /// Optionally accepts save callbacks.
    /// </summary>
    public static void Show(IEnumerable<QcResultRow> rows, Action? savePdfCallback = null, Action? saveNotepadCallback = null)
    {
        var popup = new QcResultsPopupWindow();
        popup.PopulateResults(rows);
        popup.SavePdfCallback = savePdfCallback;
        popup.SaveNotepadCallback = saveNotepadCallback;
        popup.ShowDialog();
    }
}

/// <summary>
/// A single row of QC result data for the popup.
/// </summary>
public sealed class QcResultRow
{
    public string Name { get; init; } = "";
    public string ResultLabel { get; init; } = "";
    public string ResultColor { get; init; } = "Transparent";
    public string Status { get; init; } = "";
    public string? Note { get; init; }
}
