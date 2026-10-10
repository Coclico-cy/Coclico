using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace Coclico.Services;

public sealed class ResourceGuardService : IDisposable
{
    public enum PressureLevel { Normal, Elevated, High, Critical }

    public event Action<PressureLevel>? PressureChanged;
    public event Action<string>? GuardMessage;

    public PressureLevel CurrentPressure { get; private set; } = PressureLevel.Normal;
    public double AppCpuPercent { get; private set; }
    public long AppWorkingSetMb { get; private set; }
    public long AppPrivateMb { get; private set; }

    private const int TrimCooldownSec = 30;
    private const int HighStreakForTrim = 4;

    private const double CriticalRamPercent = 93;
    private const double HighRamPercent = 83;
    private const double ElevatedRamPercent = 70;
    private const long CriticalAppWorkingSetMb = 900;
    private const long HighAppWorkingSetMb = 550;
    private const long ElevatedAppWorkingSetMb = 300;

    private readonly record struct Snapshot(
        double CpuPercent,
        long WorkingSetMb,
        long PrivateMb,
        MemoryCleanerService.RamInfo Ram);

    private readonly Process _self;
    private IDisposable? _subscription;

    private int _consecutiveHigh;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private DateTime _lastTrim = DateTime.MinValue;

    public ResourceGuardService()
    {
        _self = Process.GetCurrentProcess();
        _lastCpuTime = _self.TotalProcessorTime;
    }

    public void Start()
    {
        if (_subscription != null)
        {
            return;
        }

        _subscription = Observable
            .Interval(TimeSpan.FromSeconds(6), Scheduler.Default)
            .Select(_ => CollectOnBackground())
            .Catch<Snapshot, Exception>(ex =>
            {
                LoggingService.LogException(ex, "ResourceGuardService.Tick");
                return Observable.Empty<Snapshot>();
            })
            .Repeat()
            .Subscribe(Apply);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public void Dispose()
    {
        Stop();
        try { _self.Dispose(); } catch (Exception exSelf) { LoggingService.LogException(exSelf, "ResourceGuardService.Dispose"); }
    }

    private Snapshot CollectOnBackground()
    {
        _self.Refresh();

        double cpu = 0;
        DateTime now = DateTime.UtcNow;
        TimeSpan elapsed = now - _lastCpuCheck;
        if (elapsed.TotalMilliseconds >= 400)
        {
            TimeSpan cpuDelta = _self.TotalProcessorTime - _lastCpuTime;
            int cores = Math.Max(1, Environment.ProcessorCount);
            cpu = Math.Min(100.0,
                cpuDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * cores) * 100.0);
            _lastCpuTime = _self.TotalProcessorTime;
            _lastCpuCheck = now;
        }

        return new Snapshot(
            CpuPercent: cpu,
            WorkingSetMb: _self.WorkingSet64 / (1024 * 1024),
            PrivateMb: _self.PrivateMemorySize64 / (1024 * 1024),
            Ram: MemoryCleanerService.GetRamInfo());
    }

    private void Apply(Snapshot snap)
    {
        AppCpuPercent = snap.CpuPercent;
        AppWorkingSetMb = snap.WorkingSetMb;
        AppPrivateMb = snap.PrivateMb;

        PressureLevel previous = CurrentPressure;
        double ramPct = snap.Ram.PhysUsedPercent;

        CurrentPressure = (ramPct, AppWorkingSetMb) switch
        {
            _ when ramPct >= CriticalRamPercent || AppWorkingSetMb >= CriticalAppWorkingSetMb => PressureLevel.Critical,
            _ when ramPct >= HighRamPercent || AppWorkingSetMb >= HighAppWorkingSetMb => PressureLevel.High,
            _ when ramPct >= ElevatedRamPercent || AppWorkingSetMb >= ElevatedAppWorkingSetMb => PressureLevel.Elevated,
            _ => PressureLevel.Normal,
        };

        if (CurrentPressure != previous)
        {
            PressureChanged?.Invoke(CurrentPressure);
        }

        switch (CurrentPressure)
        {
            case PressureLevel.Critical:
                if (Interlocked.Increment(ref _consecutiveHigh) >= HighStreakForTrim)
                {
                    TrimSelfIfReady("Pression CRITIQUE — Working Set réduit");
                    _ = Interlocked.Exchange(ref _consecutiveHigh, 0);
                }
                break;

            case PressureLevel.High:
                if (Interlocked.Increment(ref _consecutiveHigh) >= HighStreakForTrim * 2)
                {
                    TrimSelfIfReady("Pression HAUTE — optimisation mémoire");
                    _ = Interlocked.Exchange(ref _consecutiveHigh, 0);
                }
                break;

            case PressureLevel.Elevated:
                _ = DecrementFloorZero(ref _consecutiveHigh);
                break;

            default:
                _ = Interlocked.Exchange(ref _consecutiveHigh, 0);
                break;
        }
    }

    private void TrimSelfIfReady(string reason)
    {
        if ((DateTime.UtcNow - _lastTrim).TotalSeconds < TrimCooldownSec)
        {
            return;
        }

        _lastTrim = DateTime.UtcNow;
        MemoryCleanerService.TrimSelfWorkingSet();
        GuardMessage?.Invoke(reason);
    }

    private static int DecrementFloorZero(ref int location)
    {
        int current, updated;
        do
        {
            current = location;
            updated = Math.Max(0, current - 1);
        }
        while (Interlocked.CompareExchange(ref location, updated, current) != current);
        return updated;
    }
}
