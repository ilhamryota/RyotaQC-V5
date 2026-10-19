using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RyotaQC.App.Converters;

/// <summary>
/// int 0 -> Visible, anything else -> Collapsed. Used for empty-state placeholders.
/// </summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value switch
        {
            int i => i == 0,
            long l => l == 0,
            _ => true
        };

        return isZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
