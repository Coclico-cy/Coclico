using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
            catch (Exception ex) { LoggingService.LogException(ex, "App.AppDomainUnhandledException.Report"); }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogException(e.Exception);
            try
            {
                ServiceContainer.GetOptional<IAppErrorManager>()?.Report(AppErrorCode.SYS_StartupFailed, e.Exception);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "App.UnobservedTaskException.Report"); }
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
                new FrameworkPropertyMetadata { DefaultValue = 60 });

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
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkDiscoveryService, Coclico.Services.Network.NetworkDiscoveryService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkSafetyService, Coclico.Services.Network.NetworkSafetyService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkSnapshotService, Coclico.Services.Network.NetworkSnapshotService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkBenchmarkService, Coclico.Services.Network.NetworkBenchmarkService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkScoringService, Coclico.Services.Network.NetworkScoringService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkWatchdogService, Coclico.Services.Network.NetworkWatchdogService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkMemoryService, Coclico.Services.Network.NetworkMemoryService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkDiagnosticsService, Coclico.Services.Network.NetworkDiagnosticsService>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkExperimentEngine, Coclico.Services.Network.NetworkExperimentEngine>();
                    _ = services.AddSingleton<Coclico.Services.Network.INetworkOptimizerService, Coclico.Services.Network.NetworkOptimizerService>();
                    _ = services.AddSingleton<Coclico.Services.AI.MultiProviderClient>();
                    _ = services.AddSingleton<Coclico.Services.AI.AiToolExecutionService>();
                    _ = services.AddSingleton<IAiService, AiChatService>();
                    _ = services.AddSingleton<KeyboardShortcutsService>();
                    _ = services.AddSingleton<StartupService>();
                    _ = services.AddSingleton<ISmartMemoryDaemonService, SmartMemoryDaemonService>();
                    _ = services.AddSingleton<UpdateManager>();
                    _ = services.AddSingleton<UpdateCheckService>();

                    _ = services.AddSingleton<TrayService>();
                    _ = services.AddTransient<CleaningService>();
                    _ = services.AddTransient<InstallerService>();
                    _ = services.AddTransient<StartupHealthService>();
                    _ = services.AddTransient<UserAccountService>();
                    _ = services.AddSingleton<SystemHealthService>();
                    _ = services.AddSingleton<CustomShortcutsService>();
                    _ = services.AddSingleton<HomeCustomizationService>();

                    _ = services.AddSingleton<IDialogService, DialogService>();

                    _ = services.AddSingleton<Coclico.Modules.Power.Services.CpuPowerService>();
                    _ = services.AddSingleton<Coclico.Modules.Power.Services.AdaptivePowerService>();

                    _ = services.AddTransient<Coclico.ViewModels.DashboardViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.CleaningViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.SettingsViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.HealthDefenseViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.InstallerViewModel>();
                    _ = services.AddTransient<Coclico.ViewModels.NetworkOptimizerViewModel>();
                });

                string appVersion = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ?? "unknown";
                LoggingService.LogInfo($"Coclico v{appVersion} starting — DI container built successfully");
            }
            catch (Exception ex)
            {
                LogException(ex);
                LoggingService.LogException(ex, "App.DIInit");

                _ = System.Windows.MessageBox.Show(
                    "Le conteneur de services de Coclico n'a pas pu être initialisé.\n" +
                    "Consultez les journaux dans %AppData%\\Coclico\\logs pour le détail.\n\n" + ex.Message,
                    "Coclico — erreur critique", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            var splash = new SplashWindow();
            splash.Show();

            await splash.RunStartupAsync();

            try
            {
                IAuditLog audit = ServiceContainer.GetRequired<IAuditLog>();
                int retentionDays = ServiceContainer.GetRequired<SettingsService>().Settings.AuditRetentionDays;
                audit.Prune(TimeSpan.FromDays(retentionDays));
                LoggingService.LogInfo($"[App] Audit pruned — rétention {retentionDays} jours.");
            }
            catch (Exception ex) { LoggingService.LogException(ex, "App.AuditPrune"); }

            AppSettings settings = ServiceContainer.GetRequired<SettingsService>().Settings;
            if (settings.FirstRun)
            {
                settings.FirstRun = false;
                await ServiceContainer.GetRequired<SettingsService>().SaveAsync();
            }

            try { ServiceContainer.GetRequired<LocalizationService>().SetLanguage(settings.Language); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.ApplyLanguage"); }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            string launchModeSetting = settings.LaunchMode;
            if (launchModeSetting == nameof(LaunchMode.Minimized))
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Show();
            }
            else if (launchModeSetting == nameof(LaunchMode.Tray))
            {
                mainWindow.Show();
                mainWindow.Hide();
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
            catch (Exception ex) { LoggingService.LogException(ex, "App.ApplyTheme"); }

            splash.Close();
            _ = mainWindow.Activate();

            _ = Task.Run(MemoryCleanerService.TrimSelfWorkingSet);

            try { ServiceContainer.GetRequired<ISmartMemoryDaemonService>().Start(); }
            catch (Exception ex) { LoggingService.LogException(ex, "App.StartSmartMemoryDaemon"); }

            try
            {
                var updateCheckService = ServiceContainer.GetRequired<UpdateCheckService>();
                updateCheckService.Start();
                LoggingService.LogInfo("UpdateCheckService started - startup check, then roughly every 6 hours");
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "App.UpdateCheckServiceInit");
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            LogException(ex);
            LoggingService.LogException(ex, "App.OnStartupFatal");
            _ = MessageBox.Show($"Erreur au démarrage de Coclico :\n{ex.Message}\n\nConsultez les journaux dans %APPDATA%\\Coclico\\logs.", "Coclico - Erreur de démarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void RemoveLegacyAiCredentialVault()
    {
        string vaultPath = Path.Combine(
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
        catch (Exception ex)
        {
            LoggingService.LogWarning($"Could not remove the legacy AI API-key vault: {ex.Message}");
        }
    }

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartAsAdmin(string[]? args = null)
    {
        if (TryRestartAsAdmin(args))
        {
            LoggingService.LogInfo("Coclico relancé en mode administrateur. Arrêt immédiat de l'instance courante.");
            PerformApplicationShutdown();
            System.Threading.Thread.Sleep(200);
            Environment.Exit(0);
            return true;
        }
        return false;
    }

    internal static void PerformApplicationShutdown()
    {
        try
        {
            if (Current != null)
            {
                Dispatcher dispatcher = Current.Dispatcher;
                if (dispatcher != null)
                {
                    static void hideAction()
                    {
                        var windows = new System.Collections.Generic.List<Window>();
                        foreach (Window w in Current.Windows)
                        {
                            windows.Add(w);
                        }

                        foreach (Window window in windows)
                        {
                            try { window.Hide(); } catch (Exception ex) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.HideWindow"); }
                        }
                    }

                    if (dispatcher.CheckAccess())
                    {
                        hideAction();
                    }
                    else
                    {
                        dispatcher.Invoke(hideAction);
                    }
                }
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.HideWindows"); }

        try { ServiceContainer.GetOptional<ResourceGuardService>()?.Stop(); ServiceContainer.GetOptional<ResourceGuardService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeResourceGuard"); }

        try { ServiceContainer.GetOptional<ThemeService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeThemeService"); }

        try { (ServiceContainer.GetOptional<IAiService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeAiChat"); }

        try { ServiceContainer.GetOptional<ProcessWatcherService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeProcessWatcher"); }

        try { ServiceContainer.GetOptional<NetworkMonitorService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeNetworkMonitor"); }

        try { ServiceContainer.GetOptional<TrayService>()?.Dispose(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.DisposeTrayService"); }

        try { ServiceContainer.Shutdown(); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.Shutdown.ServiceContainerShutdown"); }

        try { _ = Task.Run(async () => await LoggingService.ShutdownAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.LoggingShutdown"); }
    }

    public static bool TryRestartAsAdmin(string[]? args = null)
    {
        try
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            string? dllPath = null;

            if (string.IsNullOrWhiteSpace(exePath) ||
                Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "Coclico.exe");
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                }
                else
                {
                    string? entryLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                    if (!string.IsNullOrWhiteSpace(entryLoc))
                    {
                        string? dir = Path.GetDirectoryName(entryLoc);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            string candidate2 = Path.Combine(dir, "Coclico.exe");
                            if (File.Exists(candidate2))
                            {
                                exePath = candidate2;
                            }
                        }
                    }
                }

                if (!File.Exists(exePath))
                {
                    string candidateDll = Path.Combine(AppContext.BaseDirectory, "Coclico.dll");
                    if (File.Exists(candidateDll))
                    {
                        dllPath = candidateDll;
                    }
                    else
                    {
                        string? entryLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                        if (!string.IsNullOrWhiteSpace(entryLoc) && File.Exists(entryLoc))
                        {
                            dllPath = entryLoc;
                        }
                    }
                }
            }

            string[] rawArgs = args ?? Environment.GetCommandLineArgs();
            if (args == null && rawArgs.Length > 0)
            {
                rawArgs = rawArgs.Skip(1).ToArray();
            }

            var argList = new System.Collections.Generic.List<string>(rawArgs);
            if (!argList.Any(a => a.Equals("--elevated", StringComparison.OrdinalIgnoreCase)))
            {
                argList.Add("--elevated");
            }

            string workingDirectory = SanitizeWorkingDirectory(AppContext.BaseDirectory);

            var psi = new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = workingDirectory
            };

            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                psi.FileName = ResolveDotNetHost();
                string dllQuoted = $"\"{dllPath}\"";
                string remaining = FormatCommandLineArgs(argList);
                psi.Arguments = string.IsNullOrWhiteSpace(remaining) ? dllQuoted : $"{dllQuoted} {remaining}";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    LoggingService.LogError($"[App.TryRestartAsAdmin] Fichier exécutable introuvable : '{exePath}'");
                    return false;
                }
                psi.FileName = exePath;
                psi.Arguments = FormatCommandLineArgs(argList);
            }

            LoggingService.LogInfo($"[App.TryRestartAsAdmin] Lancement de l'exécutable avec élévation runas : {psi.FileName} {psi.Arguments}");

            try
            {
                var process = Process.Start(psi);
                string pidStr;
                try { pidStr = process?.Id.ToString() ?? "détaché"; } catch { pidStr = "détaché"; }
                LoggingService.LogInfo($"[App.TryRestartAsAdmin] Processus lancé avec succès (PID: {pidStr}).");
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                LoggingService.LogInfo("Élévation UAC annulée ou refusée par l'utilisateur (code 1223).");
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "App.TryRestartAsAdmin.ProcessStart");
                return false;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "App.TryRestartAsAdmin");
            return false;
        }
    }

    internal static string SanitizeWorkingDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return Environment.CurrentDirectory;
        }

        string? root = Path.GetPathRoot(baseDir);
        return string.Equals(baseDir, root, StringComparison.OrdinalIgnoreCase)
            ? baseDir
            : baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static string ResolveDotNetHost()
    {
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(procPath) &&
            Path.GetFileNameWithoutExtension(procPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(procPath))
        {
            return procPath;
        }

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string candidateInRoot = Path.Combine(dotnetRoot, "dotnet.exe");
            if (File.Exists(candidateInRoot))
            {
                return candidateInRoot;
            }
        }

        string pfDotNet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(pfDotNet))
        {
            return pfDotNet;
        }

        string pfX86DotNet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe");
        if (File.Exists(pfX86DotNet))
        {
            return pfX86DotNet;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string pathPart in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(pathPart, "dotnet.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "App.ResolveDotNetHost.PathCandidate"); }
            }
        }

        return "dotnet.exe";
    }

    internal static string FormatCommandLineArgs(System.Collections.Generic.IEnumerable<string> args)
    {
        var formatted = new System.Collections.Generic.List<string>();
        foreach (string arg in args)
        {
            if (string.IsNullOrEmpty(arg))
            {
                formatted.Add("\"\"");
            }
            else if (arg.Contains(' ') || arg.Contains('\"') || arg.Contains('\t') || arg.Contains('\n') || arg.Contains('\r'))
            {
                var sb = new System.Text.StringBuilder();
                _ = sb.Append('"');
                int backslashCount = 0;
                foreach (char c in arg)
                {
                    if (c == '\\')
                    {
                        backslashCount++;
                    }
                    else if (c == '"')
                    {
                        _ = sb.Append('\\', (backslashCount * 2) + 1);
                        _ = sb.Append('"');
                        backslashCount = 0;
                    }
                    else
                    {
                        _ = sb.Append('\\', backslashCount);
                        _ = sb.Append(c);
                        backslashCount = 0;
                    }
                }
                _ = sb.Append('\\', backslashCount * 2);
                _ = sb.Append('"');
                formatted.Add(sb.ToString());
            }
            else
            {
                formatted.Add(arg);
            }
        }
        return string.Join(" ", formatted);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PerformApplicationShutdown();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is NullReferenceException &&
            (e.Exception.StackTrace?.Contains("TitleBar.HwndSourceHook") == true))
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
