using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Coclico.Services;

namespace Coclico.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settings = SettingsService.Instance;
        private readonly LocalizationService _loc    = LocalizationService.Instance;
        private readonly ThemeService _theme          = ThemeService.Instance;

        private string _selectedLanguage;

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

        private string _customAccentHex;

        public string CustomAccentHex
        {
            get => _customAccentHex;
            set { _customAccentHex = value; OnPropertyChanged(); }
        }

        private string _backgroundMode;

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

        private double _cardOpacity;

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

        private double _fontSize;

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

        private bool _compactMode;

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

        private double _sidebarWidth;

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

        private string _wingetScope;

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

        private bool _launchAtStartup;

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

        private bool _minimizeToTray;

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

        private string _selectedPreset;

        public string SelectedPreset
        {
            get => _selectedPreset;
            set { _selectedPreset = value; OnPropertyChanged(); }
        }

        public SettingsViewModel()
        {
            var s = _settings.Settings;
            _selectedLanguage = s.Language;
            _customAccentHex  = s.AccentColor;
            _backgroundMode   = s.BackgroundMode;
            _cardOpacity      = s.CardOpacity;
            _fontSize         = s.FontSize;
            _compactMode      = s.CompactMode;
            _sidebarWidth     = s.SidebarWidth;
            _wingetScope      = s.WingetScope;
            _selectedPreset   = s.ThemePreset;
            _launchAtStartup  = s.LaunchAtStartup;
            _minimizeToTray   = s.MinimizeToTray;
        }

        public void ApplyPreset(string preset)
        {
            SelectedPreset  = preset;
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
}
