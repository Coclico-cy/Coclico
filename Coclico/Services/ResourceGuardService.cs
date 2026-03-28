#nullable enable
using System;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime;
using System.Threading;
using System.Windows;

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

    private IDisposable? _subscription;
    private readonly Process _self;

    private volatile int _consecutiveHigh;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private DateTime _lastTrim = DateTime.MinValue;

    private const int TrimCooldownSec = 30;
    private const int HighStreakForGc = 4;

    private readonly record struct Snapshot(
        double CpuPercent,
        long WorkingSetMb,
        long PrivateMb,
        MemoryCleanerService.RamInfo Ram);

    public ResourceGuardService()
    {
        _self = Process.GetCurrentProcess();
        _lastCpuTime = _self.TotalProcessorTime;
    }

    public void Start()
    {
        if (_subscription != null) return;

        _subscription = Observable
            .Interval(TimeSpan.FromSeconds(2), Scheduler.Default)
            .Select(_ => CollectOnBackground())
            .Catch<Snapshot, Exception>(ex =>
            {
                LoggingService.LogException(ex, "ResourceGuardService.Tick");
                return Observable.Empty<Snapshot>();
            })
            .Repeat()
            .ObserveOn(new DispatcherScheduler(Application.Current.Dispatcher))
            .Subscribe(snap => ApplyOnUiThread(snap));
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private Snapshot CollectOnBackground()
    {
        _self.Refresh();

        double cpu = 0;
        var now = DateTime.UtcNow;
        var elapsed = now - _lastCpuCheck;
        if (elapsed.TotalMilliseconds >= 400)
        {
            var cpuDelta = _self.TotalProcessorTime - _lastCpuTime;
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

    private void ApplyOnUiThread(Snapshot snap)
    {
        AppCpuPercent = snap.CpuPercent;
        AppWorkingSetMb = snap.WorkingSetMb;
        AppPrivateMb = snap.PrivateMb;
        ApplyPressurePolicy(snap.Ram);
    }

    private void ApplyPressurePolicy(MemoryCleanerService.RamInfo sys)
    {
        var previous = CurrentPressure;
        double ramPct = sys.PhysUsedPercent;

        CurrentPressure = (ramPct, AppWorkingSetMb) switch
        {
            _ when ramPct >= 93 || AppWorkingSetMb >= 900 => PressureLevel.Critical,
            _ when ramPct >= 83 || AppWorkingSetMb >= 550 => PressureLevel.High,
            _ when ramPct >= 70 || AppWorkingSetMb >= 300 => PressureLevel.Elevated,
            _ => PressureLevel.Normal,
        };

        if (CurrentPressure != previous)
            PressureChanged?.Invoke(CurrentPressure);

        switch (CurrentPressure)
        {
            case PressureLevel.Critical:
                if (Interlocked.Increment(ref _consecutiveHigh) >= HighStreakForGc)
                {
                    AggressiveGc();
                    Interlocked.Exchange(ref _consecutiveHigh, 0);
                }
                TrimSelfIfReady("Pression CRITIQUE — Working Set réduit");
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                break;

            case PressureLevel.High:
                if (Interlocked.Increment(ref _consecutiveHigh) >= HighStreakForGc * 2)
                {
                    TrimSelfIfReady("Pression HAUTE — optimisation mémoire");
                    Interlocked.Exchange(ref _consecutiveHigh, 0);
                }
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                break;

            case PressureLevel.Elevated:
                int cur, updated;
                do
                {
                    cur = _consecutiveHigh;
                    updated = Math.Max(0, cur - 1);
                }
                while (Interlocked.CompareExchange(ref _consecutiveHigh, updated, cur) != cur);
                GCSettings.LatencyMode = GCLatencyMode.Interactive;
                break;

            default:
                Interlocked.Exchange(ref _consecutiveHigh, 0);
                GCSettings.LatencyMode = GCLatencyMode.Interactive;
                break;
        }
    }

    private void TrimSelfIfReady(string reason)
    {
        if ((DateTime.UtcNow - _lastTrim).TotalSeconds < TrimCooldownSec) return;
        _lastTrim = DateTime.UtcNow;
        MemoryCleanerService.TrimSelfWorkingSet();
        GuardMessage?.Invoke(reason);
    }

    private static void AggressiveGc()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    }

    public void Dispose()
    {
        Stop();
        try { _self.Dispose(); } catch { }
    }
}
