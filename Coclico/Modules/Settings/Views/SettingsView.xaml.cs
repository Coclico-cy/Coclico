using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class SettingsView : UserControl
{
    private static string L(string key) => Application.Current?.TryFindResource(key) as string ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key) ?? key;
    private readonly SettingsViewModel _vm;
    private AppProfile? _activeProfile;

    public SettingsView()
    {
        _vm = ServiceContainer.GetRequired<SettingsViewModel>();
        InitializeComponent();
        DataContext = _vm;

        InitializeSavedState();
        ShowPanel(PanelAppearance);
        RefreshProfiles();
    }

    private void InitializeSavedState()
    {
        AppSettings s = ServiceContainer.GetRequired<SettingsService>().Settings;
        _vm.SidebarWidthChangeRequested += OnSidebarWidthChangeRequested;

        if (string.Equals(s.Language, "en", StringComparison.OrdinalIgnoreCase))
        {
            RbEn.IsChecked = true;
            _vm.SelectedLanguage = "en";
        }
        else
        {
            RbFr.IsChecked = true;
            _vm.SelectedLanguage = "fr";
        }

        switch (s.BackgroundMode)
        {
            case "Dark": RbDark.IsChecked = true; break;
            case "Light": RbLight.IsChecked = true; break;
            case "System": RbSystem.IsChecked = true; break;
            default: RbDark.IsChecked = true; break;
        }
    }

    private void ShowPanel(Border panel)
    {
        foreach (Border? p in new[] {
            PanelAppearance, PanelLanguage, PanelInterface,
            PanelProfiles, PanelCache, PanelAutonomous, PanelAbout })
        {
            p.Visibility = Visibility.Collapsed;
            p.Opacity = 1;
        }

        panel.Opacity = 0;
        panel.Visibility = Visibility.Visible;
        panel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (PanelAppearance == null)
        {
            return;
        }

        string? tag = null;
        if (sender is ListBoxItem item && item.Tag is string t)
        {
            tag = t;
        }

        switch (tag)
        {
            case "Appearance": ShowPanel(PanelAppearance); break;
            case "Language": ShowPanel(PanelLanguage); break;
            case "Interface": ShowPanel(PanelInterface); break;
            case "About": ShowPanel(PanelAbout); break;
            case "Profiles":
                ShowPanel(PanelProfiles);
                RefreshProfiles();
                break;
            case "Cache":
                ShowPanel(PanelCache);
                RefreshCacheInfo();
                break;
            case "Autonomous": ShowPanel(PanelAutonomous); break;
        }
    }

    private void Preset_Indigo(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPresetCommand.Execute("Indigo");
    }

    private void Preset_Cyan(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPresetCommand.Execute("Cyan");
    }

    private void Preset_Emerald(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPresetCommand.Execute("Emerald");
    }

    private void Preset_Rose(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPresetCommand.Execute("Rose");
    }

    private void Preset_Amber(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPresetCommand.Execute("Amber");
    }

    private void ApplyAccent_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyCustomAccentCommand.Execute(null);
    }

    private void BgMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender == RbDark)
        {
            _vm.BackgroundMode = "Dark";
        }
        else if (sender == RbLight)
        {
            _vm.BackgroundMode = "Light";
        }
        else if (sender == RbSystem)
        {
            _vm.BackgroundMode = "System";
        }
    }

    private void CardOpacity_Changed(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _vm.SaveAllCommand.Execute(null);
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedLanguage = sender == RbEn ? "en" : "fr";
    }

    private void FontSize_Changed(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _vm.SaveAllCommand.Execute(null);
    }

    private void SidebarWidth_Changed(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _vm.SaveAllCommand.Execute(null);
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveAllCommand.Execute(null);
    }

    private void BtnTestMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray)
        {
            ToastService.ShowInfo(
                ServiceContainer.GetRequired<LocalizationService>().Get("Settings_MinimizeToTray_Desc"));
            return;
        }

        if (Application.Current.MainWindow is MainWindow main)
        {
            main.MinimizeToTrayNow();
        }
    }

    private void RefreshProfiles()
    {
        List<AppProfile> profiles = ServiceContainer.GetRequired<ProfileService>().GetAllProfiles();
        LbProfiles.ItemsSource = profiles;

        AppProfile? active = _activeProfile ?? profiles.FirstOrDefault();
        if (active != null)
        {
            TbActiveProfileName.Text = active.Name;
            TbActiveInitials.Text = active.AvatarInitials;
        }
        else
        {
            TbActiveProfileName.Text = L("Settings_NoProfile");
            TbActiveInitials.Text = "?";
        }
    }

    private void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AppProfile profile)
        {
            _activeProfile = profile;
            ServiceContainer.GetRequired<SettingsService>().ApplyFrom(profile.Settings);
            RefreshProfiles();
            ToastService.Show($"Profil \"{profile.Name}\" activ\u00e9.");
        }
    }

    private void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtNewProfileName.Text.Trim();
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
                JsonSerializer.Serialize(ServiceContainer.GetRequired<SettingsService>().Settings)) ?? new AppSettings(),
            Categories = ServiceContainer.GetRequired<InstalledProgramsService>().GetCategories(),
            FilterGroups = ServiceContainer.GetRequired<InstalledProgramsService>().GetFilterGroups()
        };

        ServiceContainer.GetRequired<ProfileService>().Save(profile);
        TxtNewProfileName.Text = string.Empty;
        RefreshProfiles();
        ToastService.Show(string.Format(L("Settings_ProfileCreated"), name));
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LbProfiles.SelectedItem is AppProfile profile)
        {
            ServiceContainer.GetRequired<ProfileService>().Delete(profile.Name);
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

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LbProfiles.SelectedItem is not AppProfile profile)
        {
            ToastService.ShowInfo(L("Settings_SelectProfileToRename"));
            return;
        }

        string newName = TxtRenameProfile.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ToastService.ShowError(L("Settings_EnterNewName"));
            return;
        }

        string oldName = profile.Name;
        ServiceContainer.GetRequired<ProfileService>().Rename(oldName, newName);

        if (_activeProfile?.Name == oldName)
        {
            _activeProfile = ServiceContainer.GetRequired<ProfileService>().Load(newName);
        }

        TxtRenameProfile.Text = string.Empty;
        RefreshProfiles();
        ToastService.Show(string.Format(L("Settings_ProfileRenamed"), newName));
    }

    private void RefreshCacheInfo()
    {
        long bytes = ServiceContainer.GetRequired<ICacheService>().GetCacheSizeBytes();
        bool isEn = ServiceContainer.GetOptional<LocalizationService>()?.CurrentLanguage == "en";
        TbCacheSize.Text = bytes >= 1_048_576
            ? $"{bytes / 1_048_576.0:F1} {(isEn ? "MB" : "Mo")}"
            : $"{bytes / 1024.0:F1} {(isEn ? "KB" : "Ko")}";

        TbCachePath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache");
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        ServiceContainer.GetRequired<ICacheService>().Clear();
        RefreshCacheInfo();
        ToastService.Show(L("Settings_CacheCleared"));
    }

    private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "cache");
        _ = Directory.CreateDirectory(dir);
        _ = Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void OnSidebarWidthChangeRequested(object? sender, double width)
    {
        (Window.GetWindow(this) as MainWindow)?.SetSidebarWidth(width);
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = ServiceContainer.GetRequired<SettingsService>();
            bool skipConfirmation = settingsService.Settings.SkipUninstallConfirmation;

            if (!skipConfirmation)
            {
                var settings = ServiceContainer.GetRequired<SettingsService>().Settings;

                var confirmationDialog = new ConfirmationDialog(
                    L("Settings_UninstallConfirm_Title"),
                    L("Settings_UninstallConfirm_Message"),
                    L("Settings_DontAskAgain"));

                if (confirmationDialog.ShowDialog() != true)
                {
                    return;
                }

                if (confirmationDialog.DontAskAgain)
                {
                    settings.SkipUninstallConfirmation = true;
                    ServiceContainer.GetRequired<SettingsService>().Save();
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
                catch (Exception ex) { LoggingService.LogException(ex, "SettingsView.BtnUninstall_Click.RegistryFallback"); }

                ToastService.ShowError(L("Settings_Uninstall_Failed"));
            }
        }
        catch (Exception ex)
        {
            ToastService.ShowError($"{L("Settings_Uninstall_Error")}: {ex.Message}");
        }
    }
}
