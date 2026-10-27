using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Modules.Apps.Views;
using Coclico.Modules.DiskAnalyzer.Views;
using Coclico.Modules.Power.Views;
using Coclico.Modules.UpdateCenter.ViewModels;
using Coclico.Modules.UpdateCenter.Views;
using Coclico.Services;
using Coclico.Views;
namespace Coclico;

public partial class MainWindow
{
    private static readonly (string Tag, Func<UIElement> Factory)[] ModuleRegistry =
    [
        ("Home", static () => ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView()),
        ("Programs", static () => new ProgramsView()),
        ("DiskAnalyzer", static () => new DiskAnalyzerView()),
        ("HealthDefense", static () => new HealthDefenseView()),
        ("Diagnostics", static () => new DiagnosticView()),
        ("Network", static () => ServiceContainer.GetOptional<NetworkOptimizerView>() ?? new NetworkOptimizerView()),
        ("Installer", static () => ServiceContainer.GetOptional<AppsView>() ?? new AppsView()),
        ("UpdateCenter", static () => ServiceContainer.GetOptional<UpdateCenterView>() ?? new UpdateCenterView(ServiceContainer.GetRequired<UpdateCenterViewModel>())),
        ("Cleaning", static () => new CleaningView()),
        ("RamCleaner", static () => new RamCleanerView()),
        ("PowerOptimizer", static () => ServiceContainer.GetOptional<PowerOptimizerView>() ?? new PowerOptimizerView()),
        ("Settings", static () => new SettingsView()),
        ("Help", static () => new HelpView()),
    ];

    private static readonly string[] MenuGroupIds = ["Maintenance", "Performance", "Applications"];

    private void NavItem_Click(object sender, RoutedEventArgs e)
        => AsyncEventSafe.Run(() => NavItem_ClickAsync(sender, e), "MainWindow.NavItem_Click");

    private async Task NavItem_ClickAsync(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag?.ToString() is not string tag)
        {
            return;
        }

