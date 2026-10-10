using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Coclico.Services;

namespace Coclico.Views;

public sealed class ProcessItemVm
{
    public string Name { get; init; } = string.Empty;
    public string RamText { get; init; } = string.Empty;
    public double WorkingSetPercent { get; init; }
}

public partial class RamCleanerView : UserControl, INotifyPropertyChanged
{
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _gpuTimer = new();
    private readonly ISmartMemoryDaemonService? _daemon;
    private CancellationTokenSource? _cleanCts;

    private readonly bool _suppressConfigPush;
    private string _lastCleanedText = string.Empty;
    private bool _notifEnabled = true;
    private bool _autoCleanEnabled;
    private AutoCleanMode _autoCleanMode = AutoCleanMode.Interval;
    private int _autoCleanValue = 15;
    private bool _protectForeground = true;
    private string _autoCleanStatusText = string.Empty;
    private long _cumulativeFreedBytes;
    private string _gpuNameShort = string.Empty;
    private string _pressureText = string.Empty;
    private string _guardStatusText = string.Empty;

    private const int HistorySize = 90;
    private readonly double[] _ramHistory = new double[HistorySize];
    private int _historyHead;
    private int _historyCount;
    private Polyline? _sparkline;

    private static readonly Brush _brushGreen = FreezeBrush(new SolidColorBrush(Color.FromRgb(34, 197, 94)));
    private static readonly Brush _brushYellow = FreezeBrush(new SolidColorBrush(Color.FromRgb(234, 179, 8)));
    private static readonly Brush _brushOrange = FreezeBrush(new SolidColorBrush(Color.FromRgb(249, 115, 22)));
    private static readonly Brush _brushRed = FreezeBrush(new SolidColorBrush(Color.FromRgb(239, 68, 68)));
    private static readonly Brush _brushBorderNormal = FreezeBrush(new SolidColorBrush(Color.FromRgb(26, 16, 48)));
    private static readonly Brush _brushBorderElevated = FreezeBrush(new SolidColorBrush(Color.FromRgb(113, 63, 18)));
    private static readonly Brush _brushBorderHigh = FreezeBrush(new SolidColorBrush(Color.FromRgb(127, 29, 29)));
    private static readonly Brush _brushBorderCritical = FreezeBrush(new SolidColorBrush(Color.FromRgb(185, 28, 28)));
    private static readonly Brush _sparklineBrush =
        FreezeBrush(new SolidColorBrush(Color.FromRgb(124, 58, 237)));

    private static Brush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

    public ObservableCollection<ProcessItemVm> TopProcesses { get; } = [];

    public bool IsCleaning { get; private set { field = value; Notify(); Notify(nameof(IsNotCleaning)); } }
    public bool IsNotCleaning => !IsCleaning;
    public bool HasResult { get; private set { field = value; Notify(); } }
    public string CleaningStatus { get; private set { field = value; Notify(); } } = string.Empty;
    public string LastCleanedText { get => _lastCleanedText; private set { _lastCleanedText = value; Notify(); } }
    public string NotifButtonLabel => _notifEnabled ? L("RamCleaner_NotifOn") : L("RamCleaner_NotifOff");
    public bool AutoCleanEnabled
    {
        get => _autoCleanEnabled;
        set
        {
            _autoCleanEnabled = value;
            Notify();
            Notify(nameof(AutoCleanValueEnabled));
            PushDaemonConfig();
        }
    }
    public int AutoCleanValue
    {
        get => _autoCleanValue;
        set
        {
            _autoCleanValue = Math.Max(1, value);
            Notify();
            PushDaemonConfig();
        }
    }
    public bool ProtectForeground
    {
        get => _protectForeground;
        set
        {
            _protectForeground = value;
            Notify();
            PushDaemonConfig();
        }
    }
    public bool AutoCleanValueEnabled => _autoCleanEnabled && _autoCleanMode != AutoCleanMode.Hybrid;
    public string AutoCleanStatusText
    {
        get => _autoCleanStatusText;
        private set { _autoCleanStatusText = value; Notify(); }
    }

