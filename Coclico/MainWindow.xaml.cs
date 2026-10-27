using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.Views;
using Wpf.Ui.Controls;

namespace Coclico;

public partial class MainWindow : FluentWindow
{
    private static class Motion
    {
        public const double PageSlideDistance = 10;
        public const double DropdownOpenDistance = 36;
        public const double DropdownCloseDistance = 28;
        public const double WelcomeSlideInDistance = 20;
        public const double ChatPanelWidth = 540;

        public static readonly TimeSpan NavFadeOut = TimeSpan.FromMilliseconds(70);
        public static readonly TimeSpan NavSlideIn = TimeSpan.FromMilliseconds(160);
        public static readonly TimeSpan ChatOpen = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan ChatClose = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan DropdownSlideOpen = TimeSpan.FromMilliseconds(145);
        public static readonly TimeSpan DropdownFadeOpen = TimeSpan.FromMilliseconds(95);
        public static readonly TimeSpan DropdownSlideClose = TimeSpan.FromMilliseconds(105);
        public static readonly TimeSpan DropdownFadeClose = TimeSpan.FromMilliseconds(80);
        public static readonly TimeSpan ChevronRotate = TimeSpan.FromMilliseconds(140);
        public static readonly TimeSpan WelcomeOpen = TimeSpan.FromMilliseconds(280);
        public static readonly TimeSpan WelcomeClose = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan WindowFadeIn = TimeSpan.FromMilliseconds(400);
    }



    private static string L(string key) => Application.Current?.TryFindResource(key) as string ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key) ?? key;

    private TrayService? _trayService;
    private readonly UserAccountService _userService;
    private readonly KeyboardShortcutsService _hotkeyService = ServiceContainer.GetRequired<KeyboardShortcutsService>();

    private readonly Dictionary<string, UIElement> _viewCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _isChatOpen = false;
    private bool _isDropdownOpen = false;
    private GitHubRelease? _pendingUpdate;

    public RadioButton? FindNavButton(string? tag)
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

        RecordStigmergyTransition("Home");

        try
        {
            SettingsService ss = ServiceContainer.GetRequired<SettingsService>();
            AppSettings s = ss.Settings;

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
        InitializeNotifications();

        UserNameText.Text = _userService.DisplayName;
        UserNameText.Visibility = Visibility.Collapsed;

        StateChanged += MainWindow_StateChanged;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (WelcomeOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                DismissWelcome();
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

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (IsVisible)
            {
                CentralUnitService.Current.SetWindowVisible(true);
                CentralUnitService.Current.SetTrayMinimized(false);
            }
            else
            {
                CentralUnitService.Current.SetWindowVisible(false);
                CentralUnitService.Current.SetTrayMinimized(true);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.IsVisibleChanged"); }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            try { MemoryCleanerService.TrimSelfWorkingSet(); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MainWindow_StateChanged.TrimWorkingSet"); }
            try { CentralUnitService.Current.SetWindowVisible(false); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MainWindow_StateChanged.SetWindowVisible"); }
        }
        else if (IsVisible)
        {
            try { CentralUnitService.Current.SetWindowVisible(true); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MainWindow_StateChanged.SetWindowVisible"); }
        }
        UpdateWindowModeButton();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, Motion.WindowFadeIn)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
        _ = AnimatePageIn();

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

            _ = Dispatcher.InvokeAsync(ShowWelcome, System.Windows.Threading.DispatcherPriority.Loaded);
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
                try { CentralUnitService.Current.SetTrayMinimized(true); }
                catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.OnClosing.SetTrayMinimized"); }
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
                try { CentralUnitService.Current.SetTrayMinimized(true); } catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.MinimizeToTrayNow.SetTrayMinimized"); }
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
















    private void ProfileBorder_MouseDown(object sender, RoutedEventArgs e)
    {
        var profileWindow = new ProfileWindow(_userService)
        {
            Owner = this
        };
        _ = profileWindow.ShowDialog();
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
        TxtStudioSubtitle.Text = string.Empty;
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




}
