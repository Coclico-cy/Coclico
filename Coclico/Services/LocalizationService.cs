#nullable enable
using System;
using System.Windows;

namespace Coclico.Services;

public class LocalizationService
{
    private ResourceDictionary? _currentDict;

    public string CurrentLanguage { get; private set; } = "fr";

    public void SetLanguage(string langCode)
    {
        try
        {
            var uri = new Uri($"/Coclico;component/Resources/Lang/{langCode}.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };

            if (_currentDict != null)
                Application.Current.Resources.MergedDictionaries.Remove(_currentDict);

            Application.Current.Resources.MergedDictionaries.Add(dict);
            _currentDict = dict;
            CurrentLanguage = langCode;

            ServiceContainer.GetRequired<SettingsService>().Settings.Language = langCode;
            ServiceContainer.GetRequired<SettingsService>().Save();
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
