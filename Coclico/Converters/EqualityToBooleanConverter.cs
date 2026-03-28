#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Coclico.Converters;

public class EqualityToBooleanConverter : IMultiValueConverter
{
    public object Convert(object?[]? values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return false;

        return object.Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
