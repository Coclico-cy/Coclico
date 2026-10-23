using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Coclico.Services;

public sealed class CentralUnitStats
{
    public CentralActivityLevel ActivityLevel { get; init; }

    public double CpuPercent { get; init; }

    public bool ExternalPressureActive { get; init; }

    public bool IsRunning { get; init; }

    public int BoostDepth { get; init; }

    public long FlagWakeups { get; init; }

    public int ScheduledFlags { get; init; }

    public long PublishedValues { get; init; }

    public long DroppedRequests { get; init; }

    public long RoutedRequests { get; init; }
}

public sealed class CentralUnitService : IDisposable
{
    public static readonly TimeSpan DefaultAiUnloadDelay = TimeSpan.FromSeconds(30);

    private const int ProcessInformationMemoryPriority = 0;
    private const int ProcessInformationPowerThrottling = 4;
    private const int ThreadInformationMemoryPriority = 0;
    private const int ThreadInformationPowerThrottling = 3;
    private const uint PowerThrottlingStateVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;
    private const uint PowerThrottlingIgnoreTimerResolution = 0x4;
    private const uint MemoryPriorityVeryLow = 1;
    private const uint MemoryPriorityNormal = 5;
    private const int ExternalPressureCpuEnterPercent = 85;
    private const int ExternalPressureCpuExitPercent = 60;
    private const int ExternalPressureRamPercent = 92;
    private const int ExternalPressureEnterSamples = 3;
    private const int ExternalPressureExitSamples = 2;

    private static readonly TimeSpan PressureSampleInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PressureSampleIntervalUnderPressure = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SuspensionPostponeDelay = TimeSpan.FromSeconds(30);
    private static readonly ulong TicksPerSecond = 10_000_000UL;

    public static CentralUnitService Current { get; } = new();

    public CentralUnitFlagStore Flags { get; } = new();
    public CentralUnitActivity Activity { get; } = new();
    public CentralUnitVariables Variables { get; } = new();
    public CentralUnitBus Bus { get; } = new();

    private readonly object _stateLock = new();
    private CancellationTokenSource? _pressureCts;
    private Task? _pressureTask;
    private bool _initialized;
    private bool _shutdown;
    private int _boostDepth;
    private bool _externalPressureActive;
    private int _externalCpuHighStreak;
    private int _externalCpuLowStreak;
    private double _lastCpuPercent;
    private bool _lastSampleValid;
    private ulong _lastIdleTicks;
    private ulong _lastTotalTicks;
    private TimeSpan _lastOwnCpuTime;

    private CentralUnitService()
    {
    }

    public void Initialize()
    {
        lock (_stateLock)
        {
            if (_initialized || _shutdown)
            {
                return;
            }

            _initialized = true;
        }

        Flags.SetCriticalFlags(CentralFlags.StartupCompleted, CentralFlags.PressureSample, CentralFlags.AiModelUnload);
        Activity.LevelChanged += OnActivityLevelChanged;
        Variables.Publish(CentralVariables.ActivityLevel, Activity.Level);
        Flags.StartScheduler();
        Flags.Schedule(CentralFlags.PressureSample, DateTime.UtcNow);

        CancellationTokenSource pressureCts = new();
        _pressureCts = pressureCts;
        _pressureTask = Task.Run(() => RunBackgroundLoopAsync(pressureCts.Token));

        ApplyPowerPolicy();
        LoggingService.LogInfo("CentralUnit : unité centrale initialisée.");
    }

    public void MarkStartupCompleted()
    {
        Activity.MarkStarted();
        Flags.Set(CentralFlags.StartupCompleted);
    }

    public void SetWindowVisible(bool visible)
    {
        Activity.SetWindowVisible(visible);
    }

    public void SetTrayMinimized(bool minimized)
    {
        Activity.SetTrayMinimized(minimized);
    }

    public void SetAiInference(bool active)
    {
        Activity.SetAiInference(active);
        if (active)
        {
            BeginBoost();
            Flags.CancelSchedule(CentralFlags.AiModelUnload);
        }
        else
        {
            EndBoost();
            ScheduleAiUnload();
        }
    }

    public void BeginBoost()
    {
        lock (_stateLock)
        {
            _boostDepth++;
            if (_boostDepth == 1)
            {
                ApplyPowerPolicyLocked();
            }
        }
    }

    public void EndBoost()
    {
        lock (_stateLock)
        {
            if (_boostDepth > 0)
            {
                _boostDepth--;
                if (_boostDepth == 0)
                {
                    ApplyPowerPolicyLocked();
                }
            }
        }
    }

