using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RyotaQC.App.Converters;

/// <summary>
/// false -> Visible, true -> Collapsed. Inverse of BooleanToVisibilityConverter.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value as bool? ?? false;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
