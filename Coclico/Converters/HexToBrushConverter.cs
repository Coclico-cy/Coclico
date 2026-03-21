using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Coclico.Converters
{
    public class HexToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush FallbackBrush;

        static HexToBrushConverter()
        {
            FallbackBrush = new SolidColorBrush(Colors.Gray);
            FallbackBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                    brush.Freeze();
                    return brush;
                }
                catch { }
            }
            return FallbackBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
