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
}
