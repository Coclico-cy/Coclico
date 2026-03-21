using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private readonly SettingsViewModel _vm;
        private AppProfile? _activeProfile;

        public SettingsView()
        {
            _vm = new SettingsViewModel();
            InitializeComponent();
            DataContext = _vm;

            InitializeSavedState();
            ShowPanel(PanelAppearance);
            RefreshProfiles();
        }

        private void InitializeSavedState()
        {
            var s = SettingsService.Instance.Settings;

            switch (s.Language)
            {
                case "en": RbEn.IsChecked = true; break;
                case "es": RbEs.IsChecked = true; break;
                case "de": RbDe.IsChecked = true; break;
                case "pt": RbPt.IsChecked = true; break;
                case "it": RbIt.IsChecked = true; break;
                case "ja": RbJa.IsChecked = true; break;
                case "zh": RbZh.IsChecked = true; break;
                case "ko": RbKo.IsChecked = true; break;
                case "ru": RbRu.IsChecked = true; break;
                default:   RbFr.IsChecked = true; break;
            }

            switch (s.BackgroundMode)
            {
                case "Dark":     RbDark.IsChecked   = true; break;
                case "Light":    RbLight.IsChecked  = true; break;
                case "System":   RbSystem.IsChecked = true; break;
                default:          RbDark.IsChecked   = true; break;
            }

            RbUser.IsChecked    = s.WingetScope == "user";
            RbMachine.IsChecked = s.WingetScope != "user";
        }

        private void ShowPanel(Border panel)
        {
            foreach (var p in new[] {
                PanelAppearance, PanelLanguage, PanelInterface,
                PanelInstaller, PanelProfiles, PanelCache, PanelAbout })
            {
                p.Visibility = Visibility.Collapsed;
                p.Opacity    = 1;
            }

            panel.Opacity    = 0;
            panel.Visibility = Visibility.Visible;
            panel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        private void Section_Click(object sender, RoutedEventArgs e)
        {
            if (PanelAppearance == null) return;

            string? tag = null;
            if (sender is ListBoxItem item && item.Tag is string t)
                tag = t;

            switch (tag)
            {
                case "Appearance": ShowPanel(PanelAppearance); break;
                case "Language":   ShowPanel(PanelLanguage);   break;
                case "Interface":  ShowPanel(PanelInterface);  break;
                case "Installer":  ShowPanel(PanelInstaller);  break;
                case "About":      ShowPanel(PanelAbout);      break;
                case "Profiles":
                    ShowPanel(PanelProfiles);
                    RefreshProfiles();
                    break;
                case "Cache":
                    ShowPanel(PanelCache);
                    RefreshCacheInfo();
                    break;
            }
        }

        private void Preset_Indigo(object sender, RoutedEventArgs e)  => _vm.ApplyPreset("Indigo");
        private void Preset_Cyan(object sender, RoutedEventArgs e)    => _vm.ApplyPreset("Cyan");
        private void Preset_Emerald(object sender, RoutedEventArgs e) => _vm.ApplyPreset("Emerald");
        private void Preset_Rose(object sender, RoutedEventArgs e)    => _vm.ApplyPreset("Rose");
        private void Preset_Amber(object sender, RoutedEventArgs e)   => _vm.ApplyPreset("Amber");

        private void ApplyAccent_Click(object sender, RoutedEventArgs e)
            => _vm.ApplyCustomAccent();

        private void BgMode_Click(object sender, RoutedEventArgs e)
        {
            if      (sender == RbDark)   _vm.BackgroundMode = "Dark";
            else if (sender == RbLight)  _vm.BackgroundMode = "Light";
            else if (sender == RbSystem) _vm.BackgroundMode = "System";
        }

        private void CardOpacity_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
            => _vm.SaveAll();

        private void Lang_Click(object sender, RoutedEventArgs e)
        {
            if      (sender == RbEn) _vm.SelectedLanguage = "en";
            else if (sender == RbEs) _vm.SelectedLanguage = "es";
            else if (sender == RbDe) _vm.SelectedLanguage = "de";
            else if (sender == RbPt) _vm.SelectedLanguage = "pt";
            else if (sender == RbIt) _vm.SelectedLanguage = "it";
            else if (sender == RbJa) _vm.SelectedLanguage = "ja";
            else if (sender == RbZh) _vm.SelectedLanguage = "zh";
            else if (sender == RbKo) _vm.SelectedLanguage = "ko";
            else if (sender == RbRu) _vm.SelectedLanguage = "ru";
            else                     _vm.SelectedLanguage = "fr";
        }

        private void FontSize_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
            => _vm.SaveAll();

        private void SidebarWidth_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
            => _vm.SaveAll();

        private void Compact_Click(object sender, RoutedEventArgs e)
            => _vm.SaveAll();

        private void WingetScope_Click(object sender, RoutedEventArgs e)
            => _vm.WingetScope = (sender == RbUser) ? "user" : "machine";

        private void BtnTestMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (!SettingsService.Instance.Settings.MinimizeToTray)
            {
                ToastService.ShowInfo(
                    LocalizationService.Instance.Get("Settings_MinimizeToTray_Desc"));
                return;
            }

            if (Application.Current.MainWindow is MainWindow main)
                main.MinimizeToTrayNow();
        }

        private void RefreshProfiles()
        {
            var profiles = ProfileService.Instance.GetAllProfiles();
            LbProfiles.ItemsSource = profiles;

            var active = _activeProfile ?? profiles.FirstOrDefault();
            if (active != null)
            {
                TbActiveProfileName.Text = active.Name;
                TbActiveInitials.Text    = active.AvatarInitials;
            }
            else
            {
                TbActiveProfileName.Text = "Aucun profil";
                TbActiveInitials.Text    = "?";
            }
        }

        private void ActivateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AppProfile profile)
            {
                _activeProfile = profile;
                SettingsService.Instance.ApplyFrom(profile.Settings);
                RefreshProfiles();
                ToastService.Show($"Profil \"{profile.Name}\" activ\u00e9.");
            }
        }

        private void CreateProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtNewProfileName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ToastService.ShowError("Entrez un nom de profil.");
                return;
            }

            var profile = new AppProfile
            {
                Name         = name,
                Description  = string.Empty,
                CreatedAt    = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Settings     = SettingsService.Instance.Settings,
                Categories   = InstalledProgramsService.Instance.GetCategories(),
                FilterGroups = InstalledProgramsService.Instance.GetFilterGroups()
            };

            ProfileService.Instance.Save(profile);
            TxtNewProfileName.Text = string.Empty;
            RefreshProfiles();
            ToastService.Show($"Profil \"{name}\" cr\u00e9\u00e9.");
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (LbProfiles.SelectedItem is AppProfile profile)
            {
                ProfileService.Instance.Delete(profile.Name);
                if (_activeProfile?.Name == profile.Name)
                    _activeProfile = null;
                RefreshProfiles();
                ToastService.Show($"Profil \"{profile.Name}\" supprim\u00e9.");
            }
            else
            {
                ToastService.ShowInfo("S\u00e9lectionnez un profil \u00e0 supprimer.");
            }
        }

        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (LbProfiles.SelectedItem is not AppProfile profile)
            {
                ToastService.ShowInfo("S\u00e9lectionnez un profil \u00e0 renommer.");
                return;
            }

            var newName = TxtRenameProfile.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                ToastService.ShowError("Entrez le nouveau nom.");
                return;
            }

            var oldName = profile.Name;
            ProfileService.Instance.Rename(oldName, newName);

            if (_activeProfile?.Name == oldName)
                _activeProfile = ProfileService.Instance.Load(newName);

            TxtRenameProfile.Text = string.Empty;
            RefreshProfiles();
            ToastService.Show($"Profil renomm\u00e9 en \"{newName}\".");
        }

        private void RefreshCacheInfo()
        {
            var bytes = CacheService.Instance.GetCacheSizeBytes();
            TbCacheSize.Text = bytes >= 1_048_576
                ? $"{bytes / 1_048_576.0:F1} Mo"
                : $"{bytes / 1024.0:F1} Ko";

            TbCachePath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "cache");
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            CacheService.Instance.Clear();
            RefreshCacheInfo();
            ToastService.Show("Cache vid\u00e9 avec succ\u00e8s.");
        }

        private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "cache");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
    }
}
