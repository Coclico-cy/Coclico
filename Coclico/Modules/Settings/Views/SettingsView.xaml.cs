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

    public SettingsView()
    {
        _vm = ServiceContainer.GetRequired<SettingsViewModel>();
        InitializeComponent();
        DataContext = _vm;
        _vm.UninstallConfirmation = ShowUninstallConfirmation;

        ShowPanel(PanelAppearance);
        _vm.RefreshProfiles();
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
                _vm.RefreshProfiles();
                break;
            case "Cache":
                ShowPanel(PanelCache);
                _vm.RefreshCacheInfo();
                break;
            case "Autonomous": ShowPanel(PanelAutonomous); break;
        }
    }

    private void BtnTestMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.MinimizeToTray)
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

    private (bool Proceed, bool DontAskAgain) ShowUninstallConfirmation()
    {
        var confirmationDialog = new ConfirmationDialog(
            L("Settings_UninstallConfirm_Title"),
            L("Settings_UninstallConfirm_Message"),
            L("Settings_DontAskAgain"));

        bool? result = confirmationDialog.ShowDialog();
        return (result == true, confirmationDialog.DontAskAgain);
    }

    private async void BtnExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter les réglages Coclico",
            Filter = "Fichier de réglages Coclico (*.json)|*.json",
            FileName = $"coclico-reglages-{DateTime.Now:yyyy-MM-dd}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        bool ok = await ServiceContainer.GetRequired<SettingsService>().ExportToAsync(dialog.FileName);
        if (ok)
        {
            ToastService.ShowInfo("Réglages exportés avec succès.");
        }
        else
        {
            ToastService.ShowWarning("L'export a échoué — consultez les journaux de Coclico.");
        }
    }

    private async void BtnImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer des réglages Coclico",
            Filter = "Fichier de réglages Coclico (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        bool ok = await ServiceContainer.GetRequired<SettingsService>().ImportFromAsync(dialog.FileName);
        if (ok)
        {
            ToastService.ShowInfo("Réglages importés. Redémarrez Coclico pour que tout s'applique pleinement.");
        }
        else
        {
            ToastService.ShowWarning("Import impossible : fichier introuvable ou invalide (consultez les journaux).");
        }
    }
}
