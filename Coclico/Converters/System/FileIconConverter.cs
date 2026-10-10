using System.Globalization;
using System.Windows.Data;

namespace Coclico.Converters;

[ValueConversion(typeof(string), typeof(string))]
public class FirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? trimmed = (value as string)?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "?"
            : StringInfo.GetNextTextElement(trimmed).ToUpperInvariant();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}