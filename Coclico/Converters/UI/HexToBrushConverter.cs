using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Coclico.Converters;

public class HexToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> _cache = new();
    private static readonly SolidColorBrush FallbackBrush = CreateFrozenBrush(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex)
        {
            return FallbackBrush;
        }

        if (_cache.TryGetValue(hex, out SolidColorBrush? cached))
        {
            return cached;
        }

        try
        {
            var brush = CreateFrozenBrush((Color)ColorConverter.ConvertFromString(hex));
            _ = _cache.TryAdd(hex, brush);
            return brush;
        }
        catch
        {
            Services.LoggingService.LogError($"[HexToBrushConverter] Invalid hex color: {value}");
            return FallbackBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}