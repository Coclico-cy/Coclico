using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Coclico.Converters;

public class EqualityToBoolConverter : IMultiValueConverter, IValueConverter
{
    public object Convert(object?[]? values, Type targetType, object parameter, CultureInfo culture)
    {
        return values != null
            && values.Length >= 2
            && values[0] != DependencyProperty.UnsetValue
            && values[1] != DependencyProperty.UnsetValue
            && object.Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return Array.Empty<object>();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null
            && parameter != null
            && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            if (targetType.IsEnum)
            {
                try
                {
                    return Enum.Parse(targetType, parameter.ToString()!, true);
                }
                catch (Exception exSwallow)
                {
                    Services.LoggingService.LogException(exSwallow, "EqualityToBoolConverter.ConvertBackEnum");
                }
            }

            return parameter;
        }

        return Binding.DoNothing;
    }
}