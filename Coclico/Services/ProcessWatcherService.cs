#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace Coclico.Services;

public class ProcessWatcherService : IDisposable
{

    private readonly ManagementEventWatcher? _startWatcher;
    private readonly ManagementEventWatcher? _stopWatcher;

    public event EventHandler<string>? ProcessStarted;
    public event EventHandler<string>? ProcessStopped;

    public ProcessWatcherService()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_Process'"));
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProcessWatcherService.StartWatcherInit");
        }

        try
        {
            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_Process'"));
            _stopWatcher.EventArrived += OnProcessStopped;
            _stopWatcher.Start();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProcessWatcherService.StopWatcherInit");
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = targetInstance["Name"]?.ToString();
            if (processName != null)
                ProcessStarted?.Invoke(this, processName);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProcessWatcherService.OnProcessStarted");
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = targetInstance["Name"]?.ToString();
            if (processName != null)
                ProcessStopped?.Invoke(this, processName);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProcessWatcherService.OnProcessStopped");
        }
    }

    public void KillProcess(string processName)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)))
            {
                using (process)
                {
                    try { process.Kill(); }
                    catch (Exception ex) { LoggingService.LogException(ex, "ProcessWatcherService.KillProcess.Kill"); }
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "ProcessWatcherService.KillProcess");
        }
    }

    public void Dispose()
    {
        if (_startWatcher != null) _startWatcher.EventArrived -= OnProcessStarted;
        if (_stopWatcher != null) _stopWatcher.EventArrived -= OnProcessStopped;
        _startWatcher?.Stop();
        _startWatcher?.Dispose();
        _stopWatcher?.Stop();
        _stopWatcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}
