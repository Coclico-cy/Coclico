using System.Windows;
using System.Windows.Threading;
using Coclico.Services;
namespace Coclico;

public partial class App
{
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
                            try { window.Hide(); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.HideWindow"); }
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
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.HideWindows"); }

        try { CentralUnitWatchdog.Current.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.Watchdog"); }

        try { CentralUnitService.Current.Shutdown(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.CentralUnit"); }

        try { ServiceContainer.GetOptional<IntegrityRuntimeService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeIntegrityRuntime"); }

        try { ServiceContainer.GetOptional<ResourceGuardService>()?.Stop(); ServiceContainer.GetOptional<ResourceGuardService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeResourceGuard"); }

        try { ServiceContainer.GetOptional<ThemeService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeThemeService"); }

        try { (ServiceContainer.GetOptional<IAiService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeAiChat"); }

        try { ServiceContainer.GetOptional<ProcessWatcherService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeProcessWatcher"); }

        try { ServiceContainer.GetOptional<NetworkMonitorService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeNetworkMonitor"); }

        try { ServiceContainer.GetOptional<TrayService>()?.Dispose(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.DisposeTrayService"); }

        try { ServiceContainer.Shutdown(); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.Shutdown.ServiceContainerShutdown"); }

        try { _ = Task.Run(async () => await LoggingService.ShutdownAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, "App.PerformApplicationShutdown.LoggingShutdown"); }
    }
}