        await NavigateToTagAsync(tag);
    }

    private UIElement GetOrCreateView(string? tag)
    {
        tag = string.IsNullOrEmpty(tag) ? "Home" : tag;
        if (_viewCache.TryGetValue(tag, out UIElement? cached))
        {
            return cached;
        }

        (string Tag, Func<UIElement> Factory) module = ModuleRegistry
            .FirstOrDefault(m => string.Equals(m.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (module.Factory == null)
        {
            module = ModuleRegistry[0];
        }

        UIElement view = module.Factory();
        _viewCache[module.Tag] = view;
        return view;
    }

    private static string? TagForAiAction(string action)
    {
        return action switch
        {
            "open_dashboard" => "Home",
            "open_programs" => "Programs",
            "open_disk" => "DiskAnalyzer",
            "open_health" or "open_defense" => "HealthDefense",
            "open_diagnostics" => "Diagnostics",
            "open_network" => "Network",
            "open_power" => "PowerOptimizer",
            "open_installer" => "Installer",
            "open_updates" => "UpdateCenter",
            "open_cleaning" => "Cleaning",
            "open_ramcleaner" => "RamCleaner",
            "open_settings" => "Settings",
            "open_help" => "Help",
            _ => null
        };
    }

    private static void RecordStigmergyTransition(string tag)
    {
        try
        {
            CentralUnitStigmergy stigmergy = CentralUnitStigmergy.Current;
            stigmergy.RecordTransition(stigmergy.LastModule, tag);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.RecordStigmergyTransition"); }
    }

    private void PreloadPredictedViews(string currentTag)
    {
        try
        {
            List<(string Module, double Probability)> predicted = CentralUnitStigmergy.Current.GetPredictedNextModules(currentTag, 2);
            if (predicted.Count == 0)
            {
                return;
            }

            CentralActivityLevel level = CentralUnitService.Current.Activity.Level;
            if (level != CentralActivityLevel.Active && level != CentralActivityLevel.StartingUp)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                foreach ((string module, double probability) in predicted)
                {
                    if (probability < 0.6)
                    {
                        continue;
                    }

                    _ = GetOrCreateView(module);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.PreloadPredictedViews"); }
    }

    public async Task NavigateToTagAsync(string tag)
    {
        try
        {
            RecordStigmergyTransition(tag);

            UIElement newContent = GetOrCreateView(tag);

            IAiService? ai = ServiceContainer.GetOptional<IAiService>();
            ai?.CurrentStatusContext = tag switch
            {
                "Home" => "L'utilisateur est sur le Tableau de Bord (stats CPU/RAM/Disque).",
                "Programs" => "L'utilisateur consulte la liste des Applications installées.",
                "DiskAnalyzer" => "L'utilisateur est sur l'analyse de l'espace disque (carte radiale des zones du disque dur, doublons et gros fichiers).",
                "HealthDefense" => "L'utilisateur est sur le module Santé, Défense & Réparation Windows.",
                "Network" => "L'utilisateur est sur le module Réseau & Latence (optimisation TCP, cartes Ethernet/Wi-Fi, DNS, MTU).",
                "Installer" => "L'utilisateur est dans l'Installeur Winget.",
                "UpdateCenter" => "L'utilisateur est dans le Centre de Mises à jour (applications via Winget, pilotes via Windows Update, statut Windows et Coclico).",
                "Cleaning" => "L'utilisateur est dans le Nettoyage système.",
                "RamCleaner" => "L'utilisateur est dans le RAM Cleaner (nettoyage et surveillance mémoire).",
                "PowerOptimizer" => "L'utilisateur est dans le module CPU & Alimentation (plans Coclico économie/optimal/performance, mode adaptatif, test CPU).",
                "Settings" => "L'utilisateur est dans les Paramètres.",
                "Help" => "L'utilisateur est sur la page d'Aide.",
                _ => "L'utilisateur est sur le Tableau de Bord."
            };

            await NavigateWithTransition(newContent);
            UpdateTopNavActiveIndicator(tag);
            PreloadPredictedViews(tag);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.NavigateToTagAsync");
            MainContentFrame.Content = CreateErrorFallback(L("MainWindow_PageLoadError"));
        }
    }

    private async Task NavigateWithTransition(UIElement newContent)
    {
        if (MainContentFrame.RenderTransform is not TranslateTransform)
        {
            MainContentFrame.RenderTransform = new TranslateTransform();
        }

        var translate = (TranslateTransform)MainContentFrame.RenderTransform;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation(0, Motion.NavFadeOut) { EasingFunction = easeOut };
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(Motion.NavFadeOut);

        MainContentFrame.Content = newContent;

        if (AnimatePageIn())
        {
            return;
        }

        translate.Y = Motion.PageSlideDistance;
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, Motion.NavSlideIn) { EasingFunction = easeIn };
        var slideIn = new DoubleAnimation(Motion.PageSlideDistance, 0, Motion.NavSlideIn) { EasingFunction = easeIn };
        translate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private bool AnimatePageIn()
    {
        if (TryFindResource("PageInStoryboard") is not Storyboard pageIn || FindName("PageContentTransform") is null)
        {
            return false;
        }

        pageIn.Begin(this, HandoffBehavior.SnapshotAndReplace, false);
        return true;
    }

    private UIElement CreateErrorFallback(string message)
    {
        var border = new Border
        {
            Background = TryFindResource("BgPanelBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(255, 6, 6, 12)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(26),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _ = stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? new SolidColorBrush(Colors.White),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6),
            Width = 560
        });

        _ = stack.Children.Add(new Button
        {
            Content = "Réessayer",
            Width = 140,
            Margin = new Thickness(0, 14, 0, 0),
            Background = TryFindResource("BgElevatedBrush") as Brush,
            Foreground = TryFindResource("ArchitecturalYellowBrush") as Brush,
            BorderBrush = TryFindResource("BorderAccentBrush") as Brush,
            BorderThickness = new Thickness(1),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => MainContentFrame.Content = GetOrCreateView("Home"))
        });

        border.Child = stack;
        return border;
    }

    public void NavigateTo(string tag)
    {
        RadioButton? rb = FindNavButton(tag);
        if (rb != null)
        {
            rb.IsChecked = true;
        }

        _ = NavigateToTagAsync(tag);
    }

    private void TopNavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    public void UpdateTopNavActiveIndicator(string activeTag)
    {
        try
        {
            if (TopUnderlineHome == null)
            {
                return;
            }

            TopUnderlineHome.Visibility = activeTag == "Home" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineNetwork.Visibility = activeTag == "Network" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineCleaning.Visibility = activeTag == "Cleaning" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineRam.Visibility = activeTag == "RamCleaner" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineHealth.Visibility = activeTag == "HealthDefense" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlinePrograms.Visibility = activeTag == "Programs" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineDisk.Visibility = activeTag == "DiskAnalyzer" ? Visibility.Visible : Visibility.Collapsed;
            TopUnderlineInstaller.Visibility = activeTag == "Installer" ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "MainWindow.UpdateTopNavActiveIndicator"); }
    }

    private void MenuGroupToggle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string groupId)
        {
            SetMenuGroupExpanded(groupId, !IsMenuGroupExpanded(groupId), persist: true);
            e.Handled = true;
        }
    }

    private bool IsMenuGroupExpanded(string groupId)
    {
        return FindMenuPanel(groupId)?.Visibility == Visibility.Visible;
    }

    private System.Windows.Controls.StackPanel? FindMenuPanel(string groupId) => groupId switch
    {
        "Maintenance" => MenuPanelMaintenance,
        "Performance" => MenuPanelPerformance,
        "Applications" => MenuPanelApplications,
        _ => null
    };

    private Wpf.Ui.Controls.SymbolIcon? FindMenuChevron(string groupId) => groupId switch
    {
        "Maintenance" => MenuChevronMaintenance,
        "Performance" => MenuChevronPerformance,
        "Applications" => MenuChevronApplications,
        _ => null
    };

    private void SetMenuGroupExpanded(string groupId, bool expanded, bool persist = false)
    {
        try
        {
            System.Windows.Controls.StackPanel? panel = FindMenuPanel(groupId);
            if (panel != null)
            {
                panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FindMenuChevron(groupId)?.RenderTransform is RotateTransform rotate)
            {
                var anim = new DoubleAnimation(expanded ? 0 : -90, Motion.ChevronRotate)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }

            if (persist)
            {
                SaveMenuGroupExpansion();
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"MainWindow.SetMenuGroupExpanded({groupId})");
        }
    }

    private void LoadMenuGroupExpansion()
    {
        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            HashSet<string> expanded = new(
                ss.Settings.MenuExpandedGroups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            foreach (string id in MenuGroupIds)
            {
                SetMenuGroupExpanded(id, expanded.Contains(id), persist: false);
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.LoadMenuGroupExpansion");
        }
    }

    private void SaveMenuGroupExpansion()
    {
        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            ss.Settings.MenuExpandedGroups = string.Join(",", MenuGroupIds.Where(IsMenuGroupExpanded));
            _ = ss.SaveAsync();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.SaveMenuGroupExpansion");
        }
    }
}
