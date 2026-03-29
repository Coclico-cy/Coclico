#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public class StartupProgress
{
    public string Status { get; set; } = string.Empty;
    public int Percent { get; set; }
}

public class StartupService
{
    public async Task RunStartupAsync(IProgress<StartupProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report(new StartupProgress { Status = "Chargement des paramètres…", Percent = 10 });
            await ServiceContainer.GetRequired<SettingsService>().LoadAsync().ConfigureAwait(false);

            progress?.Report(new StartupProgress { Status = "Initialisation des services de base…", Percent = 22 });
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await System.Windows.Application.Current.Dispatcher
                            .InvokeAsync(() => ServiceContainer.GetRequired<ThemeService>().ApplyCurrentSettings());
                    }
                    catch (Exception ex) { LoggingService.LogException(ex, "StartupService.ApplyTheme"); }
                }, ct),
                Task.Run(() =>
                {
                    try { ServiceContainer.GetRequired<LocalizationService>().SetLanguage(ServiceContainer.GetRequired<SettingsService>().Settings.Language); }
                    catch (Exception ex) { LoggingService.LogException(ex, "StartupService.SetLanguage"); }
                }, ct),
                Task.Run(() =>
                {
                    try { _ = ServiceContainer.GetOptional<ProcessWatcherService>(); }
                    catch (Exception ex) { LoggingService.LogException(ex, "StartupService.ProcessWatcherInit"); }
                }, ct),
                Task.Run(() =>
                {
                    try { ServiceContainer.GetRequired<ResourceGuardService>().Start(); }
                    catch (Exception ex) { LoggingService.LogException(ex, "StartupService.ResourceGuardInit"); }
                }, ct)
            ).ConfigureAwait(false);
            progress?.Report(new StartupProgress { Status = "Services de base initialisés…", Percent = 48 });

            progress?.Report(new StartupProgress { Status = "Vérification de la santé système…", Percent = 54 });
            try
            {
                var health = await new StartupHealthService().CheckAndRepairAsync().ConfigureAwait(false);
                if (!health.IsHealthy)
                    LoggingService.LogInfo($"StartupHealthService: {health.Message}");
                else
                    LoggingService.LogInfo("StartupHealthService: système opérationnel.");
            }
            catch (Exception ex) { LoggingService.LogException(ex, "StartupService.HealthCheck"); }

            progress?.Report(new StartupProgress { Status = "Analyse des applications installées…", Percent = 62 });
            try
            {
                await ServiceContainer.GetRequired<InstalledProgramsService>().GetAllInstalledProgramsAsync(cancellationToken: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { LoggingService.LogInfo("Installed programs scan cancelled"); }
            catch (Exception ex) { LoggingService.LogException(ex, "StartupService.InstalledPrograms"); }

            progress?.Report(new StartupProgress { Status = "Chargement du cache icônes…", Percent = 82 });
            try
            {
                var iconPaths = ServiceContainer.GetRequired<InstalledProgramsService>().GetMemoryCacheIconPaths();
                if (iconPaths.Count > 0)
                    _ = Coclico.Converters.FileIconToImageSourceConverter.PreloadAllAsync(iconPaths);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "StartupService.IconPreload"); }
            await Task.Delay(60, ct).ConfigureAwait(false);

            progress?.Report(new StartupProgress { Status = "Initialisation du Noyau Autonome…", Percent = 88 });
            try
            {
                var sourceRoot = AppDomain.CurrentDomain.BaseDirectory;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var twin = ServiceContainer.GetOptional<IStateValidator>();
                        var analyser = ServiceContainer.GetOptional<ISourceAnalyzer>();
                        if (analyser is not null)
                            await analyser.AnalyseAsync(sourceRoot).ConfigureAwait(false);
                        if (twin is not null)
                            await twin.IndexAsync(sourceRoot).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogException(ex, "StartupService.StateValidatorIndex");
                    }
                }, ct);

                var engine = ServiceContainer.GetOptional<IOptimizationEngine>();
                engine?.Start(ct);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "StartupService.NoyauAutonome"); }

            progress?.Report(new StartupProgress { Status = "Démarrage de l'interface…", Percent = 96 });
            await Task.Delay(120, ct).ConfigureAwait(false);

            progress?.Report(new StartupProgress { Status = "Prêt !", Percent = 100 });
            await Task.Delay(60, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { LoggingService.LogInfo("Startup cancelled"); throw; }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.RunStartupAsync");
            throw;
        }
    }
}