    public string ResultWorkingSets { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultStandby { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultLowPriorityStandby { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultModifiedPages { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultCombinedPages { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultModifiedFileCache { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultSystemFileCache { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultRegistryCache { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultDnsCache { get; private set { field = value; Notify(); } } = "\u2014";
    public string ResultTotal { get; private set { field = value; Notify(); } } = "\u2014";

    public string PhysUsedText { get; private set { field = value; Notify(); } } = "\u2014";
    public string PhysAvailText { get; private set { field = value; Notify(); } } = "\u2014";
    public string PhysTotalText { get; private set { field = value; Notify(); } } = "\u2014";
    public string PhysPercent { get; private set { field = value; Notify(); } } = "\u2014";
    public double PhysUsedPercent { get; private set { field = value; Notify(); } }
    public string VirtUsedText { get; private set { field = value; Notify(); } } = "\u2014";
    public string VirtAvailText { get; private set { field = value; Notify(); } } = "\u2014";
    public string VirtTotalText { get; private set { field = value; Notify(); } } = "\u2014";
    public string VirtPercent { get; private set { field = value; Notify(); } } = "\u2014";
    public double VirtUsedPercent { get; private set { field = value; Notify(); } }
    public string PageUsedText { get; private set { field = value; Notify(); } } = "\u2014";
    public string PageAvailText { get; private set { field = value; Notify(); } } = "\u2014";
    public string PageTotalText { get; private set { field = value; Notify(); } } = "\u2014";

    public double SysCpuPercent { get; private set { field = value; Notify(); } }
    public string SysCpuText { get; private set { field = value; Notify(); } } = "\u2014";
    public Brush SysCpuColor { get; private set { field = value; Notify(); } } = _brushGreen;
    public string GpuNameShort { get => _gpuNameShort; private set { _gpuNameShort = value; Notify(); } }
    public string GpuVramText { get; private set { field = value; Notify(); } } = "\u2014";
    public string GpuTypeText { get; private set { field = value; Notify(); } } = "\u2014";
    public string AppRamText { get; private set { field = value; Notify(); } } = "\u2014";
    public double AppRamPercent { get; private set { field = value; Notify(); } }
    public string AppCpuText { get; private set { field = value; Notify(); } } = "\u2014";
    public double AppCpuPercent { get; private set { field = value; Notify(); } }
    public string PressureText { get => _pressureText; private set { _pressureText = value; Notify(); } }
    public Brush PressureColor { get; private set { field = value; Notify(); } } = _brushGreen;
    public Brush PressureBorderColor { get; private set { field = value; Notify(); } } = _brushBorderNormal;
    public string CumulativeFreedText { get; private set { field = value; Notify(); } } = "0 B";
    public string GuardStatusText { get => _guardStatusText; private set { _guardStatusText = value; Notify(); } }

    private static string L(string key)
    {
        try
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }
        catch { return key; }
    }

    public RamCleanerView()
    {
        _daemon = ServiceContainer.GetOptional<ISmartMemoryDaemonService>();
        InitializeComponent();
        DataContext = this;

        _lastCleanedText = L("RamCleaner_NotCleaned");
        _pressureText = L("RamCleaner_Pressure_Normal");
        _guardStatusText = L("RamCleaner_GuardActive");
        _gpuNameShort = L("RamCleaner_GpuDetecting");

        _suppressConfigPush = true;
        try
        {
            if (_daemon != null)
            {
                _autoCleanEnabled = _daemon.IsEnabled;
                _autoCleanMode = _daemon.Mode;
                _autoCleanValue = _daemon.Value;
                _protectForeground = _daemon.ProtectForeground;
                _autoCleanStatusText = _daemon.StatusMessage;

                ChkAutoClean.IsChecked = _daemon.IsEnabled;
                CboAutoProfile?.SelectedIndex = _daemon.Profile switch
                {
                    MemoryCleanerService.CleanProfile.Quick => 1,
                    MemoryCleanerService.CleanProfile.Normal => 2,
                    MemoryCleanerService.CleanProfile.Deep => 3,
                    _ => 0
                };
                CboAutoMode?.SelectedIndex = (int)_daemon.Mode;

                AppSettings? settings = ServiceContainer.GetOptional<SettingsService>()?.Settings;
                if (CboAutoInterval != null)
                {
                    int intervalMinutes = _daemon.Mode == AutoCleanMode.Interval
                        ? _daemon.Value
                        : (settings?.AutoCleanIntervalMinutes ?? 15);
                    CboAutoInterval.SelectedIndex = intervalMinutes switch
                    {
                        <= 1 => 0,
                        <= 5 => 1,
                        <= 15 => 2,
                        <= 30 => 3,
                        _ => 4
                    };
                }

                if (CboAutoThreshold != null)
                {
                    int threshPercent = _daemon.Mode == AutoCleanMode.ThresholdPercent
                        ? _daemon.Value
                        : (settings?.AutoCleanThresholdPercent ?? 80);
                    CboAutoThreshold.SelectedIndex = threshPercent switch
                    {
                        <= 70 => 0,
                        <= 75 => 1,
                        <= 80 => 2,
                        <= 85 => 3,
                        <= 90 => 4,
                        _ => 5
                    };
                }

                UpdateAutoModeVisibility();
                ChkProtectForeground.IsChecked = _daemon.ProtectForeground;

                _daemon.StatusChanged += OnDaemonStatusChanged;
                _daemon.CleanExecuted += OnDaemonCleanExecuted;
            }
            else
            {
                _autoCleanStatusText = L("RamCleaner_AutoDisabled");
            }
        }
        finally
        {
            _suppressConfigPush = false;
        }

        _monitorTimer.Interval = TimeSpan.FromSeconds(3);
        _monitorTimer.Tick += (_, _) => RefreshStats();

        _gpuTimer.Interval = TimeSpan.FromSeconds(60);
        _gpuTimer.Tick += (_, _) => Task.Run(RefreshGpu);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
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

        _monitorTimer.Start();
        _gpuTimer.Start();

        RefreshStats();
        _ = Task.Run(RefreshGpu);
        RefreshProcesses();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
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
        _monitorTimer.Stop();
        _gpuTimer.Stop();
    }

    private void RefreshStats()
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
            AppRamText = string.Format(L("RamCleaner_AppRam"), guard.AppWorkingSetMb);
            AppCpuText = string.Format(L("RamCleaner_AppCpu"), $"{guard.AppCpuPercent:F1}");
            double totalRamMb = info.TotalPhysBytes / (1024.0 * 1024.0);
            AppRamPercent = totalRamMb > 0 ? Math.Min(100.0, guard.AppWorkingSetMb / totalRamMb * 100.0) : 0;
            AppCpuPercent = Math.Min(100.0, Math.Max(0.0, guard.AppCpuPercent));

            PushHistory(info.PhysUsedPercent);
            DrawSparkline();
        }
        catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.RefreshStats"); }
    }

    private void RefreshGpu()
    {
        try
        {
            MemoryCleanerService.GpuInfo gpu = MemoryCleanerService.GetGpuInfo();
            string name = gpu.Name.Length > 22 ? gpu.Name[..22] + "\u2026" : gpu.Name;
            string vram = gpu.AdapterRamMb > 0
                ? string.Format(L("RamCleaner_GpuVram"), gpu.AdapterRamMb)
                : gpu.SharedUsedMb > 0 ? string.Format(L("RamCleaner_GpuShared"), gpu.SharedUsedMb) : L("RamCleaner_GpuVramNA");
            string type = gpu.IsIntegrated ? L("RamCleaner_GpuIntegrated") : L("RamCleaner_GpuDedicated");

            _ = Dispatcher.InvokeAsync(() =>
            {
                GpuNameShort = name;
                GpuVramText = vram;
                GpuTypeText = type;
            });
        }
        catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.RefreshGpu"); }
    }

    private void RefreshProcesses()
    {
        try
        {
            List<MemoryCleanerService.ProcessMemInfo> procs = MemoryCleanerService.GetTopProcessesByMemory(8);
            double totalMb = MemoryCleanerService.GetRamInfo().TotalPhysBytes / (1024.0 * 1024.0);
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
        }
        catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.RefreshProcesses"); }
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

    private void DrawSparkline()
    {
        if (HistoryCanvas == null || HistoryCanvas.ActualWidth <= 0 || _historyCount < 2)
        {
            return;
        }

        if (_sparkline == null)
        {
            _sparkline = new Polyline
            {
                Stroke = _sparklineBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
            };
            HistoryCanvas.Children.Clear();
            _ = HistoryCanvas.Children.Add(_sparkline);
        }

        double w = HistoryCanvas.ActualWidth;
        double h = HistoryCanvas.ActualHeight > 0 ? HistoryCanvas.ActualHeight : 70;
        int n = _historyCount;

        var points = new PointCollection(n);
        for (int i = 0; i < n; i++)
        {
            int idx = (_historyHead - n + i + HistorySize) % HistorySize;
            double x = i * (w / (n - 1));
            double y = (1.0 - (_ramHistory[idx] / 100.0)) * h;
            points.Add(new Point(x, y));
        }
        _sparkline.Points = points;
    }

    private void HistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _sparkline = null;
        DrawSparkline();
    }

    private void OnPressureChanged(ResourceGuardService.PressureLevel level)
    {
        _ = Dispatcher.InvokeAsync(() =>
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
        _ = Dispatcher.InvokeAsync(() => GuardStatusText = msg);
    }

    private async void BtnClean_Click(object sender, RoutedEventArgs e)
    {
        await SafeCleanAsync(MemoryCleanerService.CleanProfile.Smart);
    }

    private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
    {
        await SafeCleanAsync(MemoryCleanerService.CleanProfile.Quick);
    }

    private async void BtnSmartClean_Click(object sender, RoutedEventArgs e)
    {
        await SafeCleanAsync(MemoryCleanerService.CleanProfile.Smart);
    }

    private async void BtnNormalClean_Click(object sender, RoutedEventArgs e)
    {
        await SafeCleanAsync(MemoryCleanerService.CleanProfile.Normal);
    }

    private async void BtnDeepClean_Click(object sender, RoutedEventArgs e)
    {
        await SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);
    }

    private async void BtnClearShaders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            long freed = await Task.Run(MemoryCleanerService.ClearDirectXShaderCaches);
            string freedText = MemoryCleanerService.FormatBytes(freed);
            GuardStatusText = string.Format(L("RamCleaner_ShaderCleared"), freedText) + " — " + DateTime.Now.ToString("HH:mm:ss");
            if (_notifEnabled)
            {
                ToastService.Show(string.Format(L("RamCleaner_ShaderCleared"), freedText));
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "RamCleanerView.BtnClearShaders_Click");
        }
    }

    private void BtnRestartGpuPipeline_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult confirm = MessageBox.Show(
            L("RamCleaner_GpuPipelineConfirm"),
            L("RamCleaner_GpuPipelineTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            MemoryCleanerService.RestartGraphicsDriverPipeline();
            GuardStatusText = L("RamCleaner_GpuPipelineReset") + " — " + DateTime.Now.ToString("HH:mm:ss");
            if (_notifEnabled)
            {
                ToastService.Show(L("RamCleaner_GpuPipelineToast"));
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "RamCleanerView.BtnRestartGpuPipeline_Click");
        }
    }

    private async void BtnTrimSelf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(MemoryCleanerService.TrimSelfWorkingSet);
            GuardStatusText = L("RamCleaner_WorkingSetFreed") + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.BtnTrimSelf_Click"); }
    }

    private void BtnRefreshProc_Click(object sender, RoutedEventArgs e)
    {
        RefreshProcesses();
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
            CancellationTokenSource? oldCts = Interlocked.Exchange(ref _cleanCts, new CancellationTokenSource());
            try { oldCts?.Cancel(); } catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.SafeCleanAsync.CancelPreviousCts"); }
            try { oldCts?.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.SafeCleanAsync.DisposePreviousCts"); }

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
                    protectForeground: _protectForeground,
                    purgeStandbyOnly: false,
                    ct: ct);

                ws = smartRes.WorkingSetsFreed;
                sb = smartRes.StandbyFreed;
                sbf = smartRes.StandbyFastFreed;
                lp = smartRes.LowPriorityStandbyFreed;
                sfc = smartRes.SystemFileCacheFreed;
                dns = smartRes.DnsCacheFreed;
                gc = smartRes.GcCollectFreed;
                total = smartRes.TotalFreed;
            }
            else
            {
                if (profile is MemoryCleanerService.CleanProfile.Deep or MemoryCleanerService.CleanProfile.Quick
                    or MemoryCleanerService.CleanProfile.Normal)
                {
                    CleaningStatus = L("RamCleaner_Status_WorkingSets");
                    ws = await Task.Run(() => MemoryCleanerService.EmptyWorkingSets(protectForeground: _protectForeground), ct);
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
                    try { sf = await MemoryCleanerService.FlushSuperFetchAsync(ct); } catch (Exception ex) { LoggingService.LogException(ex, "RamCleanerView.SafeCleanAsync.FlushSuperFetch"); }
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

            if (_notifEnabled)
            {
                ToastService.Show(L("RamCleaner_Freed") + MemoryCleanerService.FormatBytes(total));
            }

            RefreshStats();
            RefreshProcesses();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CleaningStatus = "Erreur : " + ex.Message;
            ResultTotal = "Erreur";
            HasResult = true;
            LoggingService.LogException(ex, "RamCleanerView.SafeCleanAsync");
        }
        finally { IsCleaning = false; }
    }

    private void BtnToggleNotif_Click(object sender, RoutedEventArgs e)
    {
        _notifEnabled = !_notifEnabled;
        Notify(nameof(NotifButtonLabel));
    }

    private void UpdateAutoModeVisibility()
    {
        int modeIdx = CboAutoMode?.SelectedIndex ?? 0;
        CboAutoInterval?.Visibility = modeIdx == 0 ? Visibility.Visible : Visibility.Collapsed;
        CboAutoThreshold?.Visibility = modeIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
        BrdAutoHybrid?.Visibility = modeIdx == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PushDaemonConfig()
    {
        if (_suppressConfigPush || _daemon == null)
        {
            return;
        }

        var mode = (AutoCleanMode)(CboAutoMode?.SelectedIndex is >= 0 and <= 2 ? CboAutoMode.SelectedIndex : 0);
        _autoCleanMode = mode;

        MemoryCleanerService.CleanProfile profile = CboAutoProfile?.SelectedIndex switch
        {
            1 => MemoryCleanerService.CleanProfile.Quick,
            2 => MemoryCleanerService.CleanProfile.Normal,
            3 => MemoryCleanerService.CleanProfile.Deep,
            _ => MemoryCleanerService.CleanProfile.Smart
        };

        int val = 15;
        if (mode == AutoCleanMode.Interval)
        {
            val = CboAutoInterval?.SelectedIndex switch
            {
                0 => 1,
                1 => 5,
                2 => 15,
                3 => 30,
                4 => 60,
                _ => 15
            };
        }
        else if (mode == AutoCleanMode.ThresholdPercent)
        {
            val = CboAutoThreshold?.SelectedIndex switch
            {
                0 => 70,
                1 => 75,
                2 => 80,
                3 => 85,
                4 => 90,
                5 => 95,
                _ => 80
            };
        }
        _autoCleanValue = val;

        bool isEnabled = ChkAutoClean?.IsChecked == true;
        bool protectFg = ChkProtectForeground?.IsChecked == true;

        _daemon.Configure(isEnabled, mode, val, protectFg, profile);
        AutoCleanStatusText = _daemon.StatusMessage;
        Notify(nameof(AutoCleanValue));
        Notify(nameof(AutoCleanValueEnabled));
    }

    private void CboAutoProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressConfigPush || CboAutoProfile == null)
        {
            return;
        }

        PushDaemonConfig();
    }

    private void ChkAutoClean_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressConfigPush)
        {
            return;
        }

