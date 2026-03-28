#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.Views;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;

namespace Coclico;

public partial class MainWindow : FluentWindow
{
    private TrayService? _trayService;
    private readonly UserAccountService _userService;
    private readonly ProcessWatcherService _watcher = ServiceContainer.GetRequired<ProcessWatcherService>();

    private DashboardView? _dashboardView;
    private ProgramsView? _programsView;
    private WorkflowPipelinesView? _flowChainsView;
    private InstallerView? _installerView;
    private CleaningView? _cleaningView;
    private ScannerView? _scannerView;
    private SettingsView? _settingsView;
    private HelpView? _helpView;
    private RamCleanerView? _ramCleanerView;

    private bool _sidebarCollapsed = false;
    private bool _isChatOpen = false;
    private const double SidebarCollapsedWidth = 52;
    private readonly KeyboardShortcutsService _hotkeyService = ServiceContainer.GetRequired<KeyboardShortcutsService>();

    private IEnumerable<TextBlock> NavLabels =>
    [
        NavHome_Lbl, NavPrograms_Lbl, NavWorkflowPipelines_Lbl,
        NavInstaller_Lbl, NavCleaning_Lbl, NavScanner_Lbl,
        NavRamCleaner_Lbl,
        NavSettings_Lbl, NavHelp_Lbl, NavAi_Lbl
    ];

    private RadioButton[] NavButtons =>
    [
        NavHome, NavPrograms, NavWorkflowPipelines, NavInstaller,
        NavCleaning, NavScanner, NavRamCleaner,
        NavSettings, NavHelp
    ];

