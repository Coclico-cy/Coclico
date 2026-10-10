using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings = ServiceContainer.GetRequired<SettingsService>();
    private readonly LocalizationService _loc = ServiceContainer.GetRequired<LocalizationService>();
    private readonly ThemeService _theme = ServiceContainer.GetRequired<ThemeService>();

    private string _selectedLanguage;
    private string _customAccentHex;
    private string _backgroundMode;
    private double _backgroundOpacity;
    private double _cardOpacity;
    private double _fontSize;
    private bool _compactMode;
    private double _sidebarWidth;
    private string _wingetScope;
    private bool _launchAtStartup;
    private bool _minimizeToTray;
    private string _selectedPreset;
    private bool _autoPatcherAuditOnly;
    private int _auditRetentionDays;
    private int _aiIdleTimeoutMinutes;
    private bool _aiEnableFloatingButton;
    private string _ollamaEndpoint;

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value)
            {
                return;
            }

            _selectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFrench));
            OnPropertyChanged(nameof(IsEnglish));
            _loc.SetLanguage(value);
        }
    }

    public bool IsFrench
    {
        get => SelectedLanguage == "fr";
        set
        {
            if (value)
            {
                SelectedLanguage = "fr";
            }
        }
    }

    public bool IsEnglish
    {
        get => SelectedLanguage == "en";
        set
        {
            if (value)
            {
                SelectedLanguage = "en";
            }
        }
    }

    public string CustomAccentHex
    {
        get => _customAccentHex;
        set { _customAccentHex = value; OnPropertyChanged(); }
    }

    public string BackgroundMode
    {
        get => _backgroundMode;
        set
        {
            if (_backgroundMode == value)
            {
                return;
            }

            _backgroundMode = value;
            OnPropertyChanged();
            _theme.ApplyBackground(value);
        }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            _backgroundOpacity = Math.Clamp(value, SettingsService.ThemeOpacityBounds.BackgroundMin, SettingsService.ThemeOpacityBounds.BackgroundMax);
            OnPropertyChanged();
            _theme.ApplyBackgroundOpacity(_backgroundOpacity);
        }
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set
        {
            _cardOpacity = Math.Clamp(value, SettingsService.ThemeOpacityBounds.CardMin, SettingsService.ThemeOpacityBounds.CardMax);
            OnPropertyChanged();
            _theme.ApplyCardOpacity(_cardOpacity);
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            OnPropertyChanged();
            _settings.Settings.FontSize = value;
            Application.Current.Resources["GlobalFontSize"] = value;
        }
    }

    public bool CompactMode
    {
        get => _compactMode;
        set
        {
            _compactMode = value;
            OnPropertyChanged();
            _settings.Settings.CompactMode = value;
            _settings.Save();
            _theme.ApplyCompactMode(value);
        }
    }

    public event EventHandler<double>? SidebarWidthChangeRequested;

    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            _sidebarWidth = value;
            OnPropertyChanged();
            _settings.Settings.SidebarWidth = value;
            SidebarWidthChangeRequested?.Invoke(this, value);
        }
    }

    public string WingetScope
    {
        get => _wingetScope;
        set
        {
            _wingetScope = value;
            OnPropertyChanged();
            _settings.Settings.WingetScope = value;
            _settings.Save();
        }
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (_launchAtStartup == value)
            {
                return;
            }

            _launchAtStartup = value;
            OnPropertyChanged();
            _settings.Settings.LaunchAtStartup = value;
            if (value)
            {
                _settings.EnableAutostart();
            }
            else
            {
                _settings.DisableAutostart();
            }

            _settings.Save();
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value)
            {
                return;
            }

            _minimizeToTray = value;
            OnPropertyChanged();
            _settings.Settings.MinimizeToTray = value;
            _settings.Save();
        }
    }

    public string SelectedPreset
    {
        get => _selectedPreset;
        set { _selectedPreset = value; OnPropertyChanged(); }
    }

    public bool CodePatcherAuditOnly
    {
        get => _autoPatcherAuditOnly;
        set
        {
            if (_autoPatcherAuditOnly == value)
            {
                return;
            }

            _autoPatcherAuditOnly = value;
            OnPropertyChanged();
            _settings.Settings.CodePatcherAuditOnly = value;
            _settings.Save();
        }
    }

    public int AuditRetentionDays
    {
        get => _auditRetentionDays;
        set
        {
            int clamped = Math.Clamp(value, 7, 3650);
            if (_auditRetentionDays == clamped)
            {
                return;
            }

            _auditRetentionDays = clamped;
            OnPropertyChanged();
            _settings.Settings.AuditRetentionDays = clamped;
            _settings.Save();
        }
    }

    public int AiIdleTimeoutMinutes
    {
        get => _aiIdleTimeoutMinutes;
        set
        {
            int clamped = Math.Clamp(value, 1, 120);
            if (_aiIdleTimeoutMinutes == clamped)
            {
                return;
            }

            _aiIdleTimeoutMinutes = clamped;
            OnPropertyChanged();
            _settings.Settings.AiIdleTimeoutMinutes = clamped;
            _settings.Save();
        }
    }

    public bool AiEnableFloatingButton
    {
        get => _aiEnableFloatingButton;
        set
        {
            if (_aiEnableFloatingButton == value)
            {
                return;
            }

            _aiEnableFloatingButton = value;
            OnPropertyChanged();
            _settings.Settings.AiEnableFloatingButton = value;
            _settings.NotifyFloatingButtonVisibilityChanged(value);
            _settings.Save();
        }
    }

    public string OllamaEndpoint
    {
        get => _ollamaEndpoint;
        set
        {
            if (_ollamaEndpoint == value)
            {
                return;
            }

            _ollamaEndpoint = value;
            OnPropertyChanged();
            _settings.Settings.OllamaEndpoint = value;
            _settings.Save();
        }
    }

    public SettingsViewModel(IDialogService dialogService)
    {
        AppSettings s = _settings.Settings;
        _selectedLanguage = string.Equals(s.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "fr";
        _customAccentHex = s.AccentColor;
        _backgroundMode = s.BackgroundMode;
        _backgroundOpacity = s.BackgroundOpacity;
        _cardOpacity = s.CardOpacity;
        _fontSize = s.FontSize;
        _compactMode = s.CompactMode;
        _sidebarWidth = s.SidebarWidth;
        _wingetScope = s.WingetScope;
        _selectedPreset = s.ThemePreset;
        _launchAtStartup = s.LaunchAtStartup;
        _minimizeToTray = s.MinimizeToTray;
        _autoPatcherAuditOnly = s.CodePatcherAuditOnly;
        _auditRetentionDays = s.AuditRetentionDays;
        _aiIdleTimeoutMinutes = s.AiIdleTimeoutMinutes;
        _aiEnableFloatingButton = s.AiEnableFloatingButton;
        _ollamaEndpoint = s.OllamaEndpoint;
    }

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        SelectedPreset = preset;
        _theme.ApplyPreset(preset);
        CustomAccentHex = _settings.Settings.AccentColor;
    }

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        _theme.ApplyAccentColor(CustomAccentHex);
        SelectedPreset = "Custom";
        _settings.Settings.ThemePreset = "Custom";
        _settings.Save();
    }

    [RelayCommand]
    private void SaveAll()
    {
        _settings.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
