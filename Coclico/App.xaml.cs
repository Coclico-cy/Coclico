using System.IO;
using System.Reflection;
using System.Runtime;
using System.Windows;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coclico;

public partial class App : Application
{
    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogException(e.ExceptionObject as Exception);
            try
            {
                ServiceContainer.GetOptional<IAppErrorManager>()?.Report(AppErrorCode.SYS_StartupFailed, e.ExceptionObject as Exception);
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.AppDomainUnhandledException.Report"); }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogException(e.Exception);
            try
            {
                ServiceContainer.GetOptional<IAppErrorManager>()?.Report(AppErrorCode.SYS_StartupFailed, e.Exception);
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.UnobservedTaskException.Report"); }
            e.SetObserved();
        };
    }

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        InitializeComponent();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            RemoveLegacyAiCredentialVault();

            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.Default;

            int cpuCount = Environment.ProcessorCount;
            _ = System.Threading.ThreadPool.SetMinThreads(cpuCount, Math.Min(8, cpuCount));

            GCSettings.LatencyMode = GCLatencyMode.Interactive;

            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = 30 });

            try
            {
                ServiceContainer.Build(services =>
                {
                    _ = services.AddSingleton<IAppErrorManager, AppErrorManager>();
                    _ = services.AddSingleton<IAuditLog, AuditLogService>();
                    _ = services.AddSingleton<ISecurityPolicy, SecurityPolicyService>();
                    _ = services.AddSingleton<ICacheService, CacheService>();
                    _ = services.AddSingleton<IResourceAllocator, ResourceAllocatorService>();

                    _ = services.AddSingleton<SettingsService>();
                    _ = services.AddSingleton<InstalledProgramsService>();
                    _ = services.AddSingleton<ProfileService>();
                    _ = services.AddSingleton<ProcessWatcherService>();
                    _ = services.AddSingleton<ThemeService>();
                    _ = services.AddSingleton<LocalizationService>();
                    _ = services.AddSingleton<ResourceGuardService>();
                    _ = services.AddSingleton<NetworkMonitorService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkBenchmarkService, Coclico.Services.Network.NetworkBenchmarkService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkOptimizerService, Coclico.Services.Network.NetworkOptimizerService>();
                    _ = services.AddSingleton<Coclico.Services.AI.MultiProviderClient>();
                    _ = services.AddSingleton<Coclico.Services.AI.AiToolExecutionService>();
                    _ = services.AddSingleton<IAiService, AiChatService>();
                    _ = services.AddSingleton<KeyboardShortcutsService>();
                    _ = services.AddSingleton<StartupService>();
                    _ = services.AddSingleton<ISmartMemoryDaemonService, SmartMemoryDaemonService>();
                    _ = services.AddSingleton<UpdateManager>();
                    _ = services.AddSingleton<UpdateCheckService>();
                    _ = services.AddSingleton<IntegrityRuntimeService>();

                    _ = services.AddSingleton<TrayService>();
                    _ = services.AddSingleton<NotificationService>();
                    _ = services.AddTransient<Coclico.Modules.Cleaning.Services.CleaningService>();
                    _ = services.AddSingleton<Coclico.Modules.Apps.Services.AppCatalogService>();
                    _ = services.AddSingleton<Coclico.Modules.Apps.Services.WingetEngineService>();
                    _ = services.AddSingleton<Coclico.Modules.Apps.Services.GitHubToolService>();
                    _ = services.AddSingleton<Coclico.Modules.Apps.Services.AppShortcutsService>();
                    _ = services.AddTransient<StartupHealthService>();
                    _ = services.AddTransient<UserAccountService>();
                    _ = services.AddSingleton<SystemHealthService>();
                    _ = services.AddSingleton<CustomShortcutsService>();
                    _ = services.AddSingleton<HomeCustomizationService>();

                    _ = services.AddSingleton<IDialogService, DialogService>();

                    _ = services.AddSingleton<Coclico.Modules.Power.Services.CpuPowerService>();
                    _ = services.AddSingleton<Coclico.Modules.Power.Services.AdaptivePowerService>();

                    _ = services.AddSingleton<Coclico.Modules.Diagnostics.Services.DiagnosticPrivacySanitizer>();
                    _ = services.AddSingleton<Coclico.Modules.Diagnostics.Services.IDiagnosticHistoryService, Coclico.Modules.Diagnostics.Services.DiagnosticHistoryService>();
                    _ = services.AddSingleton<Coclico.Modules.Diagnostics.Services.Hardware.IHardwareTelemetryService, Coclico.Modules.Diagnostics.Services.Hardware.WmiHardwareTelemetryService>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.WindowsHealthCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.CpuCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.GpuCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.MemoryCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.StorageCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.NetworkCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.PowerBatteryCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.DeviceCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.ProcessesCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.SecurityCheck>();
                    _ = services.AddTransient<Coclico.Modules.Diagnostics.Services.IDiagnosticCheck, Coclico.Modules.Diagnostics.Services.Checks.MotherboardCheck>();
                    _ = services.AddSingleton<Coclico.Modules.Diagnostics.Services.IDiagnosticOrchestrator, Coclico.Modules.Diagnostics.Services.DiagnosticOrchestrator>();

                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.SecureProcessRunner>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.WingetUpdateService>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.DriverUpdateService>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.SystemUpdateService>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.RestorePointService>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Services.UpdateScanService>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.ViewModels.UpdateCenterViewModel>();
                    _ = services.AddSingleton<Coclico.Modules.UpdateCenter.Views.UpdateCenterView>();

                    _ = services.AddTransient<Coclico.ViewModels.DashboardViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.CleaningViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.SettingsViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.HealthDefenseViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.AppsViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.NetworkOptimizerViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.RamCleanerViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.ProgramsViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.DiskAnalyzerViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.DiagnosticViewModel>();
                });

                string appVersion = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ?? "unknown";
                LoggingService.LogInfo($"Coclico v{appVersion} starting DI container built successfully");
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LogException(ex);
                LoggingService.LogException(ex, "App.DIInit");

                _ = System.Windows.MessageBox.Show(
                    "Le conteneur de services de Coclico n'a pas pu être initialisé.\n" +
                    "Consultez les journaux dans %AppData%\\Coclico\\logs pour le détail.\n\n" + ex.Message,
                    "Coclico erreur critique", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            try { CentralUnitService.Current.Initialize(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.CentralUnitInit"); }

            try { CentralUnitWatchdog.Current.Start(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.WatchdogStart"); }

            try { NativeLibraryGuard.Initialize(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.NativeLibraryGuardInit"); }

            try { ServiceContainer.GetRequired<IntegrityRuntimeService>().Start(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.IntegrityRuntimeStart"); }

            var splash = new SplashWindow();
            splash.Show();

            await splash.RunStartupAsync();

            try { CentralUnitService.Current.MarkStartupCompleted(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.MarkStartupCompleted"); }

            try
            {
                IAuditLog audit = ServiceContainer.GetRequired<IAuditLog>();
                int retentionDays = ServiceContainer.GetRequired<SettingsService>().Settings.AuditRetentionDays;
                audit.Prune(TimeSpan.FromDays(retentionDays));
                LoggingService.LogInfo($"[App] Audit pruned rétention {retentionDays} jours.");
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.AuditPrune"); }

            AppSettings settings = ServiceContainer.GetRequired<SettingsService>().Settings;
            if (settings.FirstRun)
            {
                settings.FirstRun = false;
                await ServiceContainer.GetRequired<SettingsService>().SaveAsync();
            }

            try { ServiceContainer.GetRequired<LocalizationService>().SetLanguage(settings.Language); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.ApplyLanguage"); }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            string launchModeSetting = settings.LaunchMode;
            if (launchModeSetting == nameof(LaunchMode.Minimized))
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Show();
                try { CentralUnitService.Current.SetWindowVisible(false); }
                catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.LaunchModeMinimized"); }
            }
            else if (launchModeSetting == nameof(LaunchMode.Tray))
            {
                mainWindow.Show();
                mainWindow.Hide();
                try { CentralUnitService.Current.SetTrayMinimized(true); }
                catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.LaunchModeTray"); }
            }
            else if (launchModeSetting == nameof(LaunchMode.Maximized))
            {
                mainWindow.WindowState = WindowState.Maximized;
                mainWindow.Show();
            }
            else
            {
                mainWindow.Show();
            }

            try { ServiceContainer.GetRequired<ThemeService>().ApplyCurrentSettings(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.ApplyTheme"); }

            splash.Close();
            _ = mainWindow.Activate();

            _ = Task.Run(MemoryCleanerService.TrimSelfWorkingSet);

            try { ServiceContainer.GetRequired<ISmartMemoryDaemonService>().Start(); }
            catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.StartSmartMemoryDaemon"); }

            try
            {
                var updateCheckService = ServiceContainer.GetRequired<UpdateCheckService>();
                updateCheckService.Start();
                LoggingService.LogInfo("UpdateCheckService started - startup check, then roughly every 6 hours");
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, "App.UpdateCheckServiceInit");
            }

            base.OnStartup(e);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LogException(ex);
            LoggingService.LogException(ex, "App.OnStartupFatal");
            _ = MessageBox.Show($"Erreur au démarrage de Coclico :\n{ex.Message}\n\nConsultez les journaux dans %APPDATA%\\Coclico\\logs.", "Coclico - Erreur de démarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void RemoveLegacyAiCredentialVault()
    {
        string vaultPath = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico",
            "ai_vault.dat");

        try
        {
            if (File.Exists(vaultPath))
            {
                File.Delete(vaultPath);
                LoggingService.LogInfo("Removed the legacy AI API-key vault after cloud providers were removed.");
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            LoggingService.LogWarning($"Could not remove the legacy AI API-key vault: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PerformApplicationShutdown();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is NullReferenceException &&
            (e.Exception.StackTrace?.Contains("TitleBar.HwndSourceHook") is true))
        {
            LoggingService.LogDebug($"[App] TitleBar.HwndSourceHook NRE avalée (connue) : {e.Exception.Message}");
            e.Handled = true;
            return;
        }

        LogException(e.Exception);

        string message = "Une erreur imprévue est survenue. Veuillez redémarrer l'application.\n" +
                      "Détails : " + e.Exception.Message;

        if (e.Exception is System.Windows.Markup.XamlParseException xamlEx && xamlEx.InnerException != null)
        {
            message += "\nXAML : " + xamlEx.InnerException.Message;
        }

        _ = MessageBox.Show(message, "Coclico Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogException(Exception? ex)
    {
        if (ex == null)
        {
            return;
        }

        LoggingService.LogException(ex, "App.UnhandledException");
    }
}
