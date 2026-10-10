using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Modules.DiskAnalyzer.Views;
using Coclico.Modules.Installer.Windows;
using Coclico.Modules.Power.Views;
using Coclico.Services;
using Coclico.Views;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Coclico;

public partial class MainWindow : FluentWindow
{
    private static readonly (string Tag, Func<UIElement> Factory)[] ModuleRegistry =
    [
        ("Home", static () => ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView()),
        ("Programs", static () => new ProgramsView()),
        ("DiskAnalyzer", static () => new DiskAnalyzerView()),
        ("HealthDefense", static () => new HealthDefenseView()),
        ("Network", static () => ServiceContainer.GetOptional<NetworkOptimizerView>() ?? new NetworkOptimizerView()),
        ("Installer", static () => new InstallerView()),
        ("Cleaning", static () => new CleaningView()),
        ("RamCleaner", static () => new RamCleanerView()),
        ("PowerOptimizer", static () => ServiceContainer.GetOptional<PowerOptimizerView>() ?? new PowerOptimizerView()),
        ("Settings", static () => new SettingsView()),
        ("Help", static () => new HelpView()),
    ];

    private static readonly string[] MenuGroupIds = ["Maintenance", "Performance", "Applications"];

    private static string L(string key) => Application.Current?.TryFindResource(key) as string ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key) ?? key;

    private TrayService? _trayService;
    private readonly UserAccountService _userService;
    private readonly KeyboardShortcutsService _hotkeyService = ServiceContainer.GetRequired<KeyboardShortcutsService>();

    private readonly Dictionary<string, UIElement> _viewCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _isChatOpen = false;
    private bool _isDropdownOpen = false;
    private GitHubRelease? _pendingUpdate;

    public RadioButton? FindNavButton(string tag)
    {
        return tag switch
        {
            "Home" => NavHome,
            "Programs" => NavPrograms,
            "DiskAnalyzer" => NavDisk,
            "HealthDefense" => NavHealthDefense,
            "Network" => NavNetwork,
            "Installer" => NavInstaller,
            "Cleaning" => NavCleaning,
            "RamCleaner" => NavRamCleaner,
            "Settings" => NavSettings,
            "Help" => NavHelp,
            _ => null
        };
    }

    public MainWindow()
    {
        InitializeComponent();
        _userService = ServiceContainer.GetRequired<UserAccountService>();

        try
        {
            _viewCache["Home"] = ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView();
            MainContentFrame.Content = _viewCache["Home"];
        }
        catch (Exception ex)
        {
            _viewCache.Remove("Home");
            LoggingService.LogException(ex, "MainWindow.DashboardInit");
            MainContentFrame.Content = CreateErrorFallback(L("MainWindow_DashboardLoadError"));
        }

        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            AppSettings s = ss.Settings;
            if (s.CompactMode)
            {
                SetCompactMode(true);
            }
            else
            {
                SetSidebarWidth(s.SidebarWidth);
            }

            BtnFloatingAi.Visibility = s.AiEnableFloatingButton ? Visibility.Visible : Visibility.Collapsed;

            ss.FloatingButtonVisibilityChanged += visible =>
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (!_isChatOpen)
                    {
                        BtnFloatingAi.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                });
            };
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.SettingsInit"); }

        LoadMenuGroupExpansion();

        try
        {
            VersionText.Text = "v" + UpdateCheckService.GetCurrentVersion();
            ApplyVersionTexts();

            ServiceContainer.GetRequired<UpdateCheckService>().UpdateAvailable += OnUpdateAvailable;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.UpdateInit"); }

        ToastService.Initialize(RootSnackbarPresenter);

        UserNameText.Text = _userService.DisplayName;
        UserNameText.Visibility = Visibility.Collapsed;

        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (BetaWelcomeOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                DismissBetaWelcome();
                return;
            }
        }

        if (e.Key == System.Windows.Input.Key.Space && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleChatPanel(!_isChatOpen);
        }
        else if (e.Key == System.Windows.Input.Key.F11)
        {
            e.Handled = true;
            ToggleWindowMode();
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            try { MemoryCleanerService.TrimSelfWorkingSet(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MainWindow_StateChanged.TrimWorkingSet"); }
        }
        UpdateWindowModeButton();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);

        try
        {
            AppSettings? settings = ServiceContainer.GetOptional<SettingsService>()?.Settings;
            WindowState = settings != null && settings.LaunchMode == "Maximized" ? WindowState.Maximized : WindowState.Normal;
            UpdateWindowModeButton();

            LocalizationService? locService = ServiceContainer.GetOptional<LocalizationService>();
            if (locService != null)
            {
                locService.LanguageChanged += OnLanguageChanged;
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MainWindow_Loaded.ApplyStartupState"); }

        try
        {
            BtnElevateAdmin.Visibility = App.IsRunningAsAdministrator() ? Visibility.Collapsed : Visibility.Visible;

            SettingsService? ss = ServiceContainer.GetOptional<SettingsService>();
            if (ss != null && ss.Settings.MinimizeToTray)
            {
                _trayService = ServiceContainer.GetRequired<TrayService>();
            }

            _ = Dispatcher.InvokeAsync(ShowBetaWelcome, System.Windows.Threading.DispatcherPriority.Loaded);
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
            if (shortcut.Action == "ToggleAi" || shortcut.ActionType == "AiChat")
            {
                ToggleChatPanel(!_isChatOpen);
            }
            else if (shortcut.Action == "RamClean" || shortcut.ActionType == "RamClean")
            {
                _ = MemoryCleanerService.FullCleanAsync(true);
                ToastService.Show(L("MainWindow_RamCleanedShortcut"));
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
                Hide();
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
        try
        {
            _hotkeyService?.ShortcutTriggered -= OnHotkeyTriggered;
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosed.UnsubscribeHotkeys"); }
        try { _trayService?.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosed.DisposeTrayService"); }
        try { _hotkeyService?.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosed.DisposeHotkeyService"); }
    }

    public void MinimizeToTrayNow()
    {
        try
        {
            if (ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray)
            {
                Hide();
                try { MemoryCleanerService.TrimSelfWorkingSet(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MinimizeToTrayNow.TrimWorkingSet"); }
                _trayService ??= ServiceContainer.GetRequired<TrayService>();
                try { _trayService.Initialize(this); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MinimizeToTrayNow.InitializeTray"); }
                _trayService.ShowBalloon(ServiceContainer.GetRequired<LocalizationService>().Get("App_Title"), ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Running"));
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MinimizeToTrayNow"); }
    }

    public void ShowTrayBalloon(string title, string text)
    {
        try { _trayService?.ShowBalloon(title, text); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.ShowTrayBalloon"); }
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

    private async void NavItem_Click(object sender, RoutedEventArgs e)
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
            "open_network" => "Network",
            "open_power" => "PowerOptimizer",
            "open_installer" => "Installer",
            "open_cleaning" => "Cleaning",
            "open_ramcleaner" => "RamCleaner",
            "open_settings" => "Settings",
            "open_help" => "Help",
            _ => null
        };
    }

    public async Task NavigateToTagAsync(string tag)
    {
        try
        {
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
                "Cleaning" => "L'utilisateur est dans le Nettoyage système.",
                "RamCleaner" => "L'utilisateur est dans le RAM Cleaner (nettoyage et surveillance mémoire).",
                "PowerOptimizer" => "L'utilisateur est dans le module CPU & Alimentation (plans Coclico économie/optimal/performance, mode adaptatif, test CPU).",
                "Settings" => "L'utilisateur est dans les Paramètres.",
                "Help" => "L'utilisateur est sur la page d'Aide.",
                _ => "L'utilisateur est sur le Tableau de Bord."
            };

            await NavigateWithTransition(newContent);
            UpdateTopNavActiveIndicator(tag);
        }
        catch (Exception ex)
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
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(70)) { EasingFunction = easeOut };
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(70);

        MainContentFrame.Content = newContent;

        translate.Y = 10;
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = easeIn };
        var slideIn = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = easeIn };
        translate.BeginAnimation(TranslateTransform.YProperty, slideIn);
        MainContentFrame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public void SetSidebarWidth(double width)
    {
        double clamped = Math.Clamp(width, 52.0, 500.0);
        SidebarColumn.Width = new GridLength(clamped);
    }

    public void SetCompactMode(bool compact)
    {
        try
        {
            NavBorder.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
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

        _ = stack.Children.Add(new TextBlock
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

        _ = stack.Children.Add(new Button
        {
            Content = "Réessayer",
            Width = 140,
            Margin = new Thickness(0, 14, 0, 0),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => MainContentFrame.Content = GetOrCreateView("Home"))
        });

        border.Child = stack;
        return border;
    }

    private void BtnAiChat_Click(object sender, RoutedEventArgs e)
    {
        ToggleChatPanel(!_isChatOpen);
    }

    private void AiChatPanel_CloseRequested(object? sender, EventArgs e)
    {
        ToggleChatPanel(false);
    }

    private async void AiChatPanel_ActionRequested(object? sender, string action)
    {
        try
        {
            ToggleChatPanel(false);

            string? targetTag = TagForAiAction(action);
            UIElement? target = targetTag == null ? null : GetOrCreateView(targetTag);

            ServiceContainer.GetRequired<IAiService>().CurrentStatusContext = action switch
            {
                "open_dashboard" => "L'utilisateur est sur le Tableau de Bord (stats CPU/RAM/Disque).",
                "open_programs" => "L'utilisateur consulte la liste des Applications installées.",
                "open_disk" => "L'utilisateur est sur l'analyse de l'espace disque (carte radiale des zones du disque dur).",
                "open_health" or "open_defense" => "L'utilisateur est sur le module Santé, Défense & Réparation Windows.",
                "open_network" => "L'utilisateur consulte le module Réseau & Latence.",
                "open_power" => "L'utilisateur est dans le module CPU & Alimentation (plans Coclico économie/optimal/performance, mode adaptatif).",
                "open_installer" => "L'utilisateur est dans l'Installeur Winget.",
                "open_cleaning" => "L'utilisateur est dans le Nettoyage système.",
                "open_ramcleaner" => "L'utilisateur est dans le RAM Cleaner.",
                "open_settings" => "L'utilisateur est dans les Paramètres.",
                "open_help" => "L'utilisateur consulte l'Aide et le support.",
                _ => ServiceContainer.GetRequired<IAiService>().CurrentStatusContext
            };

            RadioButton? navButton = action switch
            {
                "open_dashboard" => NavHome,
                "open_programs" => NavPrograms,
                "open_disk" => NavDisk,
                "open_health" or "open_defense" => NavHealthDefense,
                "open_network" => NavNetwork,
                "open_installer" => NavInstaller,
                "open_cleaning" => NavCleaning,
                "open_ramcleaner" => NavRamCleaner,
                "open_settings" => NavSettings,
                "open_help" => NavHelp,
                _ => null
            };
            navButton?.IsChecked = true;

            if (target != null)
            {
                await NavigateWithTransition(target);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.AiChatPanel_ActionRequested"); }
    }

    private void BtnFloatingAi_Click(object sender, RoutedEventArgs e)
    {
        ToggleChatPanel(!_isChatOpen);
    }

    private void ToggleChatPanel(bool open)
    {
        _isChatOpen = open;
        var slideTransform = AiChatOverlay.RenderTransform as TranslateTransform;

        if (open)
        {
            BtnFloatingAi.Visibility = Visibility.Collapsed;
            AiChatOverlay.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            _ = ServiceContainer.GetOptional<IAiService>()?.UnloadAsync();
            var anim = new DoubleAnimation(540, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += OnAiChatAnimCompleted;
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    private void OnAiChatAnimCompleted(object? sender, EventArgs e)
    {
        AiChatOverlay.Visibility = Visibility.Collapsed;
        AppSettings? s = ServiceContainer.GetOptional<SettingsService>()?.Settings;
        BtnFloatingAi.Visibility = (s?.AiEnableFloatingButton ?? true) ? Visibility.Visible : Visibility.Collapsed;
        if (sender is AnimationClock clock)
        {
            clock.Completed -= OnAiChatAnimCompleted;
        }
    }

    private void ProfileBorder_MouseDown(object sender, RoutedEventArgs e)
    {
        var profileWindow = new ProfileWindow(_userService)
        {
            Owner = this
        };
        _ = profileWindow.ShowDialog();
    }

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is GitHubRelease pendingRelease)
        {
            OpenUpdateWindow(pendingRelease);
            return;
        }

        try
        {
            ToastService.ShowInfo("Vérification des mises à jour en cours...");
            var updateCheckService = ServiceContainer.GetRequired<UpdateCheckService>();

            GitHubRelease? update = await updateCheckService.CheckForUpdatesAsync();

            if (update != null)
            {
                _pendingUpdate = update;
                MarkUpdateButton(update.TagName);
                OpenUpdateWindow(update);
            }
            else
            {
                ToastService.ShowInfo(L("MainWindow_LatestVersion"));
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.UpdateCheck");
            ToastService.ShowError(L("MainWindow_UpdateCheckError"));
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateAvailableEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnUpdateAvailable(sender, e));
            return;
        }

        try
        {
            string? skipped = ServiceContainer.GetOptional<SettingsService>()?.Settings.SkippedUpdateVersion;
            if (UpdateManager.IsSkipped(e.Release.TagName, skipped))
            {
                return;
            }

            _pendingUpdate = e.Release;
            MarkUpdateButton(e.Release.TagName);
            ToastService.Show($"Mise à jour disponible : {e.Release.TagName} — clique sur l'icône de synchronisation pour l'installer.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.OnUpdateAvailable");
        }
    }

    private void MarkUpdateButton(string tagName)
    {
        try
        {
            UpdateCheckButton.ToolTip = $"{L("Main_CheckUpdatesTip")} — {tagName}";
            if (UpdateCheckButton.Content is Wpf.Ui.Controls.SymbolIcon icon)
            {
                icon.Foreground = FindResource("ArchitecturalYellowBrush") as Brush ?? icon.Foreground;
            }
        }
        catch
        {
        }
    }

    private void OpenUpdateWindow(GitHubRelease release)
    {
        try
        {
            var window = new UpdateWindow(release, UpdateCheckService.GetCurrentVersion()) { Owner = this };
            _ = window.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.OpenUpdateWindow");
            ToastService.ShowError("Impossible d'ouvrir le dialogue de mise à jour.");
        }
    }

    private void BtnElevateAdmin_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
            L("MainWindow_ElevateAdminPrompt"),
            L("MainWindow_ElevateAdminTitle"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            if (!App.RestartAsAdmin())
            {
                ToastService.ShowWarning(L("MainWindow_ElevateAdminFailed"));
            }
        }
    }

    public void ToggleDropdownMenu(bool? forceOpen = null)
    {
        bool target = forceOpen ?? !_isDropdownOpen;
        if (target == _isDropdownOpen)
        {
            return;
        }

        _isDropdownOpen = target;

        if (_isDropdownOpen)
        {
            ArchitecturalDropdownOverlay.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(-36, 0, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(95)) { EasingFunction = ease };
            DropdownTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            ArchitecturalDropdownOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, -28, TimeSpan.FromMilliseconds(105)) { EasingFunction = ease };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80)) { EasingFunction = ease };
            fade.Completed += (s, e) =>
            {
                if (!_isDropdownOpen)
                {
                    ArchitecturalDropdownOverlay.Visibility = Visibility.Collapsed;
                }
            };
            DropdownTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            ArchitecturalDropdownOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    private void BtnArchitecturalMenu_Click(object sender, RoutedEventArgs e)
    {
        ToggleDropdownMenu();
    }

    private void DropdownBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ToggleDropdownMenu(false);
    }

    private void DropdownClose_Click(object sender, RoutedEventArgs e)
    {
        ToggleDropdownMenu(false);
    }

    private void DropdownItem_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag)
        {
            ToggleDropdownMenu(false);
            NavigateTo(tag);
        }
    }

    private void TopNavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag)
        {
            NavigateTo(tag);
        }
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
                var anim = new DoubleAnimation(expanded ? 0 : -90, TimeSpan.FromMilliseconds(140))
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
        catch (Exception ex)
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
        catch (Exception ex)
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
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.SaveMenuGroupExpansion");
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
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.UpdateTopNavActiveIndicator"); }
    }

    private void BtnToggleWindowMode_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowMode();
    }

    public void ToggleWindowMode()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (Width < 800 || Height < 600 || double.IsNaN(Width) || double.IsNaN(Height))
            {
                Width = 1240;
                Height = 820;
            }
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Left + ((workArea.Width - Width) / 2));
            Top = Math.Max(workArea.Top, workArea.Top + ((workArea.Height - Height) / 2));
        }
        else
        {
            WindowState = WindowState.Maximized;
        }

        UpdateWindowModeButton();

        try
        {
            AppSettings? settings = ServiceContainer.GetOptional<SettingsService>()?.Settings;
            if (settings != null)
            {
                settings.LaunchMode = (WindowState == WindowState.Maximized)
                    ? nameof(LaunchMode.Maximized)
                    : nameof(LaunchMode.Normal);
                _ = ServiceContainer.GetOptional<SettingsService>()?.SaveAsync();
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.ToggleWindowMode.PersistLaunchMode"); }
    }

    private void OnLanguageChanged(string? lang)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateWindowModeButton();
            ApplyVersionTexts();
        });
    }

    private void ApplyVersionTexts()
    {
        string template = ServiceContainer.GetOptional<LocalizationService>()?.Get("App_StudioSubtitle")
            ?? (TryFindResource("App_StudioSubtitle") as string ?? "App_StudioSubtitle");
        TxtStudioSubtitle.Text = string.Format(template, UpdateCheckService.GetCurrentVersion());
    }

    private void UpdateWindowModeButton()
    {
        if (IconWindowMode == null || TxtWindowMode == null || BtnToggleWindowMode == null)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            IconWindowMode.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowMinimize24;
            var loc = ServiceContainer.GetOptional<LocalizationService>();
            TxtWindowMode.Text = loc?.Get("Main_Windowed") ?? "FENÊTRÉ";
            BtnToggleWindowMode.ToolTip = loc?.Get("Main_WindowedTip") ?? "Passer en mode fenêtré (F11)";
        }
        else
        {
            IconWindowMode.Symbol = Wpf.Ui.Controls.SymbolRegular.FullScreenMaximize24;
            var loc = ServiceContainer.GetOptional<LocalizationService>();
            TxtWindowMode.Text = loc?.Get("Main_Fullscreen") ?? "PLEIN ÉCRAN";
            BtnToggleWindowMode.ToolTip = loc?.Get("Main_FullscreenTip") ?? "Passer en mode plein écran (F11)";
        }
    }

    private void TopNavigationBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(dep) != null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleWindowMode();
        }
        else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
            {
                Point mousePos = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                if (Width < 800 || Height < 600 || double.IsNaN(Width) || double.IsNaN(Height))
                {
                    Width = 1240;
                    Height = 820;
                }
                Left = mousePos.X - (Width / 2);
                Top = mousePos.Y - 20;
                UpdateWindowModeButton();
            }
            try { DragMove(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.TopNavigationBar_MouseDown.DragMove"); }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void ShowBetaWelcome()
    {
        BetaWelcomeOverlay.Opacity = 0;
        BetaWelcomeOverlay.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BetaWelcomeOverlay.BeginAnimation(OpacityProperty, fade);
        BetaWelcomeTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        _ = Dispatcher.InvokeAsync(() => BtnDismissBetaWelcome.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void DismissBetaWelcome()
    {
        if (BetaWelcomeOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var slide = new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            BetaWelcomeOverlay.Visibility = Visibility.Collapsed;
        };
        BetaWelcomeOverlay.BeginAnimation(OpacityProperty, fade);
        BetaWelcomeTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void BtnDismissBetaWelcome_Click(object sender, RoutedEventArgs e)
    {
        DismissBetaWelcome();
    }

    private void BetaWelcomeBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DismissBetaWelcome();
    }
}