    public MainWindow()
    {
        InitializeComponent();
        _userService = new UserAccountService();

        try
        {
            _dashboardView = new DashboardView();
            MainContentFrame.Content = _dashboardView;
        }
        catch (Exception ex)
        {
            _dashboardView = null;
            LoggingService.LogException(ex, "MainWindow.DashboardInit");
            MainContentFrame.Content = CreateErrorFallback("Impossible de charger le tableau de bord. Veuillez redémarrer l'application.");
        }

        try
        {
            var s = ServiceContainer.GetRequired<SettingsService>().Settings;
            if (s.CompactMode)
                SetCompactMode(true);
            else
                SetSidebarWidth(s.SidebarWidth);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.SettingsInit"); }

        ToastService.Initialize(RootSnackbarPresenter);

        UserNameText.Text       = _userService.DisplayName;
        UserNameText.Visibility = Visibility.Collapsed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        this.BeginAnimation(OpacityProperty, fade);

        try
        {
            if (ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray)
                _trayService = new TrayService();
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.TrayCreate"); }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hotkeyService.Initialize(this);
            _hotkeyService.ShortcutTriggered += OnHotkeyTriggered;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.HotkeyInit"); }

        try
        {
            if (ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray && _trayService != null && !_trayService.IsInitialized)
            {
                _trayService.Initialize(this);
                _trayService.ShowBalloon(ServiceContainer.GetRequired<LocalizationService>().Get("App_Title"), ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Running"));
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.TrayInit.OnSourceInitialized"); }
    }

    private void OnHotkeyTriggered(KeyboardShortcut shortcut)
    {
        try
        {
            if (shortcut.ActionType == "WorkflowPipeline" && !string.IsNullOrEmpty(shortcut.ChainId))
            {
                var chains = new WorkflowService().GetWorkflowPipelines();
                var chain = System.Linq.Enumerable.FirstOrDefault(chains, c => c.Id == shortcut.ChainId);
                if (chain != null)
                {
                    ToastService.Show($"▶ Flow Chain: {chain.Name}");
                    _ = new WorkflowService().ExecuteChainAsync(chain);
                }
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnHotkeyTriggered"); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray)
            {
                e.Cancel = true;
                this.Hide();
                try { _trayService?.ShowBalloon(ServiceContainer.GetRequired<LocalizationService>().Get("App_Title"), ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Running")); }
                catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosing.TrayBalloon"); }
                return;
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosing"); }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _trayService?.Dispose(); } catch { }
        try { _hotkeyService.Dispose(); } catch { }
    }

    public void MinimizeToTrayNow()
    {
        try
        {
            if (ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray)
            {
                this.Hide();
                _trayService ??= new TrayService();
                try { _trayService.Initialize(this); } catch { }
                _trayService.ShowBalloon(ServiceContainer.GetRequired<LocalizationService>().Get("App_Title"), ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Running"));
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MinimizeToTrayNow"); }
    }

    public void ShowTrayBalloon(string title, string text)
    {
        try { _trayService?.ShowBalloon(title, text); } catch { }
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        bool newCompact = !ServiceContainer.GetRequired<SettingsService>().Settings.CompactMode;
        ServiceContainer.GetRequired<SettingsService>().Settings.CompactMode = newCompact;
        ServiceContainer.GetRequired<SettingsService>().Save();

        Application.Current.Resources.Remove("CardPadding");
        Application.Current.Resources["CardPadding"] = newCompact ? new Thickness(12) : new Thickness(24);
        Application.Current.Resources["GlobalFontSize"] = newCompact
            ? 11.5
            : ServiceContainer.GetRequired<SettingsService>().Settings.FontSize;

        SetCompactMode(newCompact);
    }

    private void AnimateSidebarWidth(double from, double to)
    {
        const double duration = 200;

        var sw    = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (s, e) =>
        {
            double elapsed = sw.Elapsed.TotalMilliseconds;
            double t = Math.Min(elapsed / duration, 1.0);
            t = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            SidebarColumn.Width = new GridLength(from + (to - from) * t);

            if (elapsed >= duration)
            {
                SidebarColumn.Width = new GridLength(to);
                timer.Stop();
                sw.Stop();
            }
        };

        timer.Start();
    }

    private async void NavItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement el || el.Tag?.ToString() is not string tag) return;

            UIElement newContent = tag switch
            {
                "Home"              => _dashboardView ??= new DashboardView(),
                "Programs"          => _programsView   ??= new ProgramsView(),
                "WorkflowPipelines" => _flowChainsView ??= new WorkflowPipelinesView(),
                "Installer"         => _installerView  ??= new InstallerView(),
                "Cleaning"          => _cleaningView   ??= new CleaningView(),
                "Scanner"           => _scannerView    ??= new ScannerView(),
                "RamCleaner"        => _ramCleanerView ??= new RamCleanerView(),
                "Settings"          => _settingsView   ??= new SettingsView(),
                "Help"              => _helpView       ??= new HelpView(),
                _                   => _dashboardView ??= new DashboardView()
            };

            ServiceContainer.GetRequired<IAiService>().CurrentStatusContext = tag switch
            {
                "Home"              => "L'utilisateur est sur le Tableau de Bord (stats CPU/RAM/Disque).",
                "Programs"          => "L'utilisateur consulte la liste des Applications installées.",
                "WorkflowPipelines" => "L'utilisateur est sur les Flow Chains (automatisation).",
                "Installer"         => "L'utilisateur est dans l'Installeur Winget.",
                "Cleaning"          => "L'utilisateur est dans le Nettoyage système.",
                "Scanner"           => "L'utilisateur utilise le Scanner d'applications.",
                "RamCleaner"        => "L'utilisateur est dans le RAM Cleaner (nettoyage et surveillance mémoire).",
                "Settings"          => "L'utilisateur est dans les Paramètres.",
                "Help"              => "L'utilisateur est sur la page d'Aide.",
                _                   => "L'utilisateur est sur le Tableau de Bord."
            };

            await NavigateWithTransition(newContent);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.NavItem_Click"); }
    }

    private async Task NavigateWithTransition(UIElement newContent)
    {
        if (MainContentFrame.RenderTransform is not TranslateTransform)
            MainContentFrame.RenderTransform = new TranslateTransform();

        var translate = (TranslateTransform)MainContentFrame.RenderTransform;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)) { EasingFunction = ease };
        var slideOut = new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(90)) { EasingFunction = ease };
        translate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(90);

        MainContentFrame.Content = newContent;

        translate.Y = 10;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
        var slideIn = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easeOut };
        translate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public void SetSidebarWidth(double width)
    {
        if (_sidebarCollapsed) return;
        SidebarColumn.Width = new GridLength(Math.Clamp(width, 120, 400));
    }

    public void SetCompactMode(bool compact)
    {
        try
        {
            _sidebarCollapsed = compact;

            double from = SidebarColumn.Width.Value;
            double to   = compact
                ? SidebarCollapsedWidth
                : Math.Clamp(ServiceContainer.GetRequired<SettingsService>().Settings.SidebarWidth, 120, 400);
            AnimateSidebarWidth(from, to);

            var labelVis = compact ? Visibility.Collapsed : Visibility.Visible;
            foreach (var lbl in NavLabels) lbl.Visibility = labelVis;
            BrandLabel.Visibility    = labelVis;
            NavSectionLbl.Visibility = labelVis;

            SidebarLogoImage.Visibility = Visibility.Visible;
            BrandLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            if (compact)
            {
                SidebarLogoImage.Margin = new Thickness(0, 0, 0, 8);
                SidebarLogoImage.HorizontalAlignment = HorizontalAlignment.Center;
                SidebarToggleBtn.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(SidebarToggleBtn, 1);
                Grid.SetColumn(SidebarToggleBtn, 0);
                Grid.SetColumnSpan(SidebarToggleBtn, 3);
                Grid.SetColumnSpan(SidebarLogoImage, 3);
            }
            else
            {
                SidebarLogoImage.Margin = new Thickness(0);
                SidebarLogoImage.HorizontalAlignment = HorizontalAlignment.Left;
                SidebarToggleBtn.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(SidebarToggleBtn, 0);
                Grid.SetColumn(SidebarToggleBtn, 2);
                Grid.SetColumnSpan(SidebarToggleBtn, 1);
                Grid.SetColumnSpan(SidebarLogoImage, 1);
            }

            SidebarHeaderBorder.Padding = compact
                ? new Thickness(0, 10, 0, 10)
                : new Thickness(12, 14, 12, 10);

            ToggleIcon.Symbol = compact
                ? SymbolRegular.ChevronRight24
                : SymbolRegular.Navigation24;

            var hca = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            foreach (var rb in NavButtons)
                rb.HorizontalContentAlignment = hca;

            BtnAiChat.HorizontalContentAlignment = hca;
            BtnAiChat.Padding = compact ? new Thickness(0, 8, 0, 8) : new Thickness(10, 8, 10, 8);

            UserNameText.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.SetCompactMode"); }
    }

    private UIElement CreateErrorFallback(string message)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 6, 6, 12)),
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

        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6),
            Width = 560
        });

        stack.Children.Add(new Button
        {
            Content = "Réessayer",
            Width = 140,
            Margin = new Thickness(0, 14, 0, 0),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => MainContentFrame.Content = _dashboardView ??= new DashboardView())
        });

        border.Child = stack;
        return border;
    }

    private void BtnAiChat_Click(object sender, RoutedEventArgs e) =>
        ToggleChatPanel(!_isChatOpen);

    private void AiChatPanel_CloseRequested(object? sender, EventArgs e) =>
        ToggleChatPanel(false);

    private async void AiChatPanel_ActionRequested(object? sender, string action)
    {
        try
        {
            ToggleChatPanel(false);

            UIElement? target = action switch
            {
                "open_dashboard"  => _dashboardView,
                "open_programs"   => _programsView   ??= new ProgramsView(),
                "open_flowchains" => _flowChainsView ??= new WorkflowPipelinesView(),
                "open_installer"  => _installerView  ??= new InstallerView(),
                "open_cleaning"   => _cleaningView   ??= new CleaningView(),
                "open_scanner"    => _scannerView    ??= new ScannerView(),
                "open_ramcleaner" => _ramCleanerView ??= new RamCleanerView(),
                "open_settings"   => _settingsView   ??= new SettingsView(),
                _                 => null
            };

            ServiceContainer.GetRequired<IAiService>().CurrentStatusContext = action switch
            {
                "open_dashboard"  => "L'utilisateur est sur le Tableau de Bord (stats CPU/RAM/Disque).",
                "open_programs"   => "L'utilisateur consulte la liste des Applications installées.",
                "open_flowchains" => "L'utilisateur est sur les Flow Chains (automatisation).",
                "open_installer"  => "L'utilisateur est dans l'Installeur Winget.",
                "open_cleaning"   => "L'utilisateur est dans le Nettoyage système.",
                "open_scanner"    => "L'utilisateur utilise le Scanner d'applications.",
                "open_settings"   => "L'utilisateur est dans les Paramètres.",
                _                 => ServiceContainer.GetRequired<IAiService>().CurrentStatusContext
            };

            var navButton = action switch
            {
                "open_dashboard"  => NavHome,
                "open_programs"   => NavPrograms,
                "open_flowchains" => NavWorkflowPipelines,
                "open_installer"  => NavInstaller,
                "open_cleaning"   => NavCleaning,
                "open_scanner"    => NavScanner,
                "open_ramcleaner" => NavRamCleaner,
                "open_settings"   => NavSettings,
                _                 => (RadioButton?)null
            };
            if (navButton != null) navButton.IsChecked = true;

            if (target != null)
                await NavigateWithTransition(target);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.AiChatPanel_ActionRequested"); }
    }

    private void ToggleChatPanel(bool open)
    {
        _isChatOpen = open;
        var slideTransform = AiChatOverlay.RenderTransform as TranslateTransform;

        if (open)
        {
            AiChatOverlay.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            var anim = new DoubleAnimation(420, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) => AiChatOverlay.Visibility = Visibility.Collapsed;
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    private void ProfileBorder_MouseDown(object sender, RoutedEventArgs e)
    {
        var profileWindow = new ProfileWindow(_userService);
        profileWindow.Owner = this;
        profileWindow.ShowDialog();
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ToastService.ShowInfo("Vérification des mises à jour en cours...");
            var updateCheckService = UpdateCheckService.GetInstance(
                new UpdateManager(new NullLogger<UpdateManager>(), ServiceContainer.GetRequired<SettingsService>()),
                new NullLogger<UpdateCheckService>(),
                ServiceContainer.GetRequired<SettingsService>()
            );

            var update = await updateCheckService.CheckForUpdatesAsync();

            if (update != null)
            {
                ToastService.Show($"Mise à jour disponible : {update.TagName}");
            }
            else
            {
                ToastService.ShowInfo("Vous utilisez la dernière version.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.UpdateCheck");
            ToastService.ShowError("Erreur lors de la vérification des mises à jour");
        }
    }
}
