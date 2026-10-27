using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public sealed class ProcessItemVm
{
    public string Name { get; init; } = string.Empty;
    public string RamText { get; init; } = string.Empty;
    public double WorkingSetPercent { get; init; }
}

public partial class RamCleanerViewModel : INotifyPropertyChanged, IDisposable
{
    private const int HistorySize = 90;
    private const string Subsystem = "RamCleaner";

    private readonly ISmartMemoryDaemonService? _daemon;
    private CancellationTokenSource? _cleanCts;
    private bool _disposed;

    private readonly bool _suppressConfigPush;
    private long _cumulativeFreedBytes;
    private bool _notifEnabled = true;
    private int _autoProfileIndex;
    private int _autoModeIndex;
    private int _autoIntervalIndex = 2;
    private int _autoThresholdIndex = 2;
    private int _refreshRateIndex = 1;

    private readonly double[] _ramHistory = new double[HistorySize];
    private int _historyHead;
    private int _historyCount;

    private static readonly Brush _brushGreen = FreezeBrush(new SolidColorBrush(Color.FromRgb(34, 197, 94)));
    private static readonly Brush _brushYellow = FreezeBrush(new SolidColorBrush(Color.FromRgb(234, 179, 8)));
    private static readonly Brush _brushOrange = FreezeBrush(new SolidColorBrush(Color.FromRgb(249, 115, 22)));
    private static readonly Brush _brushRed = FreezeBrush(new SolidColorBrush(Color.FromRgb(239, 68, 68)));
    private static readonly Brush _brushBorderNormal = FreezeBrush(new SolidColorBrush(Color.FromRgb(26, 16, 48)));
    private static readonly Brush _brushBorderElevated = FreezeBrush(new SolidColorBrush(Color.FromRgb(113, 63, 18)));
    private static readonly Brush _brushBorderHigh = FreezeBrush(new SolidColorBrush(Color.FromRgb(127, 29, 29)));
    private static readonly Brush _brushBorderCritical = FreezeBrush(new SolidColorBrush(Color.FromRgb(185, 28, 28)));

    private static Brush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

    public Func<bool>? RestartGpuPipelineConfirm { get; set; }

    public ObservableCollection<ProcessItemVm> TopProcesses { get; } = [];

