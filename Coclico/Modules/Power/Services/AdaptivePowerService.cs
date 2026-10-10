using System.Runtime.InteropServices;
using Coclico.Services;

namespace Coclico.Modules.Power.Services;

public sealed record AdaptivePowerConfig
{
    public int HighThresholdPercent { get; init; } = 60;
    public int LowThresholdPercent { get; init; } = 25;
    public TimeSpan HighSustain { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan LowSustain { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(3);
}

public enum AdaptivePowerAction
{
    None,
    SwitchToPerformance,
    SwitchToEconomy
}

public enum AdaptivePowerReason
{
    Cooldown,
    HighLoad,
    LowLoad,
    NormalLoad
}

public sealed class CpuLoadWindow
{
    private readonly Queue<(DateTime Utc, double Usage)> _samples = new();
    private readonly TimeSpan _retention;

    public CpuLoadWindow(TimeSpan retention)
    {
        _retention = retention;
    }

    public int Count { get { lock (_samples) { return _samples.Count; } } }

    public void Add(double usagePercent, DateTime utcNow)
    {
        lock (_samples)
        {
            _samples.Enqueue((utcNow, usagePercent));
            while (_samples.Count > 0 && utcNow - _samples.Peek().Utc > _retention)
            {
                _ = _samples.Dequeue();
            }
        }
    }

    public double AverageLast(TimeSpan window, DateTime utcNow)
    {
        lock (_samples)
        {
            double total = 0;
            int count = 0;
            foreach ((DateTime utc, double usage) in _samples)
            {
                if (utcNow - utc <= window)
                {
                    total += usage;
                    count++;
                }
            }

            return count == 0 ? 0 : total / count;
        }
    }
}

public static class AdaptivePowerRules
{
    public static (AdaptivePowerAction Action, AdaptivePowerReason Reason, double LoadPercent) Evaluate(
        CpuLoadWindow window,
        AdaptivePowerConfig config,
        CoclicoPowerMode currentMode,
        DateTime utcNow,
        DateTime lastSwitchUtc)
    {
        if (utcNow - lastSwitchUtc < config.Cooldown)
        {
            return (AdaptivePowerAction.None, AdaptivePowerReason.Cooldown, 0);
        }

        double high = window.AverageLast(config.HighSustain, utcNow);
        if (currentMode is CoclicoPowerMode.Optimal or CoclicoPowerMode.Economy && high >= config.HighThresholdPercent)
        {
            return (AdaptivePowerAction.SwitchToPerformance, AdaptivePowerReason.HighLoad, high);
        }

        double low = window.AverageLast(config.LowSustain, utcNow);
        if (currentMode is CoclicoPowerMode.Optimal or CoclicoPowerMode.Performance && low <= config.LowThresholdPercent)
        {
            return (AdaptivePowerAction.SwitchToEconomy, AdaptivePowerReason.LowLoad, low);
        }

        return (AdaptivePowerAction.None, AdaptivePowerReason.NormalLoad, 0);
    }
}

public sealed class AdaptivePowerService
{
    private readonly CpuPowerService _cpuPower;
    private readonly SettingsService _settings;
    private readonly object _stateLock = new();
    private readonly AdaptivePowerConfig _config = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private System.Threading.Timer? _saveTimer;
    private CpuLoadWindow _window = new(TimeSpan.FromMinutes(2));
    private CoclicoPowerMode _currentMode = CoclicoPowerMode.Optimal;
    private DateTime _lastSwitchUtc = DateTime.MinValue;
    private (long Idle, long Total)? _lastTimes;
    private double _currentCpuUsage;
    private string _status = "Inactif.";

    public event Action<string>? StatusChanged;
    public event Action<CoclicoPowerMode>? PlanChanged;

    public AdaptivePowerService(CpuPowerService cpuPower, SettingsService settings)
    {
        _cpuPower = cpuPower;
        _settings = settings;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _cts != null;
            }
        }
    }

    public double CurrentCpuUsage
    {
        get
        {
            lock (_stateLock)
            {
                return _currentCpuUsage;
            }
        }
    }

    public string Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    public CoclicoPowerMode CurrentMode
    {
        get
        {
            lock (_stateLock)
            {
                return _currentMode;
            }
        }
    }

    public async Task<(bool Started, string Message)> StartAsync(CancellationToken ct = default)
    {
        if (IsEnabled)
        {
            return (true, "Mode adaptatif deja actif.");
        }

        if (!_cpuPower.IsElevated)
        {
            return (false, "Les droits administrateur sont requis pour le mode adaptatif.");
        }

        PowerPlanSetupReport report = await _cpuPower.EnsureCoclicoPlansAsync(null, ct).ConfigureAwait(false);
        if (!report.Success)
        {
            return (false, "Impossible de creer ou verifier les plans Coclico.");
        }

        PowerStateSummary state = await _cpuPower.GetStateSummaryAsync(ct).ConfigureAwait(false);
        if (state.ActiveCoclicoMode is null)
        {
            (bool ok, string message) = await _cpuPower.ActivatePlanAsync(CoclicoPowerMode.Optimal, ct).ConfigureAwait(false);
            if (!ok)
            {
                return (false, message);
            }

            lock (_stateLock)
            {
                _currentMode = CoclicoPowerMode.Optimal;
                _lastTimes = null;
            }
        }
        else
        {
            lock (_stateLock)
            {
                _currentMode = state.ActiveCoclicoMode.Value;
            }
        }

        lock (_stateLock)
        {
            _window = new CpuLoadWindow(TimeSpan.FromMinutes(2));
            _lastSwitchUtc = DateTime.UtcNow;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _loopTask = Task.Run(() => LoopAsync(token), CancellationToken.None);
            _status = "Surveillance de la charge CPU en cours.";
        }

        _settings.Settings.AdaptivePowerEnabled = true;
        await _settings.SaveAsync().ConfigureAwait(false);
        StatusChanged?.Invoke(Status);
        return (true, "Mode optimal adaptatif actif.");
    }

