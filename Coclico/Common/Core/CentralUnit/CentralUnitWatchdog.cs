namespace Coclico.Services;

public sealed class CentralWatchdogSnapshot
{
    public required string Subsystem { get; init; }

    public required TimeSpan MaxSilence { get; init; }

    public DateTime? LastBeatUtc { get; init; }

    public long BeatCount { get; init; }

    public long StaleCount { get; init; }

    public bool IsStale { get; init; }
}

public sealed class CentralUnitWatchdog : IDisposable
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(60);

    public static CentralUnitWatchdog Current { get; } = new(
        CentralUnitService.Current.Flags,
        CentralUnitService.Current.Activity);

    private readonly CentralUnitFlagStore _flags;
    private readonly CentralUnitActivity _activity;
    private readonly TimeSpan _checkInterval;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, SubsystemState> _subsystems = new(StringComparer.Ordinal);
    private Action<CentralActivityLevel>? _levelChangedHandler;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public CentralUnitWatchdog(CentralUnitFlagStore flags, CentralUnitActivity activity, TimeSpan? checkInterval = null)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _checkInterval = checkInterval ?? DefaultCheckInterval;
    }

    public void Register(string subsystem, TimeSpan maxSilence, Action? revive = null)
    {
        if (string.IsNullOrWhiteSpace(subsystem) || maxSilence <= TimeSpan.Zero)
        {
            return;
        }

        lock (_stateLock)
        {
            _subsystems[subsystem] = new SubsystemState(maxSilence, revive, DateTime.UtcNow);
        }
    }

    public void Unregister(string subsystem)
    {
        lock (_stateLock)
        {
            _ = _subsystems.Remove(subsystem);
        }
    }

    public void Beat(string subsystem)
    {
        lock (_stateLock)
        {
            if (_subsystems.TryGetValue(subsystem, out SubsystemState? state))
            {
                state.LastBeatUtc = DateTime.UtcNow;
                state.BeatCount++;
                state.IsStale = false;
            }
        }
    }

    public int CheckAll()
    {
        List<(string Subsystem, TimeSpan Silence, Action? Revive)> stale = [];
        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, SubsystemState> entry in _subsystems)
            {
                DateTime reference = entry.Value.LastBeatUtc ?? entry.Value.RegisteredUtc;
                if (now - reference <= entry.Value.MaxSilence)
                {
                    continue;
                }

                entry.Value.IsStale = true;
                entry.Value.StaleCount++;
                stale.Add((entry.Key, now - reference, entry.Value.Revive));
            }
        }

        foreach ((string subsystem, TimeSpan silence, Action? revive) in stale)
        {
            LoggingService.LogWarning($"CentralUnit : watchdog '{subsystem}' silencieux depuis {(int)silence.TotalSeconds} s.");
            try
            {
                revive?.Invoke();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"CentralUnitWatchdog.Revive.{subsystem}");
            }
        }

        return stale.Count;
    }

    public List<CentralWatchdogSnapshot> GetSnapshot()
    {
        lock (_stateLock)
        {
            return _subsystems
                .Select(entry => new CentralWatchdogSnapshot
                {
                    Subsystem = entry.Key,
                    MaxSilence = entry.Value.MaxSilence,
                    LastBeatUtc = entry.Value.LastBeatUtc,
                    BeatCount = entry.Value.BeatCount,
                    StaleCount = entry.Value.StaleCount,
                    IsStale = entry.Value.IsStale
                })
                .OrderBy(snapshot => snapshot.Subsystem, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_loopTask != null)
            {
                return;
            }

            _levelChangedHandler = level =>
            {
                if (level != CentralActivityLevel.ExternalPressure)
                {
                    _flags.Schedule(CentralFlags.WatchdogCheckDue, DateTime.UtcNow);
                }
            };
            _activity.LevelChanged += _levelChangedHandler;

            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunCheckLoopAsync(_loopCts.Token));
        }

        _flags.Schedule(CentralFlags.WatchdogCheckDue, DateTime.UtcNow);
    }

    public void Stop()
    {
        Action<CentralActivityLevel>? handler;
        CancellationTokenSource? loopCts;
        lock (_stateLock)
        {
            handler = _levelChangedHandler;
            _levelChangedHandler = null;
            loopCts = _loopCts;
            _loopCts = null;
            _loopTask = null;
        }

        if (handler != null)
        {
            _activity.LevelChanged -= handler;
        }

        loopCts?.Cancel();
        loopCts?.Dispose();
        _flags.CancelSchedule(CentralFlags.WatchdogCheckDue);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunCheckLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CentralUnitService.ApplyCurrentThreadBackgroundQos();
            try
            {
                await _flags.WaitAsync(CentralFlags.WatchdogCheckDue, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CentralUnitService.ApplyCurrentThreadBackgroundQos();
            _ = CheckAll();

            TimeSpan interval = _activity.AdaptInterval(_checkInterval);
            if (interval > TimeSpan.Zero)
            {
                _flags.Schedule(CentralFlags.WatchdogCheckDue, DateTime.UtcNow + interval);
            }
        }
    }

    private sealed class SubsystemState(TimeSpan maxSilence, Action? revive, DateTime registeredUtc)
    {
        public TimeSpan MaxSilence { get; } = maxSilence;

        public Action? Revive { get; } = revive;

        public DateTime RegisteredUtc { get; } = registeredUtc;

        public DateTime? LastBeatUtc { get; set; }

        public long BeatCount { get; set; }

        public long StaleCount { get; set; }

        public bool IsStale { get; set; }
    }
}
