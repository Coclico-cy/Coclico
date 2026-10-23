namespace Coclico.Services;

public enum CentralCircuitState
{
    Closed,
    Open,
    HalfOpen
}

public sealed class CentralCircuitBreaker
{
    public static readonly TimeSpan DefaultOpenCooldown = TimeSpan.FromSeconds(30);

    public static CentralCircuitBreaker Current { get; } = new();

    private readonly object _stateLock = new();
    private readonly Dictionary<string, BreakerEntry> _entries = new(StringComparer.Ordinal);
    private readonly int _failureThreshold;
    private readonly TimeSpan _openCooldown;

    public event Action<string, CentralCircuitState>? StateChanged;

    public CentralCircuitBreaker(int failureThreshold = 3, TimeSpan? openCooldown = null)
    {
        _failureThreshold = Math.Max(1, failureThreshold);
        _openCooldown = openCooldown ?? DefaultOpenCooldown;
    }

    public CentralCircuitState GetState(string subsystem)
    {
        lock (_stateLock)
        {
            return _entries.TryGetValue(subsystem, out BreakerEntry? entry) ? entry.State : CentralCircuitState.Closed;
        }
    }

    public bool IsAllowed(string subsystem)
    {
        lock (_stateLock)
        {
            if (!_entries.TryGetValue(subsystem, out BreakerEntry? entry))
            {
                return true;
            }

            if (entry.State == CentralCircuitState.Open &&
                DateTime.UtcNow - entry.OpenedUtc >= _openCooldown)
            {
                entry.State = CentralCircuitState.HalfOpen;
                NotifyAndLog(subsystem, entry.State);
            }

            return entry.State != CentralCircuitState.Open;
        }
    }

    public void RecordSuccess(string subsystem)
    {
        CentralCircuitState? transition = null;
        lock (_stateLock)
        {
            if (!_entries.TryGetValue(subsystem, out BreakerEntry? entry))
            {
                return;
            }

            entry.FailureStreak = 0;
            if (entry.State != CentralCircuitState.Closed)
            {
                entry.State = CentralCircuitState.Closed;
                transition = CentralCircuitState.Closed;
            }
        }

        if (transition.HasValue)
        {
            NotifyAndLog(subsystem, transition.Value);
        }
    }

    public void RecordFailure(string subsystem, Exception? exception = null)
    {
        CentralCircuitState? transition = null;
        lock (_stateLock)
        {
            if (!_entries.TryGetValue(subsystem, out BreakerEntry? entry))
            {
                entry = new BreakerEntry();
                _entries[subsystem] = entry;
            }

            entry.FailureStreak++;
            if (entry.State == CentralCircuitState.HalfOpen)
            {
                entry.State = CentralCircuitState.Open;
                entry.OpenedUtc = DateTime.UtcNow;
                transition = CentralCircuitState.Open;
            }
            else if (entry.State == CentralCircuitState.Closed && entry.FailureStreak >= _failureThreshold)
            {
                entry.State = CentralCircuitState.Open;
                entry.OpenedUtc = DateTime.UtcNow;
                transition = CentralCircuitState.Open;
            }
        }

        if (transition == CentralCircuitState.Open)
        {
            NotifyAndLog(subsystem, CentralCircuitState.Open);
        }
    }

    public List<(string Subsystem, CentralCircuitState State, int FailureStreak)> GetSnapshot()
    {
        lock (_stateLock)
        {
            return _entries
                .Select(entry => (entry.Key, entry.Value.State, entry.Value.FailureStreak))
                .ToList();
        }
    }

    private void NotifyAndLog(string subsystem, CentralCircuitState state)
    {
        StateChanged?.Invoke(subsystem, state);
        if (state == CentralCircuitState.Open)
        {
            LoggingService.LogWarning($"CentralUnit : circuit breaker ouvert pour '{subsystem}' — quarantaine de {Math.Max(1, (int)_openCooldown.TotalSeconds)} s.");
        }
        else if (state == CentralCircuitState.HalfOpen)
        {
            LoggingService.LogWarning($"CentralUnit : circuit breaker '{subsystem}' en sondage après cooldown.");
        }
        else
        {
            LoggingService.LogInfo($"CentralUnit : circuit breaker '{subsystem}' refermé, service rétabli.");
        }
    }

    private sealed class BreakerEntry
    {
        public CentralCircuitState State { get; set; }

        public int FailureStreak { get; set; }

        public DateTime OpenedUtc { get; set; }
    }
}