    public async Task StopAsync()
    {
        Task? loop;
        lock (_stateLock)
        {
            if (_cts == null)
            {
                return;
            }

            _cts.Cancel();
            loop = _loopTask;
            _loopTask = null;
            _cts = null;
            _status = "Mode adaptatif inactif.";
        }

        if (loop != null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        lock (_stateLock)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }

        _settings.Settings.AdaptivePowerEnabled = false;
        await _settings.SaveAsync().ConfigureAwait(false);
        StatusChanged?.Invoke(Status);
    }

    public double SampleCpuUsage()
    {
        lock (_stateLock)
        {
            (long idle, long total) = NativeCpuTiming.GetTimes();
            (long Idle, long Total)? previous = _lastTimes;
            _lastTimes = (idle, total);

            if (previous is not { } prev || total <= prev.Total)
            {
                return 0;
            }

            long deltaTotal = total - prev.Total;
            long deltaIdle = idle - prev.Idle;
            if (deltaTotal <= 0)
            {
                return 0;
            }

            double usage = 100.0 * (deltaTotal - deltaIdle) / deltaTotal;
            return Math.Clamp(usage, 0.0, 100.0);
        }
    }

    private void ScheduleSettingsSave()
    {
        lock (_stateLock)
        {
            if (_saveTimer == null)
            {
                _saveTimer = new System.Threading.Timer(
                    _ => _ = SaveSettingsNowAsync(),
                    null,
                    dueTime: TimeSpan.FromMilliseconds(500),
                    period: Timeout.InfiniteTimeSpan);
            }
            else
            {
                _saveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task SaveSettingsNowAsync()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AdaptivePowerService.SaveSettingsNow");
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.SampleInterval, ct).ConfigureAwait(false);

                double usage = SampleCpuUsage();
                DateTime utcNow = DateTime.UtcNow;
                _window.Add(usage, utcNow);

                lock (_stateLock)
                {
                    _currentCpuUsage = usage;
                }

                (AdaptivePowerAction action, AdaptivePowerReason reason, double load) = AdaptivePowerRules.Evaluate(_window, _config, CurrentMode, utcNow, _lastSwitchUtc);
                if (action == AdaptivePowerAction.None)
                {
                    UpdateStatus($"Charge {usage:0}% - plan « {PowerPlanCatalog.GetPlanName(CurrentMode)} ».");
                    continue;
                }

                CoclicoPowerMode target = action == AdaptivePowerAction.SwitchToPerformance
                    ? CoclicoPowerMode.Performance
                    : CoclicoPowerMode.Economy;
                (bool ok, string message) = await _cpuPower.ActivatePlanAsync(target, ct).ConfigureAwait(false);
                if (!ok)
                {
                    UpdateStatus(message);
                    continue;
                }

                lock (_stateLock)
                {
                    _currentMode = target;
                    _lastSwitchUtc = DateTime.UtcNow;
                }

                _settings.Settings.PowerPreferredMode = target.ToString();
                ScheduleSettingsSave();
                UpdateStatus($"{message} ({ReasonText(reason, load)})");
                PlanChanged?.Invoke(target);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "AdaptivePowerService.Loop");
            }
        }
    }

    private void UpdateStatus(string status)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        StatusChanged?.Invoke(status);
    }

    private static string ReasonText(AdaptivePowerReason reason, double loadPercent)
    {
        LocalizationService? loc = ServiceContainer.GetOptional<LocalizationService>();
        return reason switch
        {
            AdaptivePowerReason.HighLoad =>
                string.Format(loc?.Get("Power_Reason_HighLoad") ?? "Charge {0:0}% maintenue, passage en performance.", loadPercent),
            AdaptivePowerReason.LowLoad =>
                string.Format(loc?.Get("Power_Reason_LowLoad") ?? "Charge {0:0}% maintenue, passage en economie.", loadPercent),
            AdaptivePowerReason.Cooldown =>
                loc?.Get("Power_Reason_Cooldown") ?? "Repos entre deux changements de plan.",
            _ =>
                loc?.Get("Power_Reason_Normal") ?? "Charge normale, aucun changement."
        };
    }

    internal static class NativeCpuTiming
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTimeStruct
        {
            public uint Low;
            public uint High;

            public readonly long Value => ((long)High << 32) | Low;
        }

        [DllImport("kernel32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FileTimeStruct idle, out FileTimeStruct kernel, out FileTimeStruct user);

        public static (long Idle, long Total) GetTimes()
        {
            if (!GetSystemTimes(out FileTimeStruct idle, out FileTimeStruct kernel, out FileTimeStruct user))
            {
                return (0, 0);
            }

            long idleValue = idle.Value;
            long total = kernel.Value + user.Value;
            return (idleValue, total);
        }
    }
}