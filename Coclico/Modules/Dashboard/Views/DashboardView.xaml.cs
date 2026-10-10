using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Coclico.Models;
using Coclico.Services;
using Coclico.ViewModels;
using Microsoft.Win32;

namespace Coclico.Views;

public partial class DashboardView : UserControl
{
    private readonly Dictionary<string, FrameworkElement> _sectionsMap = [];
    private readonly DashboardViewModel? _vm;

    private DashboardViewModel? CurrentViewModel => _vm ?? DataContext as DashboardViewModel;

    public DashboardView()
    {
        InitializeComponent();
        _vm = ServiceContainer.GetRequired<DashboardViewModel>();
        DataContext = _vm;

        _sectionsMap["Header"] = SecHeader;
        _sectionsMap["Metrics"] = SecMetrics;
        _sectionsMap["QuickModes"] = SecQuickModes;
        _sectionsMap["CoreTiles"] = SecCoreTiles;
        _sectionsMap["CustomShortcuts"] = SecCustomShortcuts;
        _sectionsMap["RecentActivity"] = SecRecentActivity;

        _vm.Customization.CustomizationChanged += (_, _) =>
        {
            Dispatcher.Invoke(ApplyLayoutAndStyles);
        };

        Loaded += (_, _) =>
        {
            _vm.StartRefresh();
            UpdateWelcomeText();
            ApplyLayoutAndStyles();
        };

        Unloaded += (_, _) => _vm.StopRefresh();
    }