        _autoCleanEnabled = ChkAutoClean?.IsChecked == true;
        Notify(nameof(AutoCleanEnabled));
        Notify(nameof(AutoCleanValueEnabled));
        PushDaemonConfig();
    }

    private void CboAutoMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateAutoModeVisibility();
        if (_suppressConfigPush || CboAutoMode == null)
        {
            return;
        }

        _autoCleanMode = (AutoCleanMode)Math.Clamp(CboAutoMode.SelectedIndex, 0, 2);
        Notify(nameof(AutoCleanValueEnabled));
        PushDaemonConfig();
    }

    private void CboAutoInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressConfigPush || CboAutoInterval == null)
        {
            return;
        }

        PushDaemonConfig();
    }

    private void CboAutoThreshold_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressConfigPush || CboAutoThreshold == null)
        {
            return;
        }

        PushDaemonConfig();
    }

    private void ChkProtectForeground_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressConfigPush)
        {
            return;
        }

        _protectForeground = ChkProtectForeground?.IsChecked == true;
        Notify(nameof(ProtectForeground));
        PushDaemonConfig();
    }

    private void OnDaemonStatusChanged(string status)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            AutoCleanStatusText = status;
        });
    }

    private void OnDaemonCleanExecuted(AutoCleanResultEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            LastCleanedText = $"Auto-clean ({e.TriggerMode}) : {e.Timestamp.ToLocalTime():HH:mm:ss}";
            _cumulativeFreedBytes += e.FreedBytes;
            CumulativeFreedText = MemoryCleanerService.FormatBytes(_cumulativeFreedBytes);
            RefreshStats();
            RefreshProcesses();
        });
    }

    private void CboRefreshRate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CboRefreshRate == null)
        {
            return;
        }

        double ms = CboRefreshRate.SelectedIndex switch
        {
            0 => 500,
            2 => 2000,
            3 => 5000,
            4 => 10000,
            _ => 1000
        };
        _monitorTimer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? p = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}