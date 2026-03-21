using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Coclico.Converters
{
    public class FileIconToImageSourceConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> _cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                if (_cache.TryGetValue(path, out var cached))
                    return cached;

                var bmp = ExtractIcon(path);
                if (bmp != null) return bmp;
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        public static Task PreloadAllAsync(IEnumerable<string> paths)
        {
            var list = new List<string>(paths);
            if (list.Count == 0) return Task.CompletedTask;

            var tcs = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                foreach (var path in list)
                {
                    if (string.IsNullOrEmpty(path) || _cache.ContainsKey(path)) continue;
                    ExtractIcon(path);
                }
                tcs.SetResult();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }

        private static BitmapSource? ExtractIcon(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                _cache.TryAdd(path, bitmap);
                return bitmap;
            }
            catch { return null; }
        }
    }
}