    private void UpdateWelcomeText()
    {
        int hour = DateTime.Now.Hour;
        string lang = ServiceContainer.GetOptional<SettingsService>()?.Settings.Language ?? "fr";
        bool isEn = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        string greeting = isEn
            ? hour switch
            {
                >= 5 and < 12 => "Good morning",
                >= 12 and < 18 => "Good afternoon",
                >= 18 and < 22 => "Good evening",
                _ => "Good night"
            }
            : hour switch
            {
                >= 5 and < 12 => "Bonjour",
                >= 12 and < 18 => "Bon après-midi",
                >= 18 and < 22 => "Bonsoir",
                _ => "Bonne nuit"
            };
        try
        {
            string name = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                name = char.ToUpper(name[0]) + (name.Length > 1 ? name[1..] : "");
                WelcomeText.Text = $"{greeting}, {name}";
            }
            else
            {
                WelcomeText.Text = greeting;
            }
        }
        catch
        {
            WelcomeText.Text = greeting;
        }
    }

    public void ApplyLayoutAndStyles()
    {
        if (_vm == null)
        {
            return;
        }

        HomeCustomizationSettings s = _vm.CustomizationSettings;

        CoreTilesGrid.Columns = s.GridColumns > 0 ? s.GridColumns : 3;

        HomeSectionsContainer.Children.Clear();
        foreach (string secId in s.SectionOrder)
        {
            if (_sectionsMap.TryGetValue(secId, out FrameworkElement? element))
            {
                bool isVisible = secId switch
                {
                    "Header" => s.ShowHeader,
                    "Metrics" => s.ShowMetrics,
                    "QuickModes" => s.ShowQuickModes,
                    "CoreTiles" => s.ShowCoreTiles,
                    "CustomShortcuts" => s.ShowCustomShortcuts,
                    "RecentActivity" => s.ShowRecentActivity,
                    _ => true
                };

                element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                if (isVisible)
                {
                    _ = HomeSectionsContainer.Children.Add(element);
                }
            }
        }

        ApplyTileThemeStyles();
    }

    private void ApplyTileThemeStyles()
    {
        string cardBg = "#13151D";
        string cardBorder = "#2A2630";
        double borderThick = 1.0;

        ApplyTileStyle(TileRam, cardBg, cardBorder, borderThick);
        ApplyTileStyle(TileCleaning, cardBg, cardBorder, borderThick);
        ApplyTileStyle(TilePrograms, cardBg, cardBorder, borderThick);
        ApplyTileStyle(TileInstaller, cardBg, cardBorder, borderThick);
        ApplyTileStyle(TileHealth, cardBg, cardBorder, borderThick);
        ApplyTileStyle(TileNetwork, cardBg, cardBorder, borderThick);
    }

    private static void ApplyTileStyle(Border tile, string bgHex, string borderHex, double thickness)
    {
        try
        {
            tile.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            tile.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
            tile.BorderThickness = new Thickness(thickness);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardView.ApplyTileStyle"); }
    }

    private void BtnCustomizeHome_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var owner = Window.GetWindow(this);
            var win = new HomeCustomizerWindow();
            if (owner != null && owner.IsVisible)
            {
                win.Owner = owner;
            }
            _ = win.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DashboardView.BtnCustomizeHome_Click");
            ToastService.Show($"Erreur lors de l'ouverture de la personnalisation : {ex.Message}");
        }
    }

    private void TileRam_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("RamCleaner");
    }

    private void TileCleaning_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("Cleaning");
    }

    private void TilePrograms_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("Programs");
    }

    private void TileInstaller_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("Installer");
    }

    private void TileHealth_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("HealthDefense");
    }

    private void TileNetwork_Click(object sender, MouseButtonEventArgs e)
    {
        NavigateTo("Network");
    }

    private void NavigateTo(string tag)
    {
        try
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.NavigateTo(tag);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"DashboardView.NavigateTo({tag})");
        }
    }

    private void ModeZen_Click(object sender, MouseButtonEventArgs e)
    {
        CurrentViewModel?.RunModeZenCommand.Execute(null);
    }

    private void ModeGamer_Click(object sender, MouseButtonEventArgs e)
    {
        CurrentViewModel?.RunModeGamerCommand.Execute(null);
    }

    private void ModeWork_Click(object sender, MouseButtonEventArgs e)
    {
        CurrentViewModel?.RunModeWorkCommand.Execute(null);
    }

    private async void CustomShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CustomShortcut shortcut } && CurrentViewModel is { } vm)
        {
            vm.NavigationRequested ??= NavigateTo;
            await vm.ExecuteShortcutCommand.ExecuteAsync(shortcut);
        }
    }

    private void BtnDeleteShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && CurrentViewModel is { } vm)
        {
            CustomShortcut? item = vm.CustomShortcuts.FirstOrDefault(s => s.Id == id);
            if (item != null)
            {
                vm.RemoveShortcutCommand.Execute(item);
                ToastService.Show($"Raccourci '{item.Title}' supprimé.");
            }
        }
    }

    private void BtnAddShortcut_Click(object sender, RoutedEventArgs e)
    {
        ShowAddShortcutMenu(sender as FrameworkElement);
    }

    private void ShowAddShortcutMenu(FrameworkElement? target)
    {
        var contextMenu = new ContextMenu();

        _ = contextMenu.Items.Add(BuildMenuItem("⚙️ Invite de commandes (cmd)",
            () => AddToolShortcut("Invite de commandes", "Console Windows CMD", "cmd.exe", "WindowConsole20", "#38BDF8", "#081C2E")));
        _ = contextMenu.Items.Add(BuildMenuItem("⚡ PowerShell",
            () => AddToolShortcut("PowerShell", "Console PowerShell", "powershell.exe", "WindowConsole20", "#38BDF8", "#081C2E")));
        _ = contextMenu.Items.Add(BuildMenuItem("📊 Gestionnaire des tâches",
            () => AddToolShortcut("Gestionnaire des tâches", "Processus & performances", "taskmgr.exe", "DesktopPulse24", "#06B6D4", "#081E29")));
        _ = contextMenu.Items.Add(BuildMenuItem("🧹 Nettoyage de disque (cleanmgr)",
            () => AddToolShortcut("Nettoyage Disque", "Utilitaire Cleanmgr Windows", "cleanmgr.exe", "HardDrive24", "#F59E0B", "#241804")));
        _ = contextMenu.Items.Add(BuildMenuItem("🔧 Panneau de configuration",
            () => AddToolShortcut("Panneau de configuration", "Outils d'administration", "control.exe", "Settings24", "#8B5CF6", "#1F1138")));
        _ = contextMenu.Items.Add(BuildMenuItem("🛡️ Sécurité Windows Defender",
            () => AddToolShortcut("Sécurité Windows", "Antivirus & Pare-feu", "windowsdefender:", "ShieldCheckmark24", "#10D9A0", "#081F17")));
        _ = contextMenu.Items.Add(BuildMenuItem("🖥️ Moniteur de ressources (resmon)",
            () => AddToolShortcut("Moniteur de ressources", "Analyse CPU, RAM, Disque", "resmon.exe", "DesktopPulse24", "#6366F1", "#13182E")));

        _ = contextMenu.Items.Add(new Separator());

        _ = contextMenu.Items.Add(BuildMenuItem("📂 Choisir une application (.exe)...", PickCustomApp));

        if (target != null)
        {
            contextMenu.PlacementTarget = target;
            contextMenu.IsOpen = true;
        }
    }

    private static MenuItem BuildMenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void AddToolShortcut(string title, string subtitle, string target, string icon, string color, string bg)
    {
        if (CurrentViewModel is { } vm)
        {
            vm.AddShortcut(new CustomShortcut
            {
                Title = title,
                Subtitle = subtitle,
                Type = ShortcutType.WindowsTool,
                Target = target,
                IconSymbol = icon,
                AccentColor = color,
                BgTint = bg
            });
            ToastService.Show($"Raccourci '{title}' ajouté avec succès !");
        }
    }

    private void PickCustomApp()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Applications exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*",
            Title = "Sélectionner une application"
        };

        if (dlg.ShowDialog() == true && CurrentViewModel is { } vm)
        {
            string exeName = Path.GetFileNameWithoutExtension(dlg.FileName);
            vm.AddShortcut(new CustomShortcut
            {
                Title = exeName,
                Subtitle = "Application personnalisée",
                Type = ShortcutType.CustomApp,
                Target = dlg.FileName,
                IconSymbol = "Apps24",
                AccentColor = "#6366F1",
                BgTint = "#13182E"
            });
            ToastService.Show($"Application '{exeName}' ajoutée à vos raccourcis !");
        }
    }
}
