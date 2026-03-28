#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Coclico.Services;

namespace Coclico.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings = ServiceContainer.GetRequired<SettingsService>();
    private readonly LocalizationService _loc = ServiceContainer.GetRequired<LocalizationService>();
    private readonly ThemeService _theme = ServiceContainer.GetRequired<ThemeService>();

    private string _selectedLanguage;
    private string _customAccentHex;
    private string _backgroundMode;
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

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            _loc.SetLanguage(value);
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
            if (_backgroundMode == value) return;
            _backgroundMode = value;
            OnPropertyChanged();
            _theme.ApplyBackground(value);
        }
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set
        {
            _cardOpacity = value;
            OnPropertyChanged();
            _theme.ApplyCardOpacity(value);
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

    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            _sidebarWidth = value;
            OnPropertyChanged();
            _settings.Settings.SidebarWidth = value;
            (Application.Current.MainWindow as MainWindow)?.SetSidebarWidth(value);
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
            if (_launchAtStartup == value) return;
            _launchAtStartup = value;
            OnPropertyChanged();
            _settings.Settings.LaunchAtStartup = value;
            if (value) _settings.EnableAutostart(); else _settings.DisableAutostart();
            _settings.Save();
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value) return;
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
            if (_autoPatcherAuditOnly == value) return;
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
            var clamped = Math.Clamp(value, 7, 3650);
            if (_auditRetentionDays == clamped) return;
            _auditRetentionDays = clamped;
            OnPropertyChanged();
            _settings.Settings.AuditRetentionDays = clamped;
            _settings.Save();
        }
    }

    public SettingsViewModel()
    {
        var s = _settings.Settings;
        _selectedLanguage = s.Language;
        _customAccentHex = s.AccentColor;
        _backgroundMode = s.BackgroundMode;
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
    }

    public void ApplyPreset(string preset)
    {
        SelectedPreset = preset;
        _theme.ApplyPreset(preset);
        CustomAccentHex = _settings.Settings.AccentColor;
    }

    public void ApplyCustomAccent()
    {
        _theme.ApplyAccentColor(CustomAccentHex);
        SelectedPreset = "Custom";
        _settings.Settings.ThemePreset = "Custom";
        _settings.Save();
    }

    public void SaveAll() => _settings.Save();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
