using System;
using System.Windows;

namespace Coclico.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;

        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private ResourceDictionary? _currentDict;

        public string CurrentLanguage { get; private set; } = "fr";

        private LocalizationService() { }

        public void SetLanguage(string langCode)
        {
            try
            {
                var uri  = new Uri($"/Coclico;component/Resources/Lang/{langCode}.xaml", UriKind.Relative);
                var dict = new ResourceDictionary { Source = uri };

                if (_currentDict != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_currentDict);

                Application.Current.Resources.MergedDictionaries.Add(dict);
                _currentDict    = dict;
                CurrentLanguage = langCode;

                SettingsService.Instance.Settings.Language = langCode;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "LocalizationService.SetLanguage");

                if (langCode != "fr" && CurrentLanguage != langCode)
                {
                    try { SetLanguage("fr"); }
                    catch { }
                }
            }
        }

        public string Get(string key)
        {
            try
            {
                if (Application.Current.Resources.Contains(key))
                    return Application.Current.Resources[key] as string ?? key;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "LocalizationService.Get");
            }
            return key;
        }
    }
}
