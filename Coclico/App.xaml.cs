using System.Diagnostics;
using System.Runtime;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Coclico.Services;
using Coclico.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Coclico
{
    public partial class App : System.Windows.Application
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

            // Force hardware GPU rendering (D3D11) — no software fallback
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.Default;

            // Pre-warm the thread pool with all logical CPUs so scans start instantly
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
                    // Services with interfaces
                    services.AddSingleton<ICacheService, CacheService>();

                    // Services without interfaces (registered as concrete types)
                    services.AddSingleton<SettingsService>();
                    services.AddSingleton<InstalledProgramsService>();
                    services.AddSingleton<ProfileService>();
                    services.AddSingleton<ProcessWatcherService>();
                    services.AddSingleton<FlowExecutionService>();
                    services.AddSingleton<ThemeService>();
                    services.AddSingleton<LocalizationService>();
                    services.AddSingleton<AppResourceGuardService>();
                    services.AddSingleton<NetworkMonitorService>();
                    services.AddSingleton<AiChatService>();
                    services.AddSingleton<KeyboardShortcutsService>();
                    services.AddSingleton<StartupService>();
                    services.AddSingleton<FeatureExecutionEngine>();

                    // Transient services (no changes needed here)
                    services.AddTransient<CleaningService>();
                    services.AddTransient<DeepCleaningService>();
                    services.AddTransient<InstallerService>();
                    services.AddTransient<FlowChainService>();
                    services.AddTransient<FlowChainExecutionService>();
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

            // Show launcher options on first run
            var settings = SettingsService.Instance.Settings;
            if (settings.FirstRun)
            {
                var launcher = new Coclico.Views.LauncherWindow(
                    settings.LaunchAtStartup,
                    settings.MinimizeToTray);
                launcher.ShowDialog();

                settings.LaunchAtStartup  = launcher.LaunchAtStartup;
                settings.MinimizeToTray   = launcher.MinimizeToTray;
                settings.LaunchMode       = launcher.SelectedMode.ToString();
                settings.FirstRun         = false;
                await SettingsService.Instance.SaveAsync();
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // Apply launch mode from settings
            var launchMode = settings.LaunchMode;
            if (launchMode == "Minimized")
            {
                mainWindow.WindowState = System.Windows.WindowState.Minimized;
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

            // Apply full WPF-UI theme (ApplicationThemeManager.Apply) now that MainWindow has an HWND.
            // This must happen after Show() so the backdrop and theme are properly applied to the window.
            try { ThemeService.Instance.ApplyCurrentSettings(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.ApplyTheme"); }

            splash.Close();
            mainWindow.Activate();

            _ = Task.Run(MemoryCleanerService.TrimSelfWorkingSet);

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
                System.Windows.Application.Current?.Shutdown();
                return false;
            }

            System.Windows.Application.Current?.Shutdown();
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
            catch
            {
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { AppResourceGuardService.Instance.Stop(); AppResourceGuardService.Instance.Dispose(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeAppResourceGuard"); }

            try { ThemeService.Instance.Dispose(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeThemeService"); }

            try { AiChatService.Instance.Dispose(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeAiChat"); }

            try { ProcessWatcherService.Instance.Dispose(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeProcessWatcher"); }

            try { NetworkMonitorService.Instance.Dispose(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.DisposeNetworkMonitor"); }

            try { ServiceContainer.Shutdown(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.OnExit.ServiceContainerShutdown"); }

            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Silently ignore known WPF-UI TitleBar internal NullReferenceException (harmless library bug)
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
}
