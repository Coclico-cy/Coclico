#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Coclico.Converters;

public class BooleanToVisibilityInvertedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool v)
        {
            return v ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility vis)
        {
            return vis != Visibility.Visible;
        }
        throw new NotSupportedException();
    }
}
