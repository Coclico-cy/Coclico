namespace Coclico.Services;

public sealed class SmartMemoryDaemonService : ISmartMemoryDaemonService
{
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PressureSampleInterval = TimeSpan.FromSeconds(5);
    private const string Subsystem = "RamDaemon";

    private readonly SettingsService _settingsService;
    private readonly object _stateLock = new();
    private readonly Action<CentralActivityLevel> _onActivityChanged;

    private bool _isEnabled;
    private AutoCleanMode _mode = AutoCleanMode.Interval;
    private int _value = 15;
    private bool _protectForeground = true;
    private MemoryCleanerService.CleanProfile _profile = MemoryCleanerService.CleanProfile.Smart;
    private long _totalSessionFreedBytes;
    private DateTime? _nextScheduledCleanUtc;
    private DateTime _cooldownUntilUtc = DateTime.MinValue;
    private CancellationTokenSource? _cts;
    private Task? _daemonLoopTask;
    private bool _disposed;

    public bool IsEnabled { get { lock (_stateLock) { return _isEnabled; } } }
    public AutoCleanMode Mode { get { lock (_stateLock) { return _mode; } } }
    public int Value { get { lock (_stateLock) { return _value; } } }
    public bool ProtectForeground { get { lock (_stateLock) { return _protectForeground; } } }
    public MemoryCleanerService.CleanProfile Profile { get { lock (_stateLock) { return _profile; } } }
    public DateTime? LastCleanUtc
    {
        get
        {
            lock (_stateLock)
            {
                return field;
            }
        }

        private set;
    }
    public long LastFreedBytes
    {
        get
        {
            lock (_stateLock)
            {
                return field;
            }
        }

        private set;
    }
    public long TotalSessionFreedBytes { get { lock (_stateLock) { return _totalSessionFreedBytes; } } }

    private string _statusMessage = "Prêt.";
    public string StatusMessage
    {
        get { lock (_stateLock) { return _statusMessage; } }
        private set { lock (_stateLock) { _statusMessage = value; } }
    }

    public TimeSpan? TimeUntilNextClean
    {
        get
        {
            if (!_isEnabled || _mode != AutoCleanMode.Interval || !_nextScheduledCleanUtc.HasValue)
            {
                return null;
            }

            TimeSpan rem = _nextScheduledCleanUtc.Value - DateTime.UtcNow;
            return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
        }
    }

    public event Action<AutoCleanResultEventArgs>? CleanExecuted;
    public event Action<string>? StatusChanged;

    public SmartMemoryDaemonService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _onActivityChanged = _ =>
            CentralUnitService.Current.Flags.Schedule(CentralFlags.RamDaemonCycleDue, DateTime.UtcNow);

        AppSettings s = _settingsService.Settings;
        _isEnabled = s.AutoCleanEnabled;
        _mode = ParseModeSetting(s.AutoCleanMode);
        _value = _mode switch
        {
            AutoCleanMode.ThresholdPercent => Math.Clamp(s.AutoCleanThresholdPercent, 10, 99),
            _ => Math.Clamp(s.AutoCleanIntervalMinutes, 1, 1440)
        };
        _protectForeground = s.AutoCleanProtectForeground;
        _profile = s.AutoCleanProfile switch
        {
            "Quick" => MemoryCleanerService.CleanProfile.Quick,
            "Normal" => MemoryCleanerService.CleanProfile.Normal,
            "Deep" => MemoryCleanerService.CleanProfile.Deep,
            _ => MemoryCleanerService.CleanProfile.Smart
        };

