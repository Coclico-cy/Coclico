using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Coclico.Services;

public class StartupProgress
{
    public string Status { get; set; } = string.Empty;
    public string SubDetail { get; set; } = string.Empty;
    public int Percent { get; set; }
    public string LogEntry { get; set; } = string.Empty;
    public string? CpuInfo { get; set; }
    public string? RamInfo { get; set; }
    public string? GpuInfo { get; set; }
    public string? SysInfo { get; set; }
}

public class StartupService
{
    public async Task RunStartupAsync(IProgress<StartupProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            await RunCoreAsync(progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LoggingService.LogInfo("Startup cancelled");
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.RunStartupAsync");
            throw;
        }
    }

    private async Task RunCoreAsync(IProgress<StartupProgress>? progress, CancellationToken ct)
    {
        bool isAdmin = IsAdministrator();
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();

        Report(progress, 12,
            "Démarrage des services...",
            "Initialisation de l'environnement",
            "Démarrage des services système",
            cpuInfo: $"{Environment.ProcessorCount} Cœurs • {arch}",
            sysInfo: BuildSystemInfo(isAdmin));

        await ServiceContainer.GetRequired<SettingsService>().LoadAsync().ConfigureAwait(false);

        LocalizationService? loc = ServiceContainer.GetOptional<LocalizationService>();

        Report(progress, 25,
            loc?.Get("Startup_MemOptimize") ?? "Optimisation de la mémoire...",
            loc?.Get("Startup_MemAnalyze") ?? "Analyse de la mémoire vive",
            loc?.Get("Startup_MemAnalyze") ?? "Analyse de la mémoire vive",
            ramInfo: BuildRamInfo(loc));

        TryRun(() => _ = ServiceContainer.GetOptional<ISmartMemoryDaemonService>(), "StartupService.MemoryDaemon");
        TryRun(() => ServiceContainer.GetRequired<ResourceGuardService>().Start(), "StartupService.ResourceGuard");

        Report(progress, 38,
            "Configuration de l'affichage...",
            "Accélération graphique matérielle",
            "Initialisation de l'affichage",
            gpuInfo: BuildGpuInfo());

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TryRun(
                () => ServiceContainer.GetRequired<ThemeService>().ApplyCurrentSettings(),
                "StartupService.ApplyTheme");
            TryRun(
                () => ServiceContainer.GetRequired<LocalizationService>()
                    .SetLanguage(ServiceContainer.GetRequired<SettingsService>().Settings.Language),
                "StartupService.SetLanguage");
        });

        Report(progress, 50,
            "Vérification des services système...",
            "Contrôle des services Windows",
            "Vérification des services Windows");

        try
        {
            _ = await ServiceContainer.GetRequired<StartupHealthService>()
                .CheckAndRepairAsync().ConfigureAwait(false);

            Report(progress, 52,
                "Services système vérifiés",
                "Services Windows opérationnels",
                "Services système opérationnels");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.HealthCheck");
        }

        Report(progress, 64,
            "Contrôle des composants...",
            "Vérification de la connectivité",
            "Vérification des composants réseau");

        Report(progress, 66,
            "Composants prêts",
            "Modules système connectés",
            "Composants prêts");

        Report(progress, 78,
            "Recherche des mises à jour...",
            "Vérification de la version",
            "Recherche des mises à jour");

        await TryAsync(async () =>
        {
            Task updateTask = Task.Run(async () =>
            {
                try
                {
                    var updateCheckService = ServiceContainer.GetRequired<UpdateCheckService>();
                    _ = await updateCheckService.CheckForUpdatesAsync().ConfigureAwait(false);
                }
                catch (Exception exInner)
                {
                    LoggingService.LogException(exInner, "StartupService.UpdateCheck");
                }
            });
            _ = await Task.WhenAny(updateTask, Task.Delay(500, ct)).ConfigureAwait(false);
        }, "StartupService.UpdateCheck");

        Report(progress, 80,
            loc?.Get("Startup_CheckUpdates") ?? "Vérification des mises à jour...",
            "v" + UpdateCheckService.GetCurrentVersion(),
            loc?.Get("Startup_VersionControl") ?? "Contrôle de version effectué");

        Report(progress, 90,
            loc?.Get("Startup_LoadingApps") ?? "Chargement des applications...",
            loc?.Get("Startup_IndexApps") ?? "Indexation des programmes installés",
            loc?.Get("Startup_IndexApps") ?? "Indexation des programmes");

        await TryAsync(async () =>
        {
            var programs = ServiceContainer.GetRequired<InstalledProgramsService>();
            _ = await programs.GetAllInstalledProgramsAsync(cancellationToken: ct).ConfigureAwait(false);
            List<string> iconPaths = programs.GetMemoryCacheIconPaths();
            if (iconPaths.Count > 0)
            {
                _ = Coclico.Converters.FileIconConverter.PreloadAllAsync(iconPaths);
            }
        }, "StartupService.AppsScan");

        Report(progress, 98,
            loc?.Get("Startup_Finalizing") ?? "Finalisation du démarrage...",
            loc?.Get("Startup_TrimFootprint") ?? "Optimisation de l'empreinte mémoire",
            loc?.Get("Startup_Finalizing") ?? "Finalisation du démarrage");

        TryRun(MemoryCleanerService.TrimSelfWorkingSet, "StartupService.TrimWorkingSet");

        await Task.Delay(100, ct).ConfigureAwait(false);

        Report(progress, 100,
            loc?.Get("Startup_Ready") ?? "Prêt !",
            loc?.Get("Startup_Opening") ?? "Ouverture de Coclico...",
            loc?.Get("Startup_Done") ?? "Démarrage terminé");

        await Task.Delay(200, ct).ConfigureAwait(false);
    }

    private static void Report(
        IProgress<StartupProgress>? progress,
        int percent,
        string status,
        string subDetail,
        string logEntry,
        string? cpuInfo = null,
        string? ramInfo = null,
        string? gpuInfo = null,
        string? sysInfo = null)
    {
        progress?.Report(new StartupProgress
        {
            Status = status,
            SubDetail = subDetail,
            Percent = percent,
            LogEntry = logEntry,
            CpuInfo = cpuInfo,
            RamInfo = ramInfo,
            GpuInfo = gpuInfo,
            SysInfo = sysInfo
        });
    }

    private static void TryRun(Action action, string context)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
        }
    }

    private static async Task TryAsync(Func<Task> action, string context)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using WindowsIdentity? id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.IsAdministrator");
            return false;
        }
    }

    private static string BuildSystemInfo(bool isAdmin)
    {
        string platform = Environment.OSVersion.Version.Major >= 10 ? "11/10" : "NT";
        return $"Win {platform} • {(isAdmin ? "Admin" : "User")}";
    }

    private static string BuildRamInfo(LocalizationService? loc)
    {
        MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
        double availGb = ram.AvailPhysBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = ram.TotalPhysBytes / (1024.0 * 1024.0 * 1024.0);
        return string.Format(loc?.Get("Startup_RamFree") ?? "{0:F1} Go Libre / {1:F0} Go", availGb, totalGb);
    }

    private static string BuildGpuInfo()
    {
        try
        {
            MemoryCleanerService.GpuInfo gpu = MemoryCleanerService.GetGpuInfo();
            string name = string.IsNullOrWhiteSpace(gpu.Name) ? "GPU Standard" : gpu.Name;
            if (name.Length > 22)
            {
                name = name[..22] + "…";
            }

            return gpu.IsIntegrated ? $"{name} (Intégré)" : $"{name} (Dédié)";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.BuildGpuInfo");
            return "DirectX DWM";
        }
    }
}