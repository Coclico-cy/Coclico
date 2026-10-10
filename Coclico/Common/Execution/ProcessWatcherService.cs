using System.Management;

namespace Coclico.Services;

public sealed class ProcessWatcherService : IDisposable
{
    private const string StartQuery = "SELECT * FROM __InstanceCreationEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_Process'";
    private const string StopQuery = "SELECT * FROM __InstanceDeletionEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_Process'";

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private readonly object _watcherLock = new();

    private EventHandler<string>? _processStarted;
    public event EventHandler<string>? ProcessStarted
    {
        add
        {
            lock (_watcherLock)
            {
                _processStarted += value;
                _startWatcher ??= CreateWatcher(StartQuery, OnProcessStarted, "ProcessWatcherService.StartWatcherInit");
            }
        }
        remove
        {
            lock (_watcherLock)
            {
                _processStarted -= value;
                if (_processStarted == null)
                {
                    DisposeWatcher(ref _startWatcher, OnProcessStarted, "ProcessWatcherService.StartWatcherDispose");
                }
            }
        }
    }

    private EventHandler<string>? _processStopped;
    public event EventHandler<string>? ProcessStopped
    {
        add
        {
            lock (_watcherLock)
            {
                _processStopped += value;
                _stopWatcher ??= CreateWatcher(StopQuery, OnProcessStopped, "ProcessWatcherService.StopWatcherInit");
            }
        }
        remove
        {
            lock (_watcherLock)
            {
                _processStopped -= value;
                if (_processStopped == null)
                {
                    DisposeWatcher(ref _stopWatcher, OnProcessStopped, "ProcessWatcherService.StopWatcherDispose");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_watcherLock)
        {
            DisposeWatcher(ref _startWatcher, OnProcessStarted, "ProcessWatcherService.DisposeStart");
            DisposeWatcher(ref _stopWatcher, OnProcessStopped, "ProcessWatcherService.DisposeStop");
        }
        GC.SuppressFinalize(this);
    }

    private static ManagementEventWatcher? CreateWatcher(string query, EventArrivedEventHandler handler, string context)
    {
        try
        {
            var watcher = new ManagementEventWatcher(new WqlEventQuery(query));
            watcher.EventArrived += handler;
            watcher.Start();
            return watcher;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
            return null;
        }
    }

    private static void DisposeWatcher(ref ManagementEventWatcher? watcher, EventArrivedEventHandler handler, string context)
    {
        try
        {
            if (watcher != null)
            {
                watcher.EventArrived -= handler;
                watcher.Stop();
                watcher.Dispose();
                watcher = null;
            }
        }
        catch (Exception exSwallow)
        {
            LoggingService.LogException(exSwallow, context);
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        string? processName = ExtractProcessName(e, "ProcessWatcherService.OnProcessStarted");
        if (processName != null)
        {
            _processStarted?.Invoke(this, processName);
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        string? processName = ExtractProcessName(e, "ProcessWatcherService.OnProcessStopped");
        if (processName != null)
        {
            _processStopped?.Invoke(this, processName);
        }
    }

    private static string? ExtractProcessName(EventArrivedEventArgs e, string context)
    {
        try
        {
            using var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            return targetInstance["Name"]?.ToString();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
            return null;
        }
    }
}