    public bool IsCleaning { get; private set { if (field == value) return; field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotCleaning)); } }
    public bool IsNotCleaning => !IsCleaning;
    public bool HasResult { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public string CleaningStatus { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public string LastCleanedText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public bool NotifEnabled
    {
        get => _notifEnabled;
        private set { if (_notifEnabled == value) return; _notifEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotifButtonLabel)); }
    }
    public string NotifButtonLabel => NotifEnabled ? L("RamCleaner_NotifOn") : L("RamCleaner_NotifOff");
    public string ResultWorkingSets { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultStandby { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultLowPriorityStandby { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultModifiedPages { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultCombinedPages { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultModifiedFileCache { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultSystemFileCache { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultRegistryCache { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultDnsCache { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string ResultTotal { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PhysUsedText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PhysAvailText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PhysTotalText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PhysPercent { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public double PhysUsedPercent { get; private set { if (field.Equals(value)) return; field = value; OnPropertyChanged(); } }
    public string VirtUsedText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string VirtAvailText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string VirtTotalText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string VirtPercent { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public double VirtUsedPercent { get; private set { if (field.Equals(value)) return; field = value; OnPropertyChanged(); } }
    public string PageUsedText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PageAvailText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string PageTotalText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public double SysCpuPercent { get; private set { if (field.Equals(value)) return; field = value; OnPropertyChanged(); } }
    public string SysCpuText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public Brush SysCpuColor { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = _brushGreen;
    public string GpuNameShort { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public string GpuVramText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string GpuTypeText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public string AppRamText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public double AppRamPercent { get; private set { if (field.Equals(value)) return; field = value; OnPropertyChanged(); } }
    public string AppCpuText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "-";
    public double AppCpuPercent { get; private set { if (field.Equals(value)) return; field = value; OnPropertyChanged(); } }
    public string PressureText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public Brush PressureColor { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = _brushGreen;
    public Brush PressureBorderColor { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = _brushBorderNormal;
    public string CumulativeFreedText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "0 B";
    public string GuardStatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public bool AutoCleanEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoCleanValueEnabled));
            PushDaemonConfig();
        }
    }

    public bool ProtectForeground
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            PushDaemonConfig();
        }
    }

    public int AutoProfileIndex
    {
        get => _autoProfileIndex;
        set { if (_autoProfileIndex == value) return; _autoProfileIndex = value; OnPropertyChanged(); PushDaemonConfig(); }
    }

    public int AutoModeIndex
    {
        get => _autoModeIndex;
        set
        {
            if (_autoModeIndex == value) return;
            _autoModeIndex = value;
            OnPropertyChanged();
            UpdateAutoModeVisibility();
            OnPropertyChanged(nameof(AutoCleanValueEnabled));
            PushDaemonConfig();
        }
    }

    public int AutoIntervalIndex
    {
        get => _autoIntervalIndex;
        set { if (_autoIntervalIndex == value) return; _autoIntervalIndex = value; OnPropertyChanged(); PushDaemonConfig(); }
    }

    public int AutoThresholdIndex
    {
        get => _autoThresholdIndex;
        set { if (_autoThresholdIndex == value) return; _autoThresholdIndex = value; OnPropertyChanged(); PushDaemonConfig(); }
    }

    public bool ShowIntervalCombo { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = true;
    public bool ShowThresholdCombo { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public bool ShowHybridBadge { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }
    public string AutoCleanStatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public bool AutoCleanValueEnabled => AutoCleanEnabled && (AutoCleanMode)AutoModeIndex != AutoCleanMode.Hybrid;

    public int AutoCleanValue
    {
        get
        {
            return (AutoCleanMode)AutoModeIndex switch
            {
                AutoCleanMode.Interval => AutoIntervalIndex switch
                {
                    0 => 1,
                    1 => 5,
                    2 => 15,
                    3 => 30,
                    4 => 60,
                    _ => 15
                },
                AutoCleanMode.ThresholdPercent => AutoThresholdIndex switch
                {
                    0 => 70,
                    1 => 75,
                    2 => 80,
                    3 => 85,
                    4 => 90,
                    5 => 95,
                    _ => 80
                },
                _ => 15
            };
        }
    }

    public int RefreshRateIndex
    {
        get => _refreshRateIndex;
        set { if (_refreshRateIndex == value) return; _refreshRateIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(MonitorIntervalMs)); }
    }

    public int MonitorIntervalMs => RefreshRateIndex switch
    {
        0 => 500,
        2 => 2000,
        3 => 5000,
        4 => 10000,
        _ => 1000
    };

    public RamCleanerViewModel()
    {
        LastCleanedText = L("RamCleaner_NotCleaned");
        PressureText = L("RamCleaner_Pressure_Normal");
        GuardStatusText = L("RamCleaner_GuardActive");
        GpuNameShort = L("RamCleaner_GpuDetecting");

        _suppressConfigPush = true;
        try
        {
            _daemon = ServiceContainer.GetOptional<ISmartMemoryDaemonService>();
            if (_daemon != null)
            {
                AutoCleanEnabled = _daemon.IsEnabled;
                AutoModeIndex = (int)_daemon.Mode;
                AutoProfileIndex = _daemon.Profile switch
                {
                    MemoryCleanerService.CleanProfile.Quick => 1,
                    MemoryCleanerService.CleanProfile.Normal => 2,
                    MemoryCleanerService.CleanProfile.Deep => 3,
                    _ => 0
                };

                AppSettings? settings = ServiceContainer.GetOptional<SettingsService>()?.Settings;
                int intervalMinutes = _daemon.Mode == AutoCleanMode.Interval
                    ? _daemon.Value
                    : (settings?.AutoCleanIntervalMinutes ?? 15);
                AutoIntervalIndex = intervalMinutes switch
                {
                    <= 1 => 0,
                    <= 5 => 1,
                    <= 15 => 2,
                    <= 30 => 3,
                    _ => 4
                };

                int threshPercent = _daemon.Mode == AutoCleanMode.ThresholdPercent
                    ? _daemon.Value
                    : (settings?.AutoCleanThresholdPercent ?? 80);
                AutoThresholdIndex = threshPercent switch
                {
                    <= 70 => 0,
                    <= 75 => 1,
                    <= 80 => 2,
                    <= 85 => 3,
                    <= 90 => 4,
                    _ => 5
                };

                ProtectForeground = _daemon.ProtectForeground;
                AutoCleanStatusText = _daemon.StatusMessage;

                _daemon.StatusChanged += OnDaemonStatusChanged;
                _daemon.CleanExecuted += OnDaemonCleanExecuted;
            }
            else
            {
                AutoCleanStatusText = L("RamCleaner_AutoDisabled");
            }
        }
        finally
        {
            _suppressConfigPush = false;
        }

        UpdateAutoModeVisibility();
    }

    public void Attach()
    {
        if (_daemon != null)
        {
            _daemon.StatusChanged -= OnDaemonStatusChanged;
            _daemon.CleanExecuted -= OnDaemonCleanExecuted;
            _daemon.StatusChanged += OnDaemonStatusChanged;
            _daemon.CleanExecuted += OnDaemonCleanExecuted;
        }
        ResourceGuardService? guard = ServiceContainer.GetOptional<ResourceGuardService>();
        if (guard != null)
        {
            guard.PressureChanged -= OnPressureChanged;
            guard.GuardMessage -= OnGuardMessage;
            guard.PressureChanged += OnPressureChanged;
            guard.GuardMessage += OnGuardMessage;
        }
    }

    public void Detach()
    {
        if (_daemon != null)
        {
            _daemon.StatusChanged -= OnDaemonStatusChanged;
            _daemon.CleanExecuted -= OnDaemonCleanExecuted;
        }
        ResourceGuardService? guard = ServiceContainer.GetOptional<ResourceGuardService>();
        if (guard != null)
        {
            guard.PressureChanged -= OnPressureChanged;
            guard.GuardMessage -= OnGuardMessage;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        CancellationTokenSource? cts = Interlocked.Exchange(ref _cleanCts, null);
        try { cts?.Cancel(); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, $"{Subsystem}.Dispose.CancelCts"); }
        cts?.Dispose();
    }

    [RelayCommand]
    private Task CleanAsync() => SafeCleanAsync(MemoryCleanerService.CleanProfile.Smart);

    [RelayCommand]
    private Task QuickCleanAsync() => SafeCleanAsync(MemoryCleanerService.CleanProfile.Quick);

    [RelayCommand]
    private Task SmartCleanAsync() => SafeCleanAsync(MemoryCleanerService.CleanProfile.Smart);

    [RelayCommand]
    private Task NormalCleanAsync() => SafeCleanAsync(MemoryCleanerService.CleanProfile.Normal);

    [RelayCommand]
    private Task DeepCleanAsync() => SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);

    [RelayCommand]
    private async Task ClearShadersAsync()
    {
        try
        {
            long freed = await Task.Run(MemoryCleanerService.ClearDirectXShaderCaches);
            string freedText = MemoryCleanerService.FormatBytes(freed);
            GuardStatusText = TextFormatService.Format(L("RamCleaner_ShaderCleared"), freedText) + " - " + DateTime.Now.ToString("HH:mm:ss");
            if (NotifEnabled)
            {
                ToastService.Show(TextFormatService.Format(L("RamCleaner_ShaderCleared"), freedText));
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.ClearShaders");
        }
    }

    [RelayCommand]
    private void RestartGpuPipeline()
    {
        try
        {
            if (RestartGpuPipelineConfirm != null && !RestartGpuPipelineConfirm())
            {
                return;
            }

            MemoryCleanerService.RestartGraphicsDriverPipeline();
            GuardStatusText = L("RamCleaner_GpuPipelineReset") + " - " + DateTime.Now.ToString("HH:mm:ss");
            if (NotifEnabled)
            {
                ToastService.Show(L("RamCleaner_GpuPipelineToast"));
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.RestartGpuPipeline");
        }
    }

    [RelayCommand]
    private void ToggleNotif()
    {
        NotifEnabled = !NotifEnabled;
    }

    public void RefreshStats()
    {
        try
        {
            MemoryCleanerService.RamInfo info = MemoryCleanerService.GetRamInfo();

            PhysUsedText = MemoryCleanerService.FormatBytes(info.UsedPhysBytes);
            PhysAvailText = MemoryCleanerService.FormatBytes(info.AvailPhysBytes);
            PhysTotalText = MemoryCleanerService.FormatBytes(info.TotalPhysBytes);
            PhysPercent = $"{info.PhysUsedPercent:F1}%";
            PhysUsedPercent = info.PhysUsedPercent;

            VirtUsedText = MemoryCleanerService.FormatBytes(info.UsedVirtBytes);
            VirtAvailText = MemoryCleanerService.FormatBytes(info.AvailVirtBytes);
            VirtTotalText = MemoryCleanerService.FormatBytes(info.TotalVirtBytes);
            VirtPercent = $"{info.VirtUsedPercent:F1}%";
            VirtUsedPercent = info.VirtUsedPercent;

            long pfCapacity = Math.Max(0, info.TotalPageBytes - info.TotalPhysBytes);
            long pfUsed = Math.Max(0, info.UsedVirtBytes - info.UsedPhysBytes);
            long pfAvail = Math.Max(0, pfCapacity - pfUsed);
            PageUsedText = MemoryCleanerService.FormatBytes(pfUsed);
            PageAvailText = MemoryCleanerService.FormatBytes(pfAvail);
            PageTotalText = MemoryCleanerService.FormatBytes(pfCapacity);

            double cpuPct = MemoryCleanerService.GetSystemCpuPercent();
            SysCpuPercent = cpuPct;
            SysCpuText = $"{cpuPct:F1}%";
            SysCpuColor = cpuPct >= 90 ? _brushRed
                          : cpuPct >= 75 ? _brushOrange
                          : cpuPct >= 50 ? _brushYellow
                          : _brushGreen;

            ResourceGuardService guard = ServiceContainer.GetRequired<ResourceGuardService>();
            AppRamText = TextFormatService.Format(L("RamCleaner_AppRam"), guard.AppWorkingSetMb);
            AppCpuText = TextFormatService.Format(L("RamCleaner_AppCpu"), $"{guard.AppCpuPercent:F1}");
            double totalRamMb = info.TotalPhysBytes / (1024.0 * 1024.0);
            AppRamPercent = totalRamMb > 0 ? Math.Min(100.0, guard.AppWorkingSetMb / totalRamMb * 100.0) : 0;
            AppCpuPercent = Math.Min(100.0, Math.Max(0.0, guard.AppCpuPercent));

            PushHistory(info.PhysUsedPercent);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, $"{Subsystem}.RefreshStats"); }
    }

    public async Task RefreshGpuAsync()
    {
        try
        {
            MemoryCleanerService.GpuInfo gpu = await Task.Run(MemoryCleanerService.GetGpuInfo);
            string name = gpu.Name.Length > 22 ? gpu.Name[..22] + "…" : gpu.Name;
            string vram = gpu.AdapterRamMb > 0
                ? TextFormatService.Format(L("RamCleaner_GpuVram"), gpu.AdapterRamMb)
                : gpu.SharedUsedMb > 0 ? TextFormatService.Format(L("RamCleaner_GpuShared"), gpu.SharedUsedMb) : L("RamCleaner_GpuVramNA");
            string type = gpu.IsIntegrated ? L("RamCleaner_GpuIntegrated") : L("RamCleaner_GpuDedicated");

            RunOnUi(() =>
            {
                GpuNameShort = name;
                GpuVramText = vram;
                GpuTypeText = type;
            });
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, $"{Subsystem}.RefreshGpu"); }
    }

    public void RefreshProcesses()
    {
        try
        {
            List<MemoryCleanerService.ProcessMemInfo> procs = MemoryCleanerService.GetTopProcessesByMemory(8);
            double totalMb = MemoryCleanerService.GetRamInfo().TotalPhysBytes / (1024.0 * 1024.0);
            RunOnUi(() =>
            {
                TopProcesses.Clear();
                foreach (MemoryCleanerService.ProcessMemInfo p in procs)
                {
                    TopProcesses.Add(new ProcessItemVm
                    {
                        Name = p.Name,
                        RamText = $"{p.WorkingSetMb} MB",
                        WorkingSetPercent = totalMb > 0
                            ? Math.Min(100.0, p.WorkingSetMb / totalMb * 100.0)
                            : 0,
                    });
                }
            });
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, $"{Subsystem}.RefreshProcesses"); }
    }

    public double[] GetRamHistorySnapshot()
    {
        int n = _historyCount;
        var snapshot = new double[n];
        for (int i = 0; i < n; i++)
        {
            snapshot[i] = _ramHistory[(_historyHead - n + i + HistorySize) % HistorySize];
        }
        return snapshot;
    }

    private void PushHistory(double value)
    {
        _ramHistory[_historyHead] = value;
        _historyHead = (_historyHead + 1) % HistorySize;
        if (_historyCount < HistorySize)
        {
            _historyCount++;
        }
    }

    private async Task SafeCleanAsync(MemoryCleanerService.CleanProfile profile)
    {
        if (IsCleaning)
        {
            return;
        }

        IsCleaning = true;
        HasResult = false;
        CleaningStatus = L("RamCleaner_Status_Starting");

        try
        {
            CancellationTokenSource? previousCts = _cleanCts;
            _cleanCts = new CancellationTokenSource();
            try
            {
                previousCts?.Cancel();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
            {
                LoggingService.LogException(ex, $"{Subsystem}.SafeCleanAsync.CancelPreviousCts");
            }

            previousCts?.Dispose();

            CancellationToken ct = _cleanCts!.Token;

            CleaningStatus = L("RamCleaner_Status_ReadingMem");
            MemoryCleanerService.RamInfo before = await Task.Run(MemoryCleanerService.GetRamInfo);

            long ws = 0, mfc = 0, sfc = 0, rc = 0, sb = 0, lp = 0, cp = 0, mp = 0;
            long dns = 0, gc = 0, sbf = 0, kt = 0, hc = 0, cb = 0, arp = 0, nb = 0, sess = 0, sf = 0;
            long total = 0;

            if (profile == MemoryCleanerService.CleanProfile.Smart)
            {
                CleaningStatus = L("RamCleaner_Status_SmartClean");
                MemoryCleanerService.CleanResult smartRes = await MemoryCleanerService.SmartCleanAsync(
                    protectForeground: ProtectForeground,
                    purgeStandbyOnly: false,
                    ct: ct);

                ws = smartRes.WorkingSetsFreed;
                sb = smartRes.StandbyFreed;
                sbf = smartRes.StandbyFastFreed;
                lp = smartRes.LowPriorityStandbyFreed;
                sfc = smartRes.SystemFileCacheFreed;
                dns = smartRes.DnsCacheFreed;
                total = smartRes.TotalFreed;
            }
            else
            {
                if (profile is MemoryCleanerService.CleanProfile.Deep or MemoryCleanerService.CleanProfile.Quick
                    or MemoryCleanerService.CleanProfile.Normal)
                {
                    CleaningStatus = L("RamCleaner_Status_WorkingSets");
                    ws = await Task.Run(() => MemoryCleanerService.EmptyWorkingSets(protectForeground: ProtectForeground), ct);
                }

                if (profile is MemoryCleanerService.CleanProfile.Quick
                    or MemoryCleanerService.CleanProfile.Normal
                    or MemoryCleanerService.CleanProfile.Deep)
                {
                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_StandbyFast");
                    sbf = await Task.Run(MemoryCleanerService.FlushStandbyListFast, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_StandbyList");
                    sb = await Task.Run(MemoryCleanerService.FlushStandbyList, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_Dns");
                    dns = await Task.Run(MemoryCleanerService.FlushDnsCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_Gc");
                    gc = await Task.Run(MemoryCleanerService.ForceGcCollect, ct);
                }

                if (profile is MemoryCleanerService.CleanProfile.Normal
                    or MemoryCleanerService.CleanProfile.Deep)
                {
                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_ModFiles");
                    mfc = await Task.Run(MemoryCleanerService.FlushModifiedFileCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_SysFiles");
                    sfc = await Task.Run(MemoryCleanerService.ClearSystemFileCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_RegCache");
                    rc = await Task.Run(MemoryCleanerService.FlushRegistryCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_LowPriStandby");
                    lp = await Task.Run(MemoryCleanerService.FlushLowPriorityStandbyList, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_CombPages");
                    cp = await Task.Run(MemoryCleanerService.FlushCombinedPageList, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_ModPages");
                    mp = await Task.Run(MemoryCleanerService.FlushModifiedPageList, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_CompactHeaps");
                    hc = await Task.Run(MemoryCleanerService.CompactAllHeaps, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_TrimSessions");
                    sess = await Task.Run(MemoryCleanerService.TrimAllSessionsWorkingSets, ct);
                }

                if (profile == MemoryCleanerService.CleanProfile.Deep)
                {
                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_KernelWS");
                    kt = await Task.Run(MemoryCleanerService.TrimKernelWorkingSet, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_Clipboard");
                    cb = await Task.Run(MemoryCleanerService.ClearClipboard, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_ArpCache");
                    arp = await Task.Run(MemoryCleanerService.FlushArpCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_NetBiosCache");
                    nb = await Task.Run(MemoryCleanerService.FlushNetBiosCache, ct);

                    ct.ThrowIfCancellationRequested();
                    CleaningStatus = L("RamCleaner_Status_SysMain");
                    try { sf = await MemoryCleanerService.FlushSuperFetchAsync(ct); } catch (Exception ex) when (ex is not System.OutOfMemoryException) { LoggingService.LogException(ex, $"{Subsystem}.SafeCleanAsync.FlushSuperFetch"); }
                }

                long sumParts = ws + mfc + sfc + rc + sb + lp + cp + mp
                    + dns + gc + sbf + kt + hc + cb + arp + nb + sess + sf;

                CleaningStatus = L("RamCleaner_Status_Measuring");
                await Task.Delay(500, CancellationToken.None);
                MemoryCleanerService.RamInfo after = await Task.Run(MemoryCleanerService.GetRamInfo);
                long globalDelta = Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
                total = Math.Max(sumParts, globalDelta);
            }

            ResultWorkingSets = MemoryCleanerService.FormatBytes(ws);
            ResultStandby = MemoryCleanerService.FormatBytes(sb + sbf);
            ResultLowPriorityStandby = MemoryCleanerService.FormatBytes(lp);
            ResultModifiedPages = MemoryCleanerService.FormatBytes(mp);
            ResultCombinedPages = MemoryCleanerService.FormatBytes(cp);
            ResultModifiedFileCache = MemoryCleanerService.FormatBytes(mfc);
            ResultSystemFileCache = MemoryCleanerService.FormatBytes(sfc);
            ResultRegistryCache = MemoryCleanerService.FormatBytes(rc);
            ResultDnsCache = MemoryCleanerService.FormatBytes(dns + arp + nb);
            ResultTotal = MemoryCleanerService.FormatBytes(total);
            LastCleanedText = L("RamCleaner_LastCleaned") + DateTime.Now.ToString("HH:mm:ss");
            HasResult = true;
            CleaningStatus = L("RamCleaner_Done");

            _cumulativeFreedBytes += total;
            CumulativeFreedText = MemoryCleanerService.FormatBytes(_cumulativeFreedBytes);

            if (NotifEnabled)
            {
                ToastService.Show(L("RamCleaner_Freed") + MemoryCleanerService.FormatBytes(total));
            }

            RefreshStats();
            RefreshProcesses();
        }
        catch (OperationCanceledException ex) { _ = ex; }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            CleaningStatus = "Erreur : " + ex.Message;
            ResultTotal = "Erreur";
            HasResult = true;
            LoggingService.LogException(ex, $"{Subsystem}.SafeCleanAsync");
        }
        finally { IsCleaning = false; }
    }

    private void PushDaemonConfig()
    {
        if (_suppressConfigPush || _daemon == null)
        {
            return;
        }

        var mode = (AutoCleanMode)Math.Clamp(AutoModeIndex, 0, 2);

        MemoryCleanerService.CleanProfile profile = AutoProfileIndex switch
        {
            1 => MemoryCleanerService.CleanProfile.Quick,
            2 => MemoryCleanerService.CleanProfile.Normal,
            3 => MemoryCleanerService.CleanProfile.Deep,
            _ => MemoryCleanerService.CleanProfile.Smart
        };

        _daemon.Configure(AutoCleanEnabled, mode, AutoCleanValue, ProtectForeground, profile);
        AutoCleanStatusText = _daemon.StatusMessage;
        OnPropertyChanged(nameof(AutoCleanValue));
        OnPropertyChanged(nameof(AutoCleanValueEnabled));
    }

    private void UpdateAutoModeVisibility()
    {
        int modeIdx = Math.Clamp(AutoModeIndex, 0, 2);
        ShowIntervalCombo = modeIdx == 0;
        ShowThresholdCombo = modeIdx == 1;
        ShowHybridBadge = modeIdx == 2;
    }

    private void OnDaemonStatusChanged(string status)
    {
        RunOnUi(() => AutoCleanStatusText = status);
    }

    private void OnDaemonCleanExecuted(AutoCleanResultEventArgs e)
    {
        RunOnUi(() =>
        {
            LastCleanedText = $"Auto-clean ({e.TriggerMode}) : {e.Timestamp.ToLocalTime():HH:mm:ss}";
            _cumulativeFreedBytes += e.FreedBytes;
            CumulativeFreedText = MemoryCleanerService.FormatBytes(_cumulativeFreedBytes);
            RefreshStats();
            RefreshProcesses();
        });
    }

    private void OnPressureChanged(ResourceGuardService.PressureLevel level)
    {
        RunOnUi(() =>
        {
            (PressureText, PressureColor, PressureBorderColor) = level switch
            {
                ResourceGuardService.PressureLevel.Critical =>
                    (L("RamCleaner_Pressure_Critical"), _brushRed, _brushBorderCritical),
                ResourceGuardService.PressureLevel.High =>
                    (L("RamCleaner_Pressure_High"), _brushOrange, _brushBorderHigh),
                ResourceGuardService.PressureLevel.Elevated =>
                    (L("RamCleaner_Pressure_Elevated"), _brushYellow, _brushBorderElevated),
                _ =>
                    (L("RamCleaner_Pressure_Normal"), _brushGreen, _brushBorderNormal),
            };
        });
    }

    private void OnGuardMessage(string msg)
    {
        RunOnUi(() => GuardStatusText = msg);
    }

    private static string L(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { return key; }
    }

    private static void RunOnUi(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.InvokeAsync(action);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? p = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
