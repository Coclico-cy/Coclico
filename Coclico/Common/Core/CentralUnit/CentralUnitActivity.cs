namespace Coclico.Services;

public enum CentralActivityLevel
{
    StartingUp,
    Active,
    Background,
    Idle,
    ExternalPressure
}

public sealed class CentralUnitActivity
{
    private readonly object _stateLock = new();
    private bool _started;
    private bool _windowVisible;
    private bool _trayMinimized;
    private bool _aiInference;
    private bool _externalPressure;

    public CentralActivityLevel Level { get; private set; } = CentralActivityLevel.StartingUp;

    public event Action<CentralActivityLevel>? LevelChanged;

    public bool IsAiInferenceActive
    {
        get
        {
            lock (_stateLock)
            {
                return _aiInference;
            }
        }
    }

    public void MarkStarted()
    {
        lock (_stateLock)
        {
            _started = true;
        }

        RecomputeLevel();
    }

    public void SetWindowVisible(bool visible)
    {
        lock (_stateLock)
        {
            _windowVisible = visible;
        }

        RecomputeLevel();
    }

    public void SetTrayMinimized(bool minimized)
    {
        lock (_stateLock)
        {
            _trayMinimized = minimized;
        }

        RecomputeLevel();
    }

    public void SetAiInference(bool active)
    {
        lock (_stateLock)
        {
            _aiInference = active;
        }

        RecomputeLevel();
    }

    public void SetExternalPressure(bool underPressure)
    {
        lock (_stateLock)
        {
            _externalPressure = underPressure;
        }

        RecomputeLevel();
    }

    public TimeSpan AdaptInterval(TimeSpan baseInterval)
    {
        return Level switch
        {
            CentralActivityLevel.Active or CentralActivityLevel.StartingUp => baseInterval,
            CentralActivityLevel.Background => TimeSpan.FromTicks(baseInterval.Ticks * 3),
            CentralActivityLevel.Idle => TimeSpan.FromTicks(baseInterval.Ticks * 10),
            CentralActivityLevel.ExternalPressure => Timeout.InfiniteTimeSpan,
            _ => baseInterval
        };
    }

    private void RecomputeLevel()
    {
        CentralActivityLevel level;
        lock (_stateLock)
        {
            if (_externalPressure)
            {
                level = CentralActivityLevel.ExternalPressure;
            }
            else if (!_started)
            {
                level = CentralActivityLevel.StartingUp;
            }
            else if (_windowVisible || _aiInference)
            {
                level = CentralActivityLevel.Active;
            }
            else if (!_trayMinimized)
            {
                level = CentralActivityLevel.Background;
            }
            else
            {
                level = CentralActivityLevel.Idle;
            }
        }

        if (level == Level)
        {
            return;
        }

        Level = level;
        LevelChanged?.Invoke(level);
    }
}
