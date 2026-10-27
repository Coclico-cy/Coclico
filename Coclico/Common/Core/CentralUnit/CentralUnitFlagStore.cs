using System.Collections.Concurrent;

namespace Coclico.Services;

public static class CentralFlags
{
    public const string StartupCompleted = "Central.Startup.Completed";
    public const string PressureSample = "Central.Pressure.Sample";
    public const string AiModelUnload = "Central.Ai.ModelUnload";
    public const string RamSnapshotDue = "Central.Ram.Snapshot.Due";
    public const string NetworkStatsDue = "Central.Network.Stats.Due";
    public const string RamDaemonCycleDue = "Central.Ram.Daemon.Cycle.Due";
    public const string DashboardRefreshDue = "Central.Dashboard.Refresh.Due";
    public const string GuardTickDue = "Central.Guard.Tick.Due";
    public const string UiViewPreloadDue = "Central.Ui.ViewPreload.Due";
    public const string WatchdogCheckDue = "Central.Watchdog.Check.Due";
}

public sealed class CentralUnitFlagStore : IDisposable
{
    private static readonly TimeSpan MaxSchedulerSleep = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waiters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _criticalFlags = new(StringComparer.Ordinal);
    private readonly object _scheduleLock = new();
    private readonly Dictionary<string, DateTime> _schedules = new(StringComparer.Ordinal);
    private DateTime _nextDueUtc = DateTime.MaxValue;
    private CancellationTokenSource _wakeCts = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _wakeups;

    public long Wakeups => Interlocked.Read(ref _wakeups);

    public int ScheduledCount
    {
        get
        {
            lock (_scheduleLock)
            {
                return _schedules.Count;
            }
        }
    }

    public void SetCriticalFlags(params string[] flags)
    {
        foreach (string flag in flags)
        {
            _criticalFlags[flag] = 0;
        }
    }

    public Task<bool> WaitAsync(string flag, CancellationToken cancellationToken = default)
    {
        if (_pending.TryRemove(flag, out _))
        {
            return Task.FromResult(true);
        }

        TaskCompletionSource<bool> waiter = _waiters.GetOrAdd(flag, static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (_pending.TryRemove(flag, out _))
        {
            _ = _waiters.TryRemove(flag, out _);
            return Task.FromResult(true);
        }

        if (cancellationToken.CanBeCanceled)
        {
            _ = cancellationToken.Register(() =>
            {
                if (_waiters.TryGetValue(flag, out TaskCompletionSource<bool>? current) && ReferenceEquals(current, waiter))
                {
                    if (_waiters.TryRemove(flag, out TaskCompletionSource<bool>? removed) && ReferenceEquals(removed, waiter))
                    {
                        _ = waiter.TrySetCanceled();
                    }
                }
                else
                {
                    _ = waiter.TrySetCanceled();
                }
            });
        }

        return waiter.Task;
    }

    public void Set(string flag)
    {
        Interlocked.Increment(ref _wakeups);
        if (_waiters.TryRemove(flag, out TaskCompletionSource<bool>? waiter))
        {
            _ = waiter.TrySetResult(true);
            return;
        }

        _pending[flag] = 0;
    }

    public void Clear(string flag)
    {
        _ = _pending.TryRemove(flag, out _);
        if (_waiters.TryRemove(flag, out TaskCompletionSource<bool>? waiter))
        {
            _ = waiter.TrySetResult(false);
        }
    }

    public void Schedule(string flag, DateTime dueUtc)
    {
        lock (_scheduleLock)
        {
            _schedules[flag] = dueUtc;
            if (dueUtc < _nextDueUtc)
            {
                _nextDueUtc = dueUtc;
                WakeSchedulerLocked();
            }
        }
    }

    public void CancelSchedule(string flag)
    {
        lock (_scheduleLock)
        {
            _schedules.Remove(flag);
        }
    }

    public void PostponeNonCritical(TimeSpan delay)
    {
        lock (_scheduleLock)
        {
            DateTime threshold = DateTime.UtcNow + delay;
            List<string> postponed = new();
            foreach (KeyValuePair<string, DateTime> schedule in _schedules.Where(schedule => schedule.Value <= threshold && !_criticalFlags.ContainsKey(schedule.Key)))
            {
                postponed.Add(schedule.Key);
            }

            foreach (string flag in postponed)
            {
                _schedules[flag] = _schedules[flag] + delay;
            }
        }
    }

    public void StartScheduler()
    {
        lock (_scheduleLock)
        {
            if (_loopTask != null)
            {
                return;
            }

            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunSchedulerLoopAsync(_loopCts.Token));
        }
    }

    public void StopScheduler()
    {
        CancellationTokenSource? loopCts;
        lock (_scheduleLock)
        {
            loopCts = _loopCts;
            _loopCts = null;
            _loopTask = null;
        }

        loopCts?.Cancel();
    }

    public void Dispose()
    {
        StopScheduler();
        lock (_scheduleLock)
        {
            _wakeCts.Dispose();
        }
    }

    private void WakeSchedulerLocked()
    {
        _wakeCts.Cancel();
        _wakeCts.Dispose();
        _wakeCts = new CancellationTokenSource();
    }

    private async Task RunSchedulerLoopAsync(CancellationToken loopToken)
    {
        while (!loopToken.IsCancellationRequested)
        {
            TimeSpan delay;
            CancellationTokenSource? linked = null;
            try
            {
                lock (_scheduleLock)
                {
                    DateTime next = _schedules.Count == 0 ? DateTime.MaxValue : _schedules.Values.Min();
                    _nextDueUtc = next;

                    TimeSpan remaining = next == DateTime.MaxValue ? MaxSchedulerSleep : next - DateTime.UtcNow;
                    delay = remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining > MaxSchedulerSleep ? MaxSchedulerSleep : remaining;
                    linked = CancellationTokenSource.CreateLinkedTokenSource(loopToken, _wakeCts.Token);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            using (linked)
            {
                if (delay == TimeSpan.Zero)
                {
                    FireDueSchedules();
                    continue;
                }

                try
                {
                    await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) { _ = ex; }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }

    private void FireDueSchedules()
    {
        List<string> due;
        lock (_scheduleLock)
        {
            DateTime now = DateTime.UtcNow;
            due = _schedules
                .Where(schedule => schedule.Value <= now)
                .Select(schedule => schedule.Key)
                .ToList();

            foreach (string flag in due)
            {
                _schedules.Remove(flag);
            }
        }

        foreach (string flag in due)
        {
            Set(flag);
        }
    }
}