    public void NotifyAiActivity()
    {
        if (Activity.IsAiInferenceActive)
        {
            return;
        }

        ScheduleAiUnload();
    }

    public void CancelAiUnload()
    {
        Flags.CancelSchedule(CentralFlags.AiModelUnload);
    }

    public CentralUnitStats GetStats()
    {
        lock (_stateLock)
        {
            return new CentralUnitStats
            {
                ActivityLevel = Activity.Level,
                CpuPercent = Math.Round(_lastCpuPercent, 1),
                ExternalPressureActive = _externalPressureActive,
                IsRunning = _initialized && !_shutdown,
                BoostDepth = _boostDepth,
                FlagWakeups = Flags.Wakeups,
                ScheduledFlags = Flags.ScheduledCount,
                PublishedValues = Variables.PublishedCount,
                DroppedRequests = Bus.DroppedRequests,
                RoutedRequests = Bus.RoutedRequests
            };
        }
    }

    public void ApplyPowerPolicy()
    {
        lock (_stateLock)
        {
            ApplyPowerPolicyLocked();
        }
    }

    public void Shutdown()
    {
        lock (_stateLock)
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
        }

        try
        {
            _pressureCts?.Cancel();
            Flags.StopScheduler();
            Activity.LevelChanged -= OnActivityLevelChanged;
            ApplyProcessPowerPolicy(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CentralUnitService.Shutdown");
        }

        LoggingService.LogInfo("CentralUnit : unité centrale arrêtée.");
    }

    public void Dispose()
    {
        Shutdown();
        CancellationTokenSource? pressureCts;
        lock (_stateLock)
        {
            pressureCts = _pressureCts;
            _pressureCts = null;
            _pressureTask = null;
        }

        pressureCts?.Dispose();
        Flags.Dispose();
    }

    public static void ApplyCurrentThreadBackgroundQos()
    {
        try
        {
            PowerThrottlingState state = new PowerThrottlingState
            {
                Version = PowerThrottlingStateVersion,
                ControlMask = PowerThrottlingExecutionSpeed | PowerThrottlingIgnoreTimerResolution,
                StateMask = PowerThrottlingExecutionSpeed | PowerThrottlingIgnoreTimerResolution
            };
            _ = SetThreadInformation(GetCurrentThread(), ThreadInformationPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());

            MemoryPriorityInformation priority = new MemoryPriorityInformation
            {
                MemoryPriority = MemoryPriorityVeryLow
            };
            _ = SetThreadInformation(GetCurrentThread(), ThreadInformationMemoryPriority, ref priority, Marshal.SizeOf<MemoryPriorityInformation>());
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CentralUnitService.ApplyCurrentThreadBackgroundQos");
        }
    }

    private void ScheduleAiUnload()
    {
        Flags.Schedule(CentralFlags.AiModelUnload, DateTime.UtcNow + DefaultAiUnloadDelay);
    }

    private void OnActivityLevelChanged(CentralActivityLevel level)
    {
        Variables.Publish(CentralVariables.ActivityLevel, level);
        if (level == CentralActivityLevel.ExternalPressure)
        {
            Flags.PostponeNonCritical(SuspensionPostponeDelay);
        }

        ApplyPowerPolicy();
    }

    private void ApplyPowerPolicyLocked()
    {
        CentralActivityLevel level = Activity.Level;
        bool eco = _boostDepth == 0
            && level is CentralActivityLevel.Background or CentralActivityLevel.Idle or CentralActivityLevel.ExternalPressure;
        ApplyProcessPowerPolicy(eco);
    }

