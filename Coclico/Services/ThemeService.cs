using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Wpf.Ui.Appearance;

namespace Coclico.Services
{
    public class ThemeService : IDisposable
    {
        private static ThemeService? _instance;

        public static ThemeService Instance => _instance ??= new ThemeService();

        private ThemeService()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (!string.Equals(SettingsService.Instance.Settings.BackgroundMode, "System",
                    StringComparison.OrdinalIgnoreCase)) return;

            Application.Current?.Dispatcher.InvokeAsync(ApplyCurrentSettings);
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            GC.SuppressFinalize(this);
        }

        public void ApplyCurrentSettings()
        {
            var settings = SettingsService.Instance.Settings;

            var bgMode = settings.BackgroundMode ?? "UltraDark";
            if (string.Equals(bgMode, "System", StringComparison.OrdinalIgnoreCase))
            {
                bgMode = IsSystemUsingLightTheme() ? "Light" : "Dark";
            }

            ApplicationThemeManager.Apply(string.Equals(bgMode, "Light", StringComparison.OrdinalIgnoreCase)
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark);

            ApplyBackground(settings.BackgroundMode!);
            ApplyAccentColor(settings.AccentColor);
            ApplyCardOpacity(settings.CardOpacity);
            ApplyCompactMode(settings.CompactMode);
            Application.Current.Resources["GlobalFontSize"] = settings.FontSize;
        }

        public void ApplyPreset(string preset)
        {
            var color = preset switch
            {
                "Cyan"    => "#0EA5E9",
                "Emerald" => "#10B981",
                "Rose"    => "#F43F5E",
                "Amber"   => "#F59E0B",
                _         => "#6366F1"
            };

            SettingsService.Instance.Settings.ThemePreset = preset;
            SettingsService.Instance.Settings.AccentColor = color;
            ApplyAccentColor(color);
            SettingsService.Instance.Save();
        }

        public void ApplyAccentColor(string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();

                byte r = (byte)Math.Min(255, color.R + 40);
                byte g = (byte)Math.Min(255, color.G + 40);
                byte b = (byte)Math.Min(255, color.B + 40);
                var lighterColor = Color.FromArgb(color.A, r, g, b);
                var lighterBrush = new SolidColorBrush(lighterColor);
                lighterBrush.Freeze();

                var gradient = new LinearGradientBrush();
                gradient.StartPoint = new Point(0, 0);
                gradient.EndPoint = new Point(1, 1);
                gradient.GradientStops.Add(new GradientStop(lighterColor, 0));
                gradient.GradientStops.Add(new GradientStop(color, 1));
                gradient.Freeze();

                Application.Current.Resources["AccentPrimary"]   = color;
                Application.Current.Resources["AccentSecondary"] = lighterColor;
                Application.Current.Resources["PrimaryBrush"]    = brush;
                Application.Current.Resources["SecondaryBrush"]  = lighterBrush;
                Application.Current.Resources["PrimaryGradient"] = gradient;

                ApplicationAccentColorManager.Apply(color);

                SettingsService.Instance.Settings.AccentColor = hexColor;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ThemeService.ApplyAccentColor");
            }
        }

        public void ApplyBackground(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) mode = "UltraDark";

            string preferenceToSave = mode;

            if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            {
                mode = IsSystemUsingLightTheme() ? "Light" : "Dark";
            }

            var (bgHex, cardHex, textPrimary, textMuted) = mode switch
            {
                "Light"    => ("#FFFFFFFF", "#FFF3F4F6", "#0F172A", "#475569"),
                "Dark"     => ("#0F0F14", "#1A1A22", "#E8E8F4", "#8080A8"),
                "Midnight" => ("#0A0A14", "#12121C", "#E8E8F4", "#8080A8"),
                _           => ("#08080A", "#10101E", "#E8E8F4", "#8080A8")
            };

            try
            {
                var bgColor   = (Color)ColorConverter.ConvertFromString(bgHex);
                var cardColor = (Color)ColorConverter.ConvertFromString(cardHex);

                var bgBrush   = new SolidColorBrush(bgColor);   bgBrush.Freeze();
                var cardBrush = new SolidColorBrush(cardColor); cardBrush.Freeze();

                Application.Current.Resources["BgBaseBrush"]  = bgBrush;
                Application.Current.Resources["BgCardBrush"]  = cardBrush;
                Application.Current.Resources["BgDarkColor"]  = bgColor;
                Application.Current.Resources["BgCardColor"]  = cardColor;
                Application.Current.Resources["BgDark"]       = bgBrush;
                Application.Current.Resources["BgCard"]       = cardBrush;

                var textPrimaryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textPrimary));
                textPrimaryBrush.Freeze();
                var textMutedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(textMuted));
                textMutedBrush.Freeze();

                Application.Current.Resources["TextPrimaryBrush"] = textPrimaryBrush;
                Application.Current.Resources["TextMutedBrush"]   = textMutedBrush;

                SettingsService.Instance.Settings.BackgroundMode = preferenceToSave;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ThemeService.ApplyBackground");
            }
        }

        public void ApplyCardOpacity(double opacity)
        {
            try
            {
                opacity = Math.Clamp(opacity, 0.01, 0.40);

                var baseR = 16; var baseG = 16; var baseB = 30;
                var r = (byte)Math.Round(opacity * 255 + (1 - opacity) * baseR);
                var g = (byte)Math.Round(opacity * 255 + (1 - opacity) * baseG);
                var b = (byte)Math.Round(opacity * 255 + (1 - opacity) * baseB);

                var cardColor = Color.FromRgb(r, g, b);
                var cardBrush = new SolidColorBrush(cardColor);
                cardBrush.Freeze();

                Application.Current.Resources["BgCardBrush"] = cardBrush;
                Application.Current.Resources["BgCardColor"] = cardColor;
                Application.Current.Resources["BgCard"]      = cardBrush;

                SettingsService.Instance.Settings.CardOpacity = opacity;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ThemeService.ApplyCardOpacity");
            }
        }

        public void ApplyCompactMode(bool compact)
        {
            try
            {
                Application.Current.Resources.Remove("CardPadding");
                Application.Current.Resources["CardPadding"] = compact
                    ? new Thickness(12)
                    : new Thickness(24);

                Application.Current.Resources["GlobalFontSize"] = compact
                    ? 11.5
                    : SettingsService.Instance.Settings.FontSize;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                        mainWindow.SetCompactMode(compact);
                });

                SettingsService.Instance.Settings.CompactMode = compact;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "ThemeService.ApplyCompactMode");
            }
        }

        private bool IsSystemUsingLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                if (key is null) return false;

                var value = key.GetValue("AppsUseLightTheme");
                if (value is int intVal)
                {
                    return intVal != 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
