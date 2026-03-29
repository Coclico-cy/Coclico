#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly DispatcherTimer _autoCleanTimer = new();
    private readonly DispatcherTimer _gpuTimer = new();
    private CancellationTokenSource? _cleanCts;

    private bool _isCleaning;
    private bool _hasResult;
    private string _cleaningStatus = string.Empty;
    private string _lastCleanedText = string.Empty;
    private bool _notifEnabled = true;
    private bool _autoCleanEnabled;
    private int _autoCleanValue = 30;
    private bool _autoCleanByThreshold;
    private long _cumulativeFreedBytes;

    private string _resultWorkingSets = "\u2014";
    private string _resultStandby = "\u2014";
    private string _resultLowPriStandby = "\u2014";
    private string _resultModifiedPages = "\u2014";
    private string _resultCombinedPages = "\u2014";
    private string _resultModifiedFileCache = "\u2014";
    private string _resultSystemFileCache = "\u2014";
    private string _resultRegistryCache = "\u2014";
    private string _resultDnsCache = "\u2014";
    private string _resultTotal = "\u2014";

    private string _physUsedText = "\u2014";
    private string _physAvailText = "\u2014";
    private string _physTotalText = "\u2014";
    private string _physPercent = "\u2014";
    private double _physUsedPercent;
    private string _virtUsedText = "\u2014";
    private string _virtAvailText = "\u2014";
    private string _virtTotalText = "\u2014";
    private string _virtPercent = "\u2014";
    private double _virtUsedPercent;
    private string _pageUsedText = "\u2014";
    private string _pageAvailText = "\u2014";
    private string _pageTotalText = "\u2014";

    private double _sysCpuPercent;
    private string _sysCpuText = "\u2014";
    private Brush _sysCpuColor = _brushGreen;
    private string _gpuNameShort = string.Empty;
    private string _gpuVramText = "\u2014";
    private string _gpuTypeText = "\u2014";
    private string _appRamText = "\u2014";
    private string _appCpuText = "\u2014";
    private string _pressureText = string.Empty;
    private Brush _pressureColor = _brushGreen;
    private Brush _pressureBorderColor = _brushBorderNormal;
    private string _cumulativeFreedText = "0 B";
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

    private static Brush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

    public ObservableCollection<ProcessItemVm> TopProcesses { get; } = [];

    public bool IsCleaning { get => _isCleaning; private set { _isCleaning = value; Notify(); Notify(nameof(IsNotCleaning)); } }
    public bool IsNotCleaning => !_isCleaning;
    public bool HasResult { get => _hasResult; private set { _hasResult = value; Notify(); } }
    public string CleaningStatus { get => _cleaningStatus; private set { _cleaningStatus = value; Notify(); } }
    public string LastCleanedText { get => _lastCleanedText; private set { _lastCleanedText = value; Notify(); } }
    public string NotifButtonLabel => _notifEnabled ? L("RamCleaner_NotifOn") : L("RamCleaner_NotifOff");
    public bool AutoCleanEnabled { get => _autoCleanEnabled; set { _autoCleanEnabled = value; Notify(); Notify(nameof(AutoCleanStatusText)); UpdateAutoCleanTimer(); } }
    public int AutoCleanValue { get => _autoCleanValue; set { _autoCleanValue = Math.Max(1, value); Notify(); UpdateAutoCleanTimer(); } }
    public string AutoCleanStatusText => BuildAutoStatusText();

    public string ResultWorkingSets { get => _resultWorkingSets; private set { _resultWorkingSets = value; Notify(); } }
    public string ResultStandby { get => _resultStandby; private set { _resultStandby = value; Notify(); } }
    public string ResultLowPriorityStandby { get => _resultLowPriStandby; private set { _resultLowPriStandby = value; Notify(); } }
    public string ResultModifiedPages { get => _resultModifiedPages; private set { _resultModifiedPages = value; Notify(); } }
    public string ResultCombinedPages { get => _resultCombinedPages; private set { _resultCombinedPages = value; Notify(); } }
    public string ResultModifiedFileCache { get => _resultModifiedFileCache; private set { _resultModifiedFileCache = value; Notify(); } }
    public string ResultSystemFileCache { get => _resultSystemFileCache; private set { _resultSystemFileCache = value; Notify(); } }
    public string ResultRegistryCache { get => _resultRegistryCache; private set { _resultRegistryCache = value; Notify(); } }
    public string ResultDnsCache { get => _resultDnsCache; private set { _resultDnsCache = value; Notify(); } }
    public string ResultTotal { get => _resultTotal; private set { _resultTotal = value; Notify(); } }

    public string PhysUsedText { get => _physUsedText; private set { _physUsedText = value; Notify(); } }
    public string PhysAvailText { get => _physAvailText; private set { _physAvailText = value; Notify(); } }
    public string PhysTotalText { get => _physTotalText; private set { _physTotalText = value; Notify(); } }
    public string PhysPercent { get => _physPercent; private set { _physPercent = value; Notify(); } }
    public double PhysUsedPercent { get => _physUsedPercent; private set { _physUsedPercent = value; Notify(); } }
    public string VirtUsedText { get => _virtUsedText; private set { _virtUsedText = value; Notify(); } }
    public string VirtAvailText { get => _virtAvailText; private set { _virtAvailText = value; Notify(); } }
    public string VirtTotalText { get => _virtTotalText; private set { _virtTotalText = value; Notify(); } }
    public string VirtPercent { get => _virtPercent; private set { _virtPercent = value; Notify(); } }
    public double VirtUsedPercent { get => _virtUsedPercent; private set { _virtUsedPercent = value; Notify(); } }
    public string PageUsedText { get => _pageUsedText; private set { _pageUsedText = value; Notify(); } }
    public string PageAvailText { get => _pageAvailText; private set { _pageAvailText = value; Notify(); } }
    public string PageTotalText { get => _pageTotalText; private set { _pageTotalText = value; Notify(); } }

    public double SysCpuPercent { get => _sysCpuPercent; private set { _sysCpuPercent = value; Notify(); } }
    public string SysCpuText { get => _sysCpuText; private set { _sysCpuText = value; Notify(); } }
    public Brush SysCpuColor { get => _sysCpuColor; private set { _sysCpuColor = value; Notify(); } }
    public string GpuNameShort { get => _gpuNameShort; private set { _gpuNameShort = value; Notify(); } }
    public string GpuVramText { get => _gpuVramText; private set { _gpuVramText = value; Notify(); } }
    public string GpuTypeText { get => _gpuTypeText; private set { _gpuTypeText = value; Notify(); } }
    public string AppRamText { get => _appRamText; private set { _appRamText = value; Notify(); } }
    public string AppCpuText { get => _appCpuText; private set { _appCpuText = value; Notify(); } }
    public string PressureText { get => _pressureText; private set { _pressureText = value; Notify(); } }
    public Brush PressureColor { get => _pressureColor; private set { _pressureColor = value; Notify(); } }
    public Brush PressureBorderColor { get => _pressureBorderColor; private set { _pressureBorderColor = value; Notify(); } }
    public string CumulativeFreedText { get => _cumulativeFreedText; private set { _cumulativeFreedText = value; Notify(); } }
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
        InitializeComponent();
        DataContext = this;

        _lastCleanedText = L("RamCleaner_NotCleaned");
        _pressureText = L("RamCleaner_Pressure_Normal");
        _guardStatusText = L("RamCleaner_GuardActive");
        _gpuNameShort = L("RamCleaner_GpuDetecting");

        _monitorTimer.Interval = TimeSpan.FromSeconds(2);
        _monitorTimer.Tick += (_, _) => RefreshStats();
        _monitorTimer.Start();

        _autoCleanTimer.Tick += async (_, _) => await SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);

        _gpuTimer.Interval = TimeSpan.FromSeconds(60);
        _gpuTimer.Tick += (_, _) => Task.Run(RefreshGpu);
        _gpuTimer.Start();

        ServiceContainer.GetRequired<ResourceGuardService>().PressureChanged += OnPressureChanged;
        ServiceContainer.GetRequired<ResourceGuardService>().GuardMessage += OnGuardMessage;

        RefreshStats();
        Task.Run(RefreshGpu);
        RefreshProcesses();
    }

    private void RefreshStats()
    {
        try
        {
            var info = MemoryCleanerService.GetRamInfo();

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

            var cpuPct = MemoryCleanerService.GetSystemCpuPercent();
            SysCpuPercent = cpuPct;
            SysCpuText = $"{cpuPct:F1}%";
            SysCpuColor = cpuPct >= 90 ? _brushRed
                          : cpuPct >= 75 ? _brushOrange
                          : cpuPct >= 50 ? _brushYellow
                          : _brushGreen;

            var guard = ServiceContainer.GetRequired<ResourceGuardService>();
            AppRamText = string.Format(L("RamCleaner_AppRam"), guard.AppWorkingSetMb);
            AppCpuText = string.Format(L("RamCleaner_AppCpu"), $"{guard.AppCpuPercent:F1}");

            PushHistory(info.PhysUsedPercent);
            DrawSparkline();

            if (_autoCleanEnabled && _autoCleanByThreshold && !_isCleaning)
            {
                if (info.PhysUsedPercent >= _autoCleanValue)
                    _ = SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);
            }
        }
        catch { }
    }

    private void RefreshGpu()
    {
        try
        {
            var gpu = MemoryCleanerService.GetGpuInfo();
            var name = gpu.Name.Length > 22 ? gpu.Name[..22] + "\u2026" : gpu.Name;
            string vram;
            if (gpu.AdapterRamMb > 0)
                vram = string.Format(L("RamCleaner_GpuVram"), gpu.AdapterRamMb);
            else if (gpu.SharedUsedMb > 0)
                vram = string.Format(L("RamCleaner_GpuShared"), gpu.SharedUsedMb);
            else
                vram = L("RamCleaner_GpuVramNA");
            var type = gpu.IsIntegrated ? L("RamCleaner_GpuIntegrated") : L("RamCleaner_GpuDedicated");

            Dispatcher.InvokeAsync(() =>
            {
                GpuNameShort = name;
                GpuVramText = vram;
                GpuTypeText = type;
            });
        }
        catch { }
    }

    private void RefreshProcesses()
    {
        try
        {
            var procs = MemoryCleanerService.GetTopProcessesByMemory(8);
            var totalMb = MemoryCleanerService.GetRamInfo().TotalPhysBytes / (1024.0 * 1024.0);
            TopProcesses.Clear();
            foreach (var p in procs)
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
        catch { }
    }

    private void PushHistory(double value)
    {
        _ramHistory[_historyHead] = value;
        _historyHead = (_historyHead + 1) % HistorySize;
        if (_historyCount < HistorySize) _historyCount++;
    }

    private static readonly Brush _sparklineBrush =
        FreezeBrush(new SolidColorBrush(Color.FromRgb(124, 58, 237)));

    private void DrawSparkline()
    {
        if (HistoryCanvas == null || HistoryCanvas.ActualWidth <= 0 || _historyCount < 2)
            return;

        if (_sparkline == null)
        {
            _sparkline = new Polyline
            {
                Stroke = _sparklineBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
            };
            HistoryCanvas.Children.Clear();
            HistoryCanvas.Children.Add(_sparkline);
        }

        double w = HistoryCanvas.ActualWidth;
        double h = HistoryCanvas.ActualHeight > 0 ? HistoryCanvas.ActualHeight : 70;
        int n = _historyCount;

        var points = new PointCollection(n);
        for (int i = 0; i < n; i++)
        {
            int idx = (_historyHead - n + i + HistorySize) % HistorySize;
            double x = i * (w / (n - 1));
            double y = (1.0 - _ramHistory[idx] / 100.0) * h;
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
        Dispatcher.InvokeAsync(() =>
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
        => Dispatcher.InvokeAsync(() => GuardStatusText = msg);

    private async void BtnClean_Click(object sender, RoutedEventArgs e)
        => await SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);

    private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
        => await SafeCleanAsync(MemoryCleanerService.CleanProfile.Quick);

    private async void BtnNormalClean_Click(object sender, RoutedEventArgs e)
        => await SafeCleanAsync(MemoryCleanerService.CleanProfile.Normal);

    private async void BtnDeepClean_Click(object sender, RoutedEventArgs e)
        => await SafeCleanAsync(MemoryCleanerService.CleanProfile.Deep);

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
        => RefreshProcesses();

    private async Task SafeCleanAsync(MemoryCleanerService.CleanProfile profile)
    {
        if (_isCleaning) return;

        IsCleaning = true;
        HasResult = false;
        CleaningStatus = "D\u00e9marrage du nettoyage...";

        try
        {
            var oldCts = Interlocked.Exchange(ref _cleanCts, new CancellationTokenSource());
            try { oldCts?.Cancel(); } catch { }
            try { oldCts?.Dispose(); } catch { }

            var ct = _cleanCts!.Token;

            CleaningStatus = "Lecture m\u00e9moire initiale...";
            var before = await Task.Run(MemoryCleanerService.GetRamInfo);

            long ws = 0, mfc = 0, sfc = 0, rc = 0, sb = 0, lp = 0, cp = 0, mp = 0;
            long dns = 0, gc = 0, sbf = 0, kt = 0, hc = 0, cb = 0, arp = 0, nb = 0, sess = 0, sf = 0;

            if (profile == MemoryCleanerService.CleanProfile.Deep || profile == MemoryCleanerService.CleanProfile.Quick
                || profile == MemoryCleanerService.CleanProfile.Normal)
            {
                CleaningStatus = "Vidage des Working Sets...";
                ws = await Task.Run(MemoryCleanerService.EmptyWorkingSets, ct);
            }

            if (profile == MemoryCleanerService.CleanProfile.Quick
                || profile == MemoryCleanerService.CleanProfile.Normal
                || profile == MemoryCleanerService.CleanProfile.Deep)
            {
                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Purge Standby (rapide)...";
                sbf = await Task.Run(MemoryCleanerService.FlushStandbyListFast, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Purge liste Standby...";
                sb = await Task.Run(MemoryCleanerService.FlushStandbyList, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache DNS...";
                dns = await Task.Run(MemoryCleanerService.FlushDnsCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = ".NET Garbage Collect...";
                gc = await Task.Run(MemoryCleanerService.ForceGcCollect, ct);
            }

            if (profile == MemoryCleanerService.CleanProfile.Normal
                || profile == MemoryCleanerService.CleanProfile.Deep)
            {
                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache fichiers modifi\u00e9s...";
                mfc = await Task.Run(MemoryCleanerService.FlushModifiedFileCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache fichier syst\u00e8me...";
                sfc = await Task.Run(MemoryCleanerService.ClearSystemFileCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache registre...";
                rc = await Task.Run(MemoryCleanerService.FlushRegistryCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Standby basse priorit\u00e9...";
                lp = await Task.Run(MemoryCleanerService.FlushLowPriorityStandbyList, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Pages combin\u00e9es...";
                cp = await Task.Run(MemoryCleanerService.FlushCombinedPageList, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Pages modifi\u00e9es...";
                mp = await Task.Run(MemoryCleanerService.FlushModifiedPageList, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Compactage heaps...";
                hc = await Task.Run(MemoryCleanerService.CompactAllHeaps, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Trim sessions...";
                sess = await Task.Run(MemoryCleanerService.TrimAllSessionsWorkingSets, ct);
            }

            if (profile == MemoryCleanerService.CleanProfile.Deep)
            {
                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Kernel Working Set...";
                kt = await Task.Run(MemoryCleanerService.TrimKernelWorkingSet, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Clipboard...";
                cb = await Task.Run(MemoryCleanerService.ClearClipboard, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache ARP...";
                arp = await Task.Run(MemoryCleanerService.FlushArpCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "Cache NetBIOS...";
                nb = await Task.Run(MemoryCleanerService.FlushNetBiosCache, ct);

                ct.ThrowIfCancellationRequested();
                CleaningStatus = "SuperFetch / SysMain...";
                try { sf = await MemoryCleanerService.FlushSuperFetchAsync(ct); } catch { }
            }

            long sumParts = ws + mfc + sfc + rc + sb + lp + cp + mp
                + dns + gc + sbf + kt + hc + cb + arp + nb + sess + sf;

            CleaningStatus = "Mesure finale...";
            await Task.Delay(500, CancellationToken.None);
            var after = await Task.Run(MemoryCleanerService.GetRamInfo);
            long globalDelta = Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
            long total = Math.Max(sumParts, globalDelta);

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
                ToastService.Show(L("RamCleaner_Freed") + MemoryCleanerService.FormatBytes(total));

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

    private void ChkAutoClean_Changed(object sender, RoutedEventArgs e)
    {
        Notify(nameof(AutoCleanStatusText));
        UpdateAutoCleanTimer();
    }

    private void CboAutoMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        _autoCleanByThreshold = CboAutoMode.SelectedIndex == 1;
        if (TxtAutoLabel != null)
            TxtAutoLabel.Text = _autoCleanByThreshold
                ? L("RamCleaner_ThresholdLabel")
                : L("RamCleaner_IntervalLabel");
        Notify(nameof(AutoCleanStatusText));
        UpdateAutoCleanTimer();
    }

    private void CboRefreshRate_Changed(object sender, SelectionChangedEventArgs e)
    {
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

    private void UpdateAutoCleanTimer()
    {
        _autoCleanTimer.Stop();
        if (!_autoCleanEnabled || _autoCleanByThreshold) return;
        _autoCleanTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, _autoCleanValue));
        _autoCleanTimer.Start();
    }

    private string BuildAutoStatusText()
    {
        if (!_autoCleanEnabled) return L("RamCleaner_AutoDisabled");
        if (_autoCleanByThreshold) return string.Format(L("RamCleaner_AutoByThreshold"), _autoCleanValue);
        return string.Format(L("RamCleaner_AutoByInterval"), _autoCleanValue);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