        if (_isEnabled && _mode == AutoCleanMode.Interval)
        {
            _nextScheduledCleanUtc = DateTime.UtcNow.AddMinutes(_value);
        }
    }

    public void Configure(bool enabled, AutoCleanMode mode, int value, bool protectForeground = true, MemoryCleanerService.CleanProfile profile = MemoryCleanerService.CleanProfile.Smart)
    {
        lock (_stateLock)
        {
            _isEnabled = enabled;
            _mode = mode;
            _profile = profile;
            _value = mode switch
            {
                AutoCleanMode.ThresholdPercent => Math.Clamp(value, 10, 99),
                _ => Math.Clamp(value, 1, 1440)
            };
            _protectForeground = protectForeground;

            if (_isEnabled)
            {
                _nextScheduledCleanUtc = _mode == AutoCleanMode.Interval ? DateTime.UtcNow.AddMinutes(_value) : null;
                _cooldownUntilUtc = DateTime.MinValue;
            }
            else
            {
                _nextScheduledCleanUtc = null;
            }

            _settingsService.Settings.AutoCleanEnabled = _isEnabled;
            _settingsService.Settings.AutoCleanMode = ModeSettingToString(_mode);
            _settingsService.Settings.AutoCleanProfile = _profile.ToString();
            if (_mode == AutoCleanMode.ThresholdPercent)
            {
                _settingsService.Settings.AutoCleanThresholdPercent = _value;
            }
            else
            {
                _settingsService.Settings.AutoCleanIntervalMinutes = _value;
            }

            _settingsService.Settings.AutoCleanProtectForeground = _protectForeground;
            _settingsService.Save();

            if (_isEnabled && (_daemonLoopTask == null || _daemonLoopTask.IsCompleted))
            {
                StartLoopLocked();
            }

            UpdateStatusText();
            CentralUnitService.Current.Flags.Schedule(CentralFlags.RamDaemonCycleDue, DateTime.UtcNow);
        }

        LoggingService.LogInfo($"[SmartMemoryDaemon] Reconfiguré en direct: Actif={enabled}, Mode={mode}, Valeur={value}, Protection1erPlan={protectForeground}, Profil={profile}");
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_daemonLoopTask != null && !_daemonLoopTask.IsCompleted)
            {
                return;
            }

            StartLoopLocked();
            LoggingService.LogInfo("[SmartMemoryDaemon] Daemon démarré avec succès.");
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            CentralUnitWatchdog.Current.Unregister(Subsystem);
            CentralUnitService.Current.Activity.LevelChanged -= _onActivityChanged;
            CentralUnitService.Current.Flags.CancelSchedule(CentralFlags.RamDaemonCycleDue);
            _cts?.Cancel();
            _daemonLoopTask = null;
            LoggingService.LogInfo("[SmartMemoryDaemon] Daemon arrêté.");
        }
    }

    public async Task<MemoryCleanerService.CleanResult> ForceCleanNowAsync(CancellationToken ct = default)
    {
        LoggingService.LogInfo("[SmartMemoryDaemon] Nettoyage manuel forcé.");
        MemoryCleanerService.CleanResult res = await MemoryCleanerService.SmartCleanAsync(
            protectForeground: _protectForeground,
            purgeStandbyOnly: false,
            ct: ct).ConfigureAwait(false);

        RecordCleanSuccess(res.TotalFreed, AutoCleanMode.Interval, "Nettoyage forcé instantané");
        return res;
    }

    private void StartLoopLocked()
    {
        _cts = new CancellationTokenSource();
        CentralUnitService.Current.Activity.LevelChanged -= _onActivityChanged;
        CentralUnitService.Current.Activity.LevelChanged += _onActivityChanged;
        CentralUnitWatchdog.Current.Register(Subsystem, TimeSpan.FromMinutes(5),
            () => CentralUnitService.Current.Flags.Schedule(CentralFlags.RamDaemonCycleDue, DateTime.UtcNow));
        _daemonLoopTask = Task.Run(() => RunDaemonLoopAsync(_cts.Token));
        CentralUnitService.Current.Flags.Schedule(CentralFlags.RamDaemonCycleDue, DateTime.UtcNow);
    }

    private async Task RunDaemonLoopAsync(CancellationToken ct)
    {
        CentralUnitService.ApplyCurrentThreadBackgroundQos();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ = await CentralUnitService.Current.Flags.WaitAsync(CentralFlags.RamDaemonCycleDue, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CentralUnitService.ApplyCurrentThreadBackgroundQos();

            DateTime? nextWakeUtc;
            try
            {
                nextWakeUtc = await EvaluateCycleAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
                CentralUnitWatchdog.Current.Beat(Subsystem);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "SmartMemoryDaemonService.Loop");
                CentralCircuitBreaker.Current.RecordFailure(Subsystem, ex);
                CentralErrorTriage.Current.Record(ex, "SmartMemoryDaemonService.Loop");
                nextWakeUtc = DateTime.UtcNow + StatusRefreshInterval;
            }

            if (nextWakeUtc.HasValue)
            {
                CentralUnitService.Current.Flags.Schedule(CentralFlags.RamDaemonCycleDue, nextWakeUtc.Value);
            }
        }
    }

    private async Task<DateTime?> EvaluateCycleAsync(DateTime now, CancellationToken ct)
    {
        if (!_isEnabled)
        {
            SetStatusMessage("Nettoyage automatique inactif.");
            return null;
        }

        bool isCoolingDown = now < _cooldownUntilUtc;

        switch (_mode)
        {
            case AutoCleanMode.Interval:
                {
                    if (!_nextScheduledCleanUtc.HasValue)
                    {
                        _nextScheduledCleanUtc = now.AddMinutes(_value);
                    }

                    TimeSpan rem = _nextScheduledCleanUtc.Value - now;
                    if (rem <= TimeSpan.Zero)
                    {
                        if (!isCoolingDown)
                        {
                            await TriggerAutoCleanAsync(AutoCleanMode.Interval, ct).ConfigureAwait(false);
                            _nextScheduledCleanUtc = DateTime.UtcNow.AddMinutes(_value);
                        }
                        return DateTime.UtcNow + StatusRefreshInterval;
                    }

                    SetStatusMessage($"Actif • Prochain cycle dans {FormatTimeSpan(rem)}");
                    TimeSpan wait = rem > StatusRefreshInterval ? StatusRefreshInterval : rem;
                    return now + wait;
                }

            case AutoCleanMode.ThresholdPercent:
                {
                    MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
                    if (ram.PhysUsedPercent >= _value)
                    {
                        if (!isCoolingDown)
                        {
                            await TriggerAutoCleanAsync(AutoCleanMode.ThresholdPercent, ct).ConfigureAwait(false);
                            _cooldownUntilUtc = DateTime.UtcNow.AddSeconds(120);
                        }
                        else
                        {
                            int waitSec = Math.Max(1, (int)(_cooldownUntilUtc - now).TotalSeconds);
                            SetStatusMessage($"Actif • Seuil dépassé ({ram.PhysUsedPercent:F0}% >= {_value}%) • Temporisation anti-spam ({waitSec}s)");
                        }
                    }
                    else
                    {
                        SetStatusMessage($"Actif • Seuil surveillé : > {_value}% (Actuel : {ram.PhysUsedPercent:F1}%)");
                    }

                    TimeSpan sample = CentralUnitService.Current.Activity.AdaptInterval(PressureSampleInterval);
                    return sample > TimeSpan.Zero ? now + sample : null;
                }

            case AutoCleanMode.Hybrid:
                {
                    MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
                    bool lowFreeRam = ram.AvailPhysBytes < 1200L * 1024 * 1024;
                    bool highPressure = ram.PhysUsedPercent >= 78.0;

                    if ((lowFreeRam || highPressure) && !isCoolingDown)
                    {
                        await TriggerAutoCleanAsync(AutoCleanMode.Hybrid, ct).ConfigureAwait(false);
                        _cooldownUntilUtc = DateTime.UtcNow.AddSeconds(90);
                    }
                    else if (isCoolingDown)
                    {
                        SetStatusMessage($"Actif • Surveillance ISLC • Temporisation en cours");
                    }
                    else
                    {
                        SetStatusMessage($"Actif • Surveillance ISLC • RAM disponible : {MemoryCleanerService.FormatBytes(ram.AvailPhysBytes)}");
                    }

                    TimeSpan sample = CentralUnitService.Current.Activity.AdaptInterval(PressureSampleInterval);
                    return sample > TimeSpan.Zero ? now + sample : null;
                }

            default:
                return null;
        }
    }

    private async Task TriggerAutoCleanAsync(AutoCleanMode triggerMode, CancellationToken ct)
    {
        if (!CentralCircuitBreaker.Current.IsAllowed(Subsystem))
        {
            return;
        }

        try
        {
            LoggingService.LogInfo($"[SmartMemoryDaemon] Déclenchement automatique - Mode={triggerMode}, Profil={_profile}, Protection1erPlan={_protectForeground}");

            long freed = 0;
            if (_profile == MemoryCleanerService.CleanProfile.Smart)
            {
                bool purgeStandbyOnly = triggerMode == AutoCleanMode.Hybrid;
                MemoryCleanerService.CleanResult res = await MemoryCleanerService.SmartCleanAsync(
                    protectForeground: _protectForeground,
                    purgeStandbyOnly: purgeStandbyOnly,
                    ct: ct).ConfigureAwait(false);
                freed = res.TotalFreed;
            }
            else
            {
                MemoryCleanerService.CleanResult res = await MemoryCleanerService.CleanByProfileAsync(
                    _profile,
                    protectForeground: _protectForeground,
                    ct: ct).ConfigureAwait(false);
                freed = res.TotalFreed;
            }

            string profileLabel = _profile switch
            {
                MemoryCleanerService.CleanProfile.Quick => "Rapide",
                MemoryCleanerService.CleanProfile.Normal => "Normal",
                MemoryCleanerService.CleanProfile.Deep => "Profond",
                _ => "Intelligent"
            };

            string summary = $"{MemoryCleanerService.FormatBytes(freed)} libérés ({profileLabel})";
            RecordCleanSuccess(freed, triggerMode, summary);

            ToastService.Show($"[Auto-Clean {profileLabel}] {summary}");
            CentralCircuitBreaker.Current.RecordSuccess(Subsystem);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SmartMemoryDaemonService.TriggerAutoCleanAsync");
            CentralCircuitBreaker.Current.RecordFailure(Subsystem, ex);
            CentralErrorTriage.Current.Record(ex, "SmartMemoryDaemonService.TriggerAutoCleanAsync");
        }
    }

    private void RecordCleanSuccess(long freed, AutoCleanMode mode, string summary)
    {
        DateTime now = DateTime.UtcNow;
        lock (_stateLock)
        {
            LastCleanUtc = now;
            LastFreedBytes = freed;
            _totalSessionFreedBytes += freed;
        }

        UpdateStatusText();

        CleanExecuted?.Invoke(new AutoCleanResultEventArgs(
            Timestamp: now,
            TriggerMode: mode,
            FreedBytes: freed,
            Summary: summary));
    }

    private void SetStatusMessage(string msg)
    {
        lock (_stateLock)
        {
            if (_statusMessage == msg)
            {
                return;
            }

            _statusMessage = msg;
        }

        StatusChanged?.Invoke(msg);
    }

    private void UpdateStatusText()
    {
        if (!_isEnabled)
        {
            SetStatusMessage("Nettoyage automatique désactivé.");
            return;
        }

        switch (_mode)
        {
            case AutoCleanMode.Interval:
                TimeSpan? rem = TimeUntilNextClean;
                SetStatusMessage(rem.HasValue
                    ? $"Actif • Prochain cycle dans {FormatTimeSpan(rem.Value)}"
                    : $"Actif • Toutes les {_value} min");
                break;
            case AutoCleanMode.ThresholdPercent:
                MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
                SetStatusMessage($"Actif • Seuil surveillé : > {_value}% (Actuel : {ram.PhysUsedPercent:F1}%)");
                break;
            case AutoCleanMode.Hybrid:
                SetStatusMessage("Actif • Surveillance continue sans lag (ISLC)");
                break;
        }
    }

    public static AutoCleanMode ParseModeSetting(string? value)
    {
        return value switch
        {
            "ThresholdPercent" => AutoCleanMode.ThresholdPercent,
            "Threshold" => AutoCleanMode.ThresholdPercent,
            "Hybrid" => AutoCleanMode.Hybrid,
            _ => AutoCleanMode.Interval
        };
    }

    public static string ModeSettingToString(AutoCleanMode mode)
    {
        return mode switch
        {
            AutoCleanMode.ThresholdPercent => nameof(AutoCleanMode.ThresholdPercent),
            AutoCleanMode.Hybrid => nameof(AutoCleanMode.Hybrid),
            _ => nameof(AutoCleanMode.Interval)
        };
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes:D2}m {ts.Seconds:D2}s";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CentralUnitWatchdog.Current.Unregister(Subsystem);
        CentralUnitService.Current.Activity.LevelChanged -= _onActivityChanged;
        CentralUnitService.Current.Flags.CancelSchedule(CentralFlags.RamDaemonCycleDue);
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