    private async Task RunBackgroundLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ApplyCurrentThreadBackgroundQos();
            try
            {
                await Flags.WaitAsync(CentralFlags.PressureSample, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            ApplyCurrentThreadBackgroundQos();
            try
            {
                SamplePressure();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "CentralUnitService.SamplePressure");
            }

            bool underPressure;
            lock (_stateLock)
            {
                underPressure = _externalPressureActive;
            }

            Flags.Schedule(CentralFlags.PressureSample, DateTime.UtcNow + (underPressure ? PressureSampleIntervalUnderPressure : PressureSampleInterval));
        }
    }

    private void SamplePressure()
    {
        double? externalCpuPercent = MeasureExternalCpuPercent();
        if (externalCpuPercent is double cpuPercent)
        {
            _lastCpuPercent = cpuPercent;
            Variables.Publish(CentralVariables.CpuPercent, Math.Round(cpuPercent, 1));
        }

        bool ramPressure = IsSystemMemoryUnderPressure();
        bool enter = false;
        bool exit = false;

        lock (_stateLock)
        {
            if (externalCpuPercent is double current)
            {
                if (current >= ExternalPressureCpuEnterPercent)
                {
                    _externalCpuHighStreak++;
                    _externalCpuLowStreak = 0;
                }
                else if (current < ExternalPressureCpuExitPercent)
                {
                    _externalCpuLowStreak++;
                    _externalCpuHighStreak = 0;
                }
                else
                {
                    _externalCpuHighStreak = 0;
                    _externalCpuLowStreak = 0;
                }
            }

            bool shouldEnter = ramPressure || _externalCpuHighStreak >= ExternalPressureEnterSamples;
            bool shouldExit = !ramPressure && _externalCpuLowStreak >= ExternalPressureExitSamples;

            if (!_externalPressureActive && shouldEnter)
            {
                _externalPressureActive = true;
                enter = true;
            }
            else if (_externalPressureActive && shouldExit)
            {
                _externalPressureActive = false;
                exit = true;
            }

            if (enter || exit)
            {
                _externalCpuHighStreak = 0;
                _externalCpuLowStreak = 0;
            }
        }

        if (!enter && !exit)
        {
            return;
        }

        Activity.SetExternalPressure(_externalPressureActive);
        LoggingService.LogInfo(enter
            ? $"CentralUnit : pression externe détectée (CPU externe {_lastCpuPercent:0.0} %)."
            : $"CentralUnit : pression externe levée (CPU externe {_lastCpuPercent:0.0} %).");
    }

    private double? MeasureExternalCpuPercent()
    {
        if (!GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime))
        {
            return null;
        }

        ulong idleTicks = ToTicks(idleTime);
        ulong totalTicks = ToTicks(kernelTime) + ToTicks(userTime);
        TimeSpan ownCpuTime = Process.GetCurrentProcess().TotalProcessorTime;

        if (!_lastSampleValid || totalTicks <= _lastTotalTicks)
        {
            _lastIdleTicks = idleTicks;
            _lastTotalTicks = totalTicks;
            _lastOwnCpuTime = ownCpuTime;
            _lastSampleValid = true;
            return null;
        }

        ulong deltaIdle = idleTicks - _lastIdleTicks;
        ulong deltaTotal = totalTicks - _lastTotalTicks;
        if (deltaTotal == 0)
        {
            return null;
        }

        double deltaSeconds = deltaTotal / (double)TicksPerSecond;
        double systemPercent = (1.0 - deltaIdle / (double)deltaTotal) * 100.0;
        double ownPercent = (ownCpuTime - _lastOwnCpuTime).TotalSeconds / deltaSeconds * 100.0;
        double externalPercent = Math.Clamp(systemPercent - ownPercent, 0.0, 100.0);

        _lastIdleTicks = idleTicks;
        _lastTotalTicks = totalTicks;
        _lastOwnCpuTime = ownCpuTime;

        return externalPercent;
    }

    private static bool IsSystemMemoryUnderPressure()
    {
        try
        {
            MemoryStatus status = new MemoryStatus
            {
                Length = (uint)Marshal.SizeOf<MemoryStatus>()
            };
            return GlobalMemoryStatusEx(ref status) && status.MemoryLoad >= ExternalPressureRamPercent;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyProcessPowerPolicy(bool eco)
    {
        try
        {
            using Process process = Process.GetCurrentProcess();

            PowerThrottlingState state = new PowerThrottlingState
            {
                Version = PowerThrottlingStateVersion,
                ControlMask = PowerThrottlingExecutionSpeed | PowerThrottlingIgnoreTimerResolution,
                StateMask = eco ? PowerThrottlingExecutionSpeed | PowerThrottlingIgnoreTimerResolution : 0
            };
            _ = SetProcessInformation(process.Handle, ProcessInformationPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());

            MemoryPriorityInformation priority = new MemoryPriorityInformation
            {
                MemoryPriority = eco ? MemoryPriorityVeryLow : MemoryPriorityNormal
            };
            _ = SetProcessInformation(process.Handle, ProcessInformationMemoryPriority, ref priority, Marshal.SizeOf<MemoryPriorityInformation>());
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "CentralUnitService.ApplyProcessPowerPolicy");
        }
    }

    private static ulong ToTicks(NativeFileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryPriorityInformation
    {
        public uint MemoryPriority;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int processInformationClass, ref PowerThrottlingState processInformation, int processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int processInformationClass, ref MemoryPriorityInformation processInformation, int processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(IntPtr thread, int threadInformationClass, ref PowerThrottlingState threadInformation, int threadInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(IntPtr thread, int threadInformationClass, ref MemoryPriorityInformation threadInformation, int threadInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();
}
