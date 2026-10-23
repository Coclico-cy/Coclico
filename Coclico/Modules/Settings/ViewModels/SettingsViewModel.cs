using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings = ServiceContainer.GetRequired<SettingsService>();
    private readonly LocalizationService _loc = ServiceContainer.GetRequired<LocalizationService>();
    private readonly ThemeService _theme = ServiceContainer.GetRequired<ThemeService>();
    private readonly ProfileService _profiles = ServiceContainer.GetRequired<ProfileService>();
    private readonly InstalledProgramsService _programs = ServiceContainer.GetRequired<InstalledProgramsService>();

    private string _selectedLanguage;
    private string _customAccentHex;
    private string _backgroundMode;
    private double _backgroundOpacity;
    private double _cardOpacity;
    private double _fontSize;
    private string _wingetScope;
    private bool _launchAtStartup;
    private bool _minimizeToTray;
    private string _selectedPreset;
    private int _auditRetentionDays;
    private int _aiIdleTimeoutMinutes;
    private bool _aiEnableFloatingButton;
    private string _ollamaEndpoint;
    private AppProfile? _activeProfile;
    private List<AppProfile> _profilesList = [];
    private AppProfile? _selectedProfile;
    private string _newProfileName = string.Empty;
    private string _renameProfileName = string.Empty;
    private string _activeProfileName = string.Empty;
    private string _activeProfileInitials = "?";
    private string _cacheSizeText = "—";
    private string _cachePathText = string.Empty;

    public Func<(bool Proceed, bool DontAskAgain)>? UninstallConfirmation { get; set; }

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
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(IsLightMode));
            OnPropertyChanged(nameof(IsSystemMode));
            _theme.ApplyBackground(value);
        }
    }

    public bool IsDarkMode
    {
        get => _backgroundMode == "Dark";
        set { if (value) BackgroundMode = "Dark"; }
    }

    public bool IsLightMode
    {
        get => _backgroundMode == "Light";
        set { if (value) BackgroundMode = "Light"; }
    }

    public bool IsSystemMode
    {
        get => _backgroundMode == "System";
        set { if (value) BackgroundMode = "System"; }
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
            _settings.Save();
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
            _settings.Save();
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

    public List<AppProfile> Profiles
    {
        get => _profilesList;
        private set { _profilesList = value; OnPropertyChanged(); }
    }

    public AppProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    public string ActiveProfileName
    {
        get => _activeProfileName;
        private set { _activeProfileName = value; OnPropertyChanged(); }
    }

    public string ActiveProfileInitials
    {
        get => _activeProfileInitials;
        private set { _activeProfileInitials = value; OnPropertyChanged(); }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set { _newProfileName = value; OnPropertyChanged(); }
    }

    public string RenameProfileName
    {
        get => _renameProfileName;
        set { _renameProfileName = value; OnPropertyChanged(); }
    }

    public string CacheSizeText
    {
        get => _cacheSizeText;
        private set { _cacheSizeText = value; OnPropertyChanged(); }
    }

    public string CachePathText
    {
        get => _cachePathText;
        private set { _cachePathText = value; OnPropertyChanged(); }
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
        _wingetScope = s.WingetScope;
        _selectedPreset = s.ThemePreset;
        _launchAtStartup = s.LaunchAtStartup;
        _minimizeToTray = s.MinimizeToTray;
        _auditRetentionDays = s.AuditRetentionDays;
        _aiIdleTimeoutMinutes = s.AiIdleTimeoutMinutes;
        _aiEnableFloatingButton = s.AiEnableFloatingButton;
        _ollamaEndpoint = s.OllamaEndpoint;
    }

    private static string L(string key)
        => Application.Current?.TryFindResource(key) as string
           ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key)
           ?? key;

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

    public void RefreshProfiles()
    {
        List<AppProfile> profiles = _profiles.GetAllProfiles();
        AppProfile? active = _activeProfile ?? profiles.FirstOrDefault();
        Profiles = profiles;
        ActiveProfileName = active != null ? active.Name : L("Settings_NoProfile");
        ActiveProfileInitials = active != null ? active.AvatarInitials : "?";
    }

    [RelayCommand]
    private void ActivateProfile(AppProfile? profile)
    {
        if (profile == null)
        {
            return;
        }

        _activeProfile = profile;
        _settings.ApplyFrom(profile.Settings);
        RefreshProfiles();
        ToastService.Show($"Profil \"{profile.Name}\" activ\u00e9.");
    }

    [RelayCommand]
    private void CreateProfile()
    {
        string name = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ToastService.ShowError(L("Settings_EnterProfileName"));
            return;
        }

        var profile = new AppProfile
        {
            Name = name,
            Description = string.Empty,
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            Settings = JsonSerializer.Deserialize<AppSettings>(
                JsonSerializer.Serialize(_settings.Settings)) ?? new AppSettings(),
            Categories = _programs.GetCategories(),
            FilterGroups = _programs.GetFilterGroups()
        };

        _profiles.Save(profile);
        NewProfileName = string.Empty;
        RefreshProfiles();
        ToastService.Show(string.Format(L("Settings_ProfileCreated"), name));
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is AppProfile profile)
        {
            _profiles.Delete(profile.Name);
            if (_activeProfile?.Name == profile.Name)
            {
                _activeProfile = null;
            }

            RefreshProfiles();
            ToastService.Show(string.Format(L("Settings_ProfileDeleted"), profile.Name));
        }
        else
        {
            ToastService.ShowInfo(L("Settings_SelectProfileToDelete"));
        }
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile is not AppProfile profile)
        {
            ToastService.ShowInfo(L("Settings_SelectProfileToRename"));
            return;
        }

        string newName = RenameProfileName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ToastService.ShowError(L("Settings_EnterNewName"));
            return;
        }

        string oldName = profile.Name;
        _profiles.Rename(oldName, newName);

        if (_activeProfile?.Name == oldName)
        {
            _activeProfile = _profiles.Load(newName);
        }

        RenameProfileName = string.Empty;
        RefreshProfiles();
        ToastService.Show(string.Format(L("Settings_ProfileRenamed"), newName));
    }

    public void RefreshCacheInfo()
        => AsyncEventSafe.Run(RefreshCacheInfoAsync, "SettingsViewModel.RefreshCacheInfo");

    private async Task RefreshCacheInfoAsync()
    {
        try
        {
            long bytes = await Task.Run(() =>
                ServiceContainer.GetRequired<ICacheService>().GetCacheSizeBytes()).ConfigureAwait(true);

            bool isEn = _loc.CurrentLanguage == "en";
            CacheSizeText = bytes >= 1_048_576
                ? $"{bytes / 1_048_576.0:F1} {(isEn ? "MB" : "Mo")}"
                : $"{bytes / 1024.0:F1} {(isEn ? "KB" : "Ko")}";

            CachePathText = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "cache");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SettingsViewModel.RefreshCacheInfo");
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            await ServiceContainer.GetRequired<ICacheService>().ClearAsync().ConfigureAwait(true);
            RefreshCacheInfo();
            ToastService.Show(L("Settings_CacheCleared"));
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SettingsViewModel.ClearCache");
        }
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache");
        _ = Directory.CreateDirectory(dir);
        _ = Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private void Uninstall()
    {
        try
        {
            bool skipConfirmation = _settings.Settings.SkipUninstallConfirmation;

            if (!skipConfirmation)
            {
                (bool proceed, bool dontAskAgain) = UninstallConfirmation != null
                    ? UninstallConfirmation()
                    : (true, false);

                if (!proceed)
                {
                    return;
                }

                if (dontAskAgain)
                {
                    _settings.Settings.SkipUninstallConfirmation = true;
                    _settings.Save();
                }
            }

            string? installerPath = null;
            string localSetupName = $"Coclico-{UpdateCheckService.GetCurrentVersion()}.exe";

            var exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exePath))
            {
                var exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var customInstallerPath = Path.Combine(exeDir, "Coclico.Installer.exe");
                    if (File.Exists(customInstallerPath))
                    {
                        installerPath = customInstallerPath;
                    }
                    else if (File.Exists(Path.Combine(exeDir, localSetupName)))
                    {
                        installerPath = Path.Combine(exeDir, localSetupName);
                    }
                    var uninstallDir = Path.Combine(exeDir, "Uninstall");
                    if (Directory.Exists(uninstallDir))
                    {
                        var uninstallerInDir = Path.Combine(uninstallDir, "Coclico.Installer.exe");
                        if (File.Exists(uninstallerInDir))
                        {
                            installerPath = uninstallerInDir;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(installerPath))
            {
                var exePath2 = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exePath2))
                {
                    var exeDir = Path.GetDirectoryName(exePath2);
                    if (!string.IsNullOrEmpty(exeDir))
                    {
                        var artifactsPath = Path.Combine(exeDir, "..", "..", "artifacts", "installer", localSetupName);
                        if (File.Exists(artifactsPath))
                        {
                            installerPath = artifactsPath;
                        }
                        var customInstallerArtifacts = Path.Combine(exeDir, "..", "..", "Coclico.Installer", "bin", "Release", "Coclico.Installer.exe");
                        if (File.Exists(customInstallerArtifacts))
                        {
                            installerPath = customInstallerArtifacts;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(installerPath))
            {
                var programFilesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Coclico", localSetupName);
                if (File.Exists(programFilesPath))
                {
                    installerPath = programFilesPath;
                }
                var customInstallerProgramFiles = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Coclico", "Coclico.Installer.exe");
                if (File.Exists(customInstallerProgramFiles))
                {
                    installerPath = customInstallerProgramFiles;
                }
            }

            if (string.IsNullOrEmpty(installerPath))
            {
                var localAppDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Coclico", "Uninstall", "Coclico.Installer.exe");
                if (File.Exists(localAppDataPath))
                {
                    installerPath = localAppDataPath;
                }
            }

            if (!string.IsNullOrEmpty(installerPath))
            {
                Process.Start(new ProcessStartInfo(installerPath, "--uninstall")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            else
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Coclico");
                    if (key != null)
                    {
                        var uninstallString = key.GetValue("UninstallString") as string;
                        if (!string.IsNullOrEmpty(uninstallString))
                        {
                            Process.Start(new ProcessStartInfo(uninstallString)
                            {
                                UseShellExecute = true,
                                Verb = "runas"
                            });
                            return;
                        }
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "SettingsViewModel.Uninstall.RegistryFallback"); }

                ToastService.ShowError(L("Settings_Uninstall_Failed"));
            }
        }
        catch (Exception ex)
        {
            ToastService.ShowError($"{L("Settings_Uninstall_Error")}: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
