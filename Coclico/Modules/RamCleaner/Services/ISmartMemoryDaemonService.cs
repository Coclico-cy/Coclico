namespace Coclico.Services;

public enum AutoCleanMode
{
    Interval,

    ThresholdPercent,

    Hybrid
}

public sealed record AutoCleanResultEventArgs(
    DateTime Timestamp,
    AutoCleanMode TriggerMode,
    long FreedBytes,
    string Summary
);

public interface ISmartMemoryDaemonService : IDisposable
{
    bool IsEnabled { get; }

    AutoCleanMode Mode { get; }

    int Value { get; }

    bool ProtectForeground { get; }

    DateTime? LastCleanUtc { get; }

    long LastFreedBytes { get; }

    long TotalSessionFreedBytes { get; }

    string StatusMessage { get; }

    TimeSpan? TimeUntilNextClean { get; }

    MemoryCleanerService.CleanProfile Profile { get; }

    void Configure(bool enabled, AutoCleanMode mode, int value, bool protectForeground = true, MemoryCleanerService.CleanProfile profile = MemoryCleanerService.CleanProfile.Smart);

    void Start();

    void Stop();

    Task<MemoryCleanerService.CleanResult> ForceCleanNowAsync(CancellationToken ct = default);

    event Action<AutoCleanResultEventArgs>? CleanExecuted;

    event Action<string>? StatusChanged;
}
