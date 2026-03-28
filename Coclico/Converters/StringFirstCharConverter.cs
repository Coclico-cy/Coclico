#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace Coclico.Converters;

[ValueConversion(typeof(string), typeof(string))]
public class StringFirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
            return s[0].ToString().ToUpperInvariant();
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
