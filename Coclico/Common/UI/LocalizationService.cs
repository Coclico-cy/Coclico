using System.Windows;

namespace Coclico.Services;

public class LocalizationService
{
    private ResourceDictionary? _currentDict;
    private ResourceDictionary? _fallbackDict;

    public string CurrentLanguage { get; private set; } = "fr";

    public event Action<string>? LanguageChanged;

    public LocalizationService()
    {
        CurrentLanguage = "fr";
        try
        {
            _currentDict = new ResourceDictionary { Source = BuildUri("fr") };
        }
        catch (Exception exSwallow)
        {
            LoggingService.LogException(exSwallow, "LocalizationService.CtorLoad");
        }
    }

    public void SetLanguage(string? langCode)
    {
        try
        {
            string targetCode = langCode?.ToLowerInvariant() == "en" ? "en" : "fr";
            ResourceDictionary? dict = SwapOrLoad(BuildUri(targetCode));

            _currentDict = dict;
            CurrentLanguage = targetCode;

            SettingsService? settingsService = ServiceContainer.GetOptional<SettingsService>();
            if (settingsService != null)
            {
                settingsService.Settings.Language = targetCode;
                settingsService.Save();
            }

            LanguageChanged?.Invoke(targetCode);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "LocalizationService.SetLanguage");

            if (langCode != "fr" && CurrentLanguage != "fr")
            {
                try { SetLanguage("fr"); }
                catch (Exception exSwallow) { LoggingService.LogException(exSwallow, "LocalizationService.SetLanguageFallback"); }
            }
        }
    }

    public string Get(string key)
    {
        try
        {
            return Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess()
                ? Application.Current.Dispatcher.Invoke(() => GetInternal(key))
                : GetInternal(key);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "LocalizationService.Get");
        }
        return key;
    }

    private static Uri BuildUri(string langCode)
    {
        return new Uri($"/Coclico;component/Resources/Lang/{langCode}.xaml", UriKind.Relative);
    }

    private ResourceDictionary? SwapOrLoad(Uri uri)
    {
        ResourceDictionary? dict = null;

        if (Application.Current != null)
        {
            void SwapDict()
            {
                dict = new ResourceDictionary { Source = uri };

                var existingList = Application.Current.Resources.MergedDictionaries
                    .Where(d => (d.Source != null && d.Source.OriginalString.Contains("/Resources/Lang/")) || d == _currentDict)
                    .ToList();
                foreach (ResourceDictionary existing in existingList)
                {
                    _ = Application.Current.Resources.MergedDictionaries.Remove(existing);
                }

                Application.Current.Resources.MergedDictionaries.Add(dict);
            }

            if (Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(SwapDict);
            }
            else
            {
                SwapDict();
            }
        }
        else
        {
            try
            {
                dict = new ResourceDictionary { Source = uri };
            }
            catch (Exception exSwallow) { LoggingService.LogException(exSwallow, "LocalizationService.SetLanguageNoApp"); }
        }

        return dict;
    }

    private string GetInternal(string key)
    {
        if (_currentDict?.Contains(key) == true)
        {
            string? val = _currentDict[key] as string;
            if (!string.IsNullOrEmpty(val))
            {
                return val;
            }
        }

        if (Application.Current?.Resources.Contains(key) == true)
        {
            string? val = Application.Current.Resources[key] as string;
            if (!string.IsNullOrEmpty(val))
            {
                return val;
            }
        }

        if (CurrentLanguage != "fr")
        {
            try
            {
                _fallbackDict ??= new ResourceDictionary { Source = BuildUri("fr") };
                if (_fallbackDict.Contains(key))
                {
                    string? fallbackVal = _fallbackDict[key] as string;
                    if (!string.IsNullOrEmpty(fallbackVal))
                    {
                        return fallbackVal;
                    }
                }
            }
            catch (Exception exSwallow) { LoggingService.LogException(exSwallow, "LocalizationService.FallbackLoad"); }
        }

        return key;
    }
}