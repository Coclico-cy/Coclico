#nullable enable
using System;
using System.Diagnostics;
using System.Runtime;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Coclico.Services;
using Coclico.Views;

namespace Coclico;

public partial class App : Application
{
    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogException(e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogException(e.Exception);
            e.SetObserved();
        };
    }

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        InitializeComponent();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!EnsureRunAsAdmin(e))
            return;

        System.Windows.Media.RenderOptions.ProcessRenderMode =
            System.Windows.Interop.RenderMode.Default;

        int cpuCount = Environment.ProcessorCount;
        System.Threading.ThreadPool.SetMinThreads(cpuCount, cpuCount);

        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = 60 });

        try
        {
            ServiceContainer.Build(services =>
            {
                services.AddSingleton<ISecurityPolicy, SecurityPolicyService>();
                services.AddSingleton<ICacheService, CacheService>();
                services.AddSingleton<IDynamicTracer, DynamicTracerService>();
                services.AddSingleton<IResourceAllocator, ResourceAllocatorService>();
                services.AddSingleton<IRollbackService, RollbackService>();
                services.AddSingleton<ISourceAnalyzer, SourceAnalyzerService>();
                services.AddSingleton<IStateValidator, StateValidatorService>();
                services.AddSingleton<ICodePatcher, CodePatcherService>();
services.AddSingleton<IOptimizationEngine>(sp =>
                    new OptimizationEngineService(
                        sp.GetRequiredService<IDynamicTracer>(),
                        sp.GetRequiredService<IRollbackService>(),
                        sp.GetRequiredService<IResourceAllocator>(),
                        sp.GetRequiredService<IAiService>(),
                        sp.GetRequiredService<ISourceAnalyzer>(),
                        sp.GetRequiredService<IAuditLog>()));

                services.AddSingleton<SettingsService>();
                services.AddSingleton<InstalledProgramsService>();
                services.AddSingleton<ProfileService>();
                services.AddSingleton<ProcessWatcherService>();
                services.AddSingleton<WorkflowService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<ResourceGuardService>();
                services.AddSingleton<NetworkMonitorService>();
                services.AddSingleton<IAiService, AiChatService>();
                services.AddSingleton<KeyboardShortcutsService>();
                services.AddSingleton<StartupService>();
                services.AddSingleton<FeatureExecutionEngine>();

                services.AddTransient<CleaningService>();
                services.AddTransient<InstallerService>();
                services.AddTransient<WorkflowService>();
                services.AddTransient<StartupHealthService>();
                services.AddTransient<UserAccountService>();
            });

            LoggingService.LogInfo("Coclico v2.0 starting — DI container built successfully");
        }
        catch (Exception ex)
        {
            LogException(ex);
            LoggingService.LogException(ex, "App.DIInit");
        }

        var splash = new SplashWindow();
        splash.Show();

        await splash.RunStartupAsync();

        try
        {
            var audit         = ServiceContainer.GetRequired<IAuditLog>();
            int retentionDays = ServiceContainer.GetRequired<SettingsService>().Settings.AuditRetentionDays;
            audit.Prune(TimeSpan.FromDays(retentionDays));
            LoggingService.LogInfo($"[App] Audit pruned — rétention {retentionDays} jours.");
        }
        catch (Exception ex) { LoggingService.LogException(ex, "App.AuditPrune"); }

        var settings = ServiceContainer.GetRequired<SettingsService>().Settings;
        if (settings.FirstRun)
        {
            var launcher = new LauncherWindow(
                settings.LaunchAtStartup,
                settings.MinimizeToTray);
            launcher.ShowDialog();

            settings.LaunchAtStartup  = launcher.LaunchAtStartup;
            settings.MinimizeToTray   = launcher.MinimizeToTray;
            settings.LaunchMode       = launcher.SelectedMode.ToString();
            settings.FirstRun         = false;
            await ServiceContainer.GetRequired<SettingsService>().SaveAsync();
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        var launchMode = settings.LaunchMode;
        if (launchMode == "Minimized")
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Show();
        }
        else if (launchMode == "Tray")
        {
            mainWindow.Show();
            mainWindow.Hide();
        }
        else
        {
            mainWindow.Show();
        }

        try { ServiceContainer.GetRequired<ThemeService>().ApplyCurrentSettings(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.ApplyTheme"); }

        splash.Close();
        mainWindow.Activate();

        _ = Task.Run(MemoryCleanerService.TrimSelfWorkingSet);

        try
        {
            var updateCheckService = UpdateCheckService.GetInstance(
                new UpdateManager(new NullLogger<UpdateManager>(), ServiceContainer.GetRequired<SettingsService>()),
                new NullLogger<UpdateCheckService>(),
                ServiceContainer.GetRequired<SettingsService>()
            );
            updateCheckService.Start();
            LoggingService.LogInfo("UpdateCheckService started - checking every 5 minutes");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "App.UpdateCheckServiceInit");
        }

        base.OnStartup(e);
    }

    private static bool EnsureRunAsAdmin(StartupEventArgs e)
    {
        if (IsRunningAsAdministrator())
            return true;

        var prompt = new AdminPromptWindow();
        var answer = prompt.ShowDialog();

        if (answer == true && TryRestartAsAdmin(e.Args))
        {
            Application.Current?.Shutdown();
            return false;
        }

        Application.Current?.Shutdown();
        return false;
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRestartAsAdmin(string[] args)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            var quotedArgs = string.Empty;
            if (args.Length > 0)
            {
                var parts = new string[args.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    var escaped = args[i].Replace("\"", "\\\"");
                    parts[i] = $"\"{escaped}\"";
                }
                quotedArgs = string.Join(" ", parts);
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = quotedArgs,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "App.TryRestartAsAdmin");
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { ServiceContainer.GetOptional<ResourceGuardService>()?.Stop(); ServiceContainer.GetOptional<ResourceGuardService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeResourceGuard"); }

        try { ServiceContainer.GetOptional<ThemeService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeThemeService"); }

        try { (ServiceContainer.GetOptional<IAiService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeAiChat"); }

        try { ServiceContainer.GetOptional<ProcessWatcherService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeProcessWatcher"); }

        try { ServiceContainer.GetOptional<NetworkMonitorService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeNetworkMonitor"); }

        try { ServiceContainer.GetOptional<IDynamicTracer>()?.FlushToDisk(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DynamicTracerFlush"); }

        try { ServiceContainer.Shutdown(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.ServiceContainerShutdown"); }

        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is NullReferenceException &&
            (e.Exception.StackTrace?.Contains("TitleBar.HwndSourceHook") == true))
        {
            e.Handled = true;
            return;
        }

        LogException(e.Exception);

        var message = "Une erreur imprévue est survenue. Veuillez redémarrer l'application.\n" +
                      "Détails : " + e.Exception.Message;

        if (e.Exception is System.Windows.Markup.XamlParseException xamlEx && xamlEx.InnerException != null)
            message += "\nXAML : " + xamlEx.InnerException.Message;

        MessageBox.Show(message, "Coclico Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogException(Exception? ex)
    {
        if (ex == null) return;
        LoggingService.LogException(ex, "App.UnhandledException");
    }
}
