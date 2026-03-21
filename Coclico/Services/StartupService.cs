using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public class StartupProgress
    {
        public string Status { get; set; } = string.Empty;
        public int Percent { get; set; }
    }

    public class StartupService
    {
        private static readonly Lazy<StartupService> _lazy = new(() => new StartupService());
        public static StartupService Instance => _lazy.Value;

        private StartupService() { }

        public async Task RunStartupAsync(IProgress<StartupProgress>? progress = null, CancellationToken ct = default)
        {
            try
            {
                progress?.Report(new StartupProgress { Status = "Chargement des paramètres…", Percent = 10 });
                await SettingsService.Instance.LoadAsync().ConfigureAwait(false);

                progress?.Report(new StartupProgress { Status = "Application du thème…", Percent = 22 });
                try
                {
                    await System.Windows.Application.Current.Dispatcher
                        .InvokeAsync(() => ThemeService.Instance.ApplyCurrentSettings());
                }
                catch (Exception ex) { LoggingService.LogException(ex, "StartupService.ApplyTheme"); }

                progress?.Report(new StartupProgress { Status = "Chargement de la localisation…", Percent = 34 });
                try { LocalizationService.Instance.SetLanguage(SettingsService.Instance.Settings.Language); } catch (Exception ex) { LoggingService.LogException(ex, "StartupService.SetLanguage"); }

                progress?.Report(new StartupProgress { Status = "Initialisation des services…", Percent = 48 });
                try { _ = ProcessWatcherService.Instance; } catch (Exception ex) { LoggingService.LogException(ex, "StartupService.ProcessWatcherInit"); }
                try { AppResourceGuardService.Instance.Start(); } catch (Exception ex) { LoggingService.LogException(ex, "StartupService.AppResourceGuardInit"); }

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
                    await InstalledProgramsService.Instance.GetAllInstalledProgramsAsync(cancellationToken: ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { LoggingService.LogInfo("Installed programs scan cancelled"); }
                catch (Exception ex) { LoggingService.LogException(ex, "StartupService.InstalledPrograms"); }

                progress?.Report(new StartupProgress { Status = "Chargement du cache icônes…", Percent = 82 });
                try
                {
                    var iconPaths = InstalledProgramsService.Instance.GetMemoryCacheIconPaths();
                    if (iconPaths.Count > 0)
                        _ = Coclico.Converters.FileIconToImageSourceConverter.PreloadAllAsync(iconPaths);
                }
                catch (Exception ex) { LoggingService.LogException(ex, "StartupService.IconPreload"); }
                await Task.Delay(60, ct).ConfigureAwait(false);

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
}
