using Coclico.Services;
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

namespace Coclico.Views
{
    public sealed class ProcessItemVm
    {
        public string Name              { get; init; } = string.Empty;
        public string RamText           { get; init; } = string.Empty;
        public double WorkingSetPercent { get; init; }
    }

    public partial class RamCleanerView : UserControl, INotifyPropertyChanged
    {
        private readonly DispatcherTimer _monitorTimer   = new();
        private readonly DispatcherTimer _autoCleanTimer = new();
        private readonly DispatcherTimer _gpuTimer       = new();
        private CancellationTokenSource? _cleanCts;

        private bool   _isCleaning;
        private bool   _hasResult;
        private string _cleaningStatus  = string.Empty;
        private string _lastCleanedText = string.Empty;
        private bool   _notifEnabled    = true;
        private bool   _autoCleanEnabled;
        private int    _autoCleanValue  = 30;
        private bool   _autoCleanByThreshold;
        private long   _cumulativeFreedBytes;

        private string _resultWorkingSets       = "—";
        private string _resultStandby           = "—";
        private string _resultLowPriStandby     = "—";
        private string _resultModifiedPages     = "—";
        private string _resultCombinedPages     = "—";
        private string _resultModifiedFileCache = "—";
        private string _resultSystemFileCache   = "—";
        private string _resultRegistryCache     = "—";
        private string _resultDnsCache          = "—";
        private string _resultTotal             = "—";

        private string _physUsedText    = "—";
        private string _physAvailText   = "—";
        private string _physTotalText   = "—";
        private string _physPercent     = "—";
        private double _physUsedPercent;
        private string _virtUsedText    = "—";
        private string _virtAvailText   = "—";
        private string _virtTotalText   = "—";
        private string _virtPercent     = "—";
        private double _virtUsedPercent;
        private string _pageUsedText    = "—";
        private string _pageAvailText   = "—";
        private string _pageTotalText   = "—";

        private double _sysCpuPercent;
        private string _sysCpuText         = "—";
        private Brush  _sysCpuColor        = _brushGreen;
        private string _gpuNameShort       = string.Empty;
        private string _gpuVramText        = "—";
        private string _gpuTypeText        = "—";
        private string _appRamText         = "—";
        private string _appCpuText         = "—";
        private string _pressureText       = string.Empty;
        private Brush  _pressureColor      = _brushGreen;
        private Brush  _pressureBorderColor= _brushBorderNormal;
        private string _cumulativeFreedText= "0 B";
        private string _guardStatusText    = string.Empty;

        private const int HistorySize = 90;
        private readonly double[] _ramHistory = new double[HistorySize];
        private int      _historyHead;
        private int      _historyCount;
        private Polyline? _sparkline;

        private static readonly Brush _brushGreen          = FreezeBrush(new SolidColorBrush(Color.FromRgb(34,  197,  94)));
        private static readonly Brush _brushYellow         = FreezeBrush(new SolidColorBrush(Color.FromRgb(234, 179,   8)));
        private static readonly Brush _brushOrange         = FreezeBrush(new SolidColorBrush(Color.FromRgb(249, 115,  22)));
        private static readonly Brush _brushRed            = FreezeBrush(new SolidColorBrush(Color.FromRgb(239,  68,  68)));
        private static readonly Brush _brushBorderNormal   = FreezeBrush(new SolidColorBrush(Color.FromRgb( 26,  16,  48)));
        private static readonly Brush _brushBorderElevated = FreezeBrush(new SolidColorBrush(Color.FromRgb(113,  63,  18)));
        private static readonly Brush _brushBorderHigh     = FreezeBrush(new SolidColorBrush(Color.FromRgb(127,  29,  29)));
        private static readonly Brush _brushBorderCritical = FreezeBrush(new SolidColorBrush(Color.FromRgb(185,  28,  28)));
        private static Brush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

        public ObservableCollection<ProcessItemVm> TopProcesses { get; } = [];

        public bool   IsCleaning         { get => _isCleaning;          private set { _isCleaning = value;          Notify(); Notify(nameof(IsNotCleaning)); } }
        public bool   IsNotCleaning      => !_isCleaning;
        public bool   HasResult          { get => _hasResult;           private set { _hasResult = value;           Notify(); } }
        public string CleaningStatus     { get => _cleaningStatus;      private set { _cleaningStatus = value;      Notify(); } }
        public string LastCleanedText    { get => _lastCleanedText;     private set { _lastCleanedText = value;     Notify(); } }
        public string NotifButtonLabel   => _notifEnabled ? L("RamCleaner_NotifOn") : L("RamCleaner_NotifOff");
        public bool   AutoCleanEnabled   { get => _autoCleanEnabled;    set { _autoCleanEnabled = value;   Notify(); Notify(nameof(AutoCleanStatusText)); UpdateAutoCleanTimer(); } }
        public int    AutoCleanValue     { get => _autoCleanValue;      set { _autoCleanValue = Math.Max(1, value); Notify(); UpdateAutoCleanTimer(); } }
        public string AutoCleanStatusText => BuildAutoStatusText();

        public string ResultWorkingSets        { get => _resultWorkingSets;       private set { _resultWorkingSets = value;       Notify(); } }
        public string ResultStandby            { get => _resultStandby;           private set { _resultStandby = value;           Notify(); } }
        public string ResultLowPriorityStandby { get => _resultLowPriStandby;     private set { _resultLowPriStandby = value;     Notify(); } }
        public string ResultModifiedPages      { get => _resultModifiedPages;     private set { _resultModifiedPages = value;     Notify(); } }
        public string ResultCombinedPages      { get => _resultCombinedPages;     private set { _resultCombinedPages = value;     Notify(); } }
        public string ResultModifiedFileCache  { get => _resultModifiedFileCache; private set { _resultModifiedFileCache = value; Notify(); } }
        public string ResultSystemFileCache    { get => _resultSystemFileCache;   private set { _resultSystemFileCache = value;   Notify(); } }
        public string ResultRegistryCache      { get => _resultRegistryCache;     private set { _resultRegistryCache = value;     Notify(); } }
        public string ResultDnsCache           { get => _resultDnsCache;          private set { _resultDnsCache = value;          Notify(); } }
        public string ResultTotal              { get => _resultTotal;             private set { _resultTotal = value;             Notify(); } }

        public string PhysUsedText    { get => _physUsedText;    private set { _physUsedText    = value; Notify(); } }
        public string PhysAvailText   { get => _physAvailText;   private set { _physAvailText   = value; Notify(); } }
        public string PhysTotalText   { get => _physTotalText;   private set { _physTotalText   = value; Notify(); } }
        public string PhysPercent     { get => _physPercent;     private set { _physPercent     = value; Notify(); } }
        public double PhysUsedPercent { get => _physUsedPercent; private set { _physUsedPercent = value; Notify(); } }
        public string VirtUsedText    { get => _virtUsedText;    private set { _virtUsedText    = value; Notify(); } }
        public string VirtAvailText   { get => _virtAvailText;   private set { _virtAvailText   = value; Notify(); } }
        public string VirtTotalText   { get => _virtTotalText;   private set { _virtTotalText   = value; Notify(); } }
        public string VirtPercent     { get => _virtPercent;     private set { _virtPercent     = value; Notify(); } }
        public double VirtUsedPercent { get => _virtUsedPercent; private set { _virtUsedPercent = value; Notify(); } }
        public string PageUsedText    { get => _pageUsedText;    private set { _pageUsedText    = value; Notify(); } }
        public string PageAvailText   { get => _pageAvailText;   private set { _pageAvailText   = value; Notify(); } }
        public string PageTotalText   { get => _pageTotalText;   private set { _pageTotalText   = value; Notify(); } }

        public double SysCpuPercent       { get => _sysCpuPercent;        private set { _sysCpuPercent        = value; Notify(); } }
        public string SysCpuText          { get => _sysCpuText;           private set { _sysCpuText           = value; Notify(); } }
        public Brush  SysCpuColor         { get => _sysCpuColor;          private set { _sysCpuColor          = value; Notify(); } }
        public string GpuNameShort        { get => _gpuNameShort;         private set { _gpuNameShort         = value; Notify(); } }
        public string GpuVramText         { get => _gpuVramText;          private set { _gpuVramText          = value; Notify(); } }
        public string GpuTypeText         { get => _gpuTypeText;          private set { _gpuTypeText          = value; Notify(); } }
        public string AppRamText          { get => _appRamText;           private set { _appRamText           = value; Notify(); } }
        public string AppCpuText          { get => _appCpuText;           private set { _appCpuText           = value; Notify(); } }
        public string PressureText        { get => _pressureText;         private set { _pressureText         = value; Notify(); } }
        public Brush  PressureColor       { get => _pressureColor;        private set { _pressureColor        = value; Notify(); } }
        public Brush  PressureBorderColor { get => _pressureBorderColor;  private set { _pressureBorderColor  = value; Notify(); } }
        public string CumulativeFreedText { get => _cumulativeFreedText;  private set { _cumulativeFreedText  = value; Notify(); } }
        public string GuardStatusText     { get => _guardStatusText;      private set { _guardStatusText      = value; Notify(); } }

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
            _pressureText    = L("RamCleaner_Pressure_Normal");
            _guardStatusText = L("RamCleaner_GuardActive");
            _gpuNameShort    = L("RamCleaner_GpuDetecting");

            _monitorTimer.Interval = TimeSpan.FromSeconds(2);
            _monitorTimer.Tick    += (_, _) => RefreshStats();
            _monitorTimer.Start();

            _autoCleanTimer.Tick += async (_, _) => await RunCleanAsync();

            _gpuTimer.Interval = TimeSpan.FromSeconds(60);
            _gpuTimer.Tick    += (_, _) => Task.Run(RefreshGpu);
            _gpuTimer.Start();

            AppResourceGuardService.Instance.PressureChanged += OnPressureChanged;
            AppResourceGuardService.Instance.GuardMessage    += OnGuardMessage;

            RefreshStats();
            Task.Run(RefreshGpu);
            RefreshProcesses();
        }

        private void RefreshStats()
        {
            try
            {
                var info = MemoryCleanerService.GetRamInfo();

                PhysUsedText    = MemoryCleanerService.FormatBytes(info.UsedPhysBytes);
                PhysAvailText   = MemoryCleanerService.FormatBytes(info.AvailPhysBytes);
                PhysTotalText   = MemoryCleanerService.FormatBytes(info.TotalPhysBytes);
                PhysPercent     = $"{info.PhysUsedPercent:F1}%";
                PhysUsedPercent = info.PhysUsedPercent;

                VirtUsedText    = MemoryCleanerService.FormatBytes(info.UsedVirtBytes);
                VirtAvailText   = MemoryCleanerService.FormatBytes(info.AvailVirtBytes);
                VirtTotalText   = MemoryCleanerService.FormatBytes(info.TotalVirtBytes);
                VirtPercent     = $"{info.VirtUsedPercent:F1}%";
                VirtUsedPercent = info.VirtUsedPercent;

                long pfCapacity = Math.Max(0, info.TotalPageBytes - info.TotalPhysBytes);
                long pfUsed     = Math.Max(0, info.UsedVirtBytes  - info.UsedPhysBytes);
                long pfAvail    = Math.Max(0, pfCapacity - pfUsed);
                PageUsedText    = MemoryCleanerService.FormatBytes(pfUsed);
                PageAvailText   = MemoryCleanerService.FormatBytes(pfAvail);
                PageTotalText   = MemoryCleanerService.FormatBytes(pfCapacity);

                var cpuPct    = MemoryCleanerService.GetSystemCpuPercent();
                SysCpuPercent = cpuPct;
                SysCpuText    = $"{cpuPct:F1}%";
                SysCpuColor   = cpuPct >= 90 ? _brushRed
                              : cpuPct >= 75 ? _brushOrange
                              : cpuPct >= 50 ? _brushYellow
                              :                _brushGreen;

                var guard  = AppResourceGuardService.Instance;
                AppRamText = string.Format(L("RamCleaner_AppRam"), guard.AppWorkingSetMb);
                AppCpuText = string.Format(L("RamCleaner_AppCpu"), $"{guard.AppCpuPercent:F1}");

                PushHistory(info.PhysUsedPercent);
                DrawSparkline();

                if (_autoCleanEnabled && _autoCleanByThreshold && !_isCleaning)
                {
                    if (info.PhysUsedPercent >= _autoCleanValue)
                        _ = RunCleanAsync();
                }
            }
            catch { }
        }

        private void RefreshGpu()
        {
            try
            {
                var gpu  = MemoryCleanerService.GetGpuInfo();
                var name = gpu.Name.Length > 22 ? gpu.Name[..22] + "…" : gpu.Name;
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
                    GpuVramText  = vram;
                    GpuTypeText  = type;
                });
            }
            catch { }
        }

        private void RefreshProcesses()
        {
            try
            {
                var procs   = MemoryCleanerService.GetTopProcessesByMemory(8);
                var totalMb = MemoryCleanerService.GetRamInfo().TotalPhysBytes / (1024.0 * 1024.0);
                TopProcesses.Clear();
                foreach (var p in procs)
                {
                    TopProcesses.Add(new ProcessItemVm
                    {
                        Name              = p.Name,
                        RamText           = $"{p.WorkingSetMb} MB",
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
                    Stroke          = _sparklineBrush,
                    StrokeThickness = 1.5,
                    StrokeLineJoin  = PenLineJoin.Round,
                };
                HistoryCanvas.Children.Clear();
                HistoryCanvas.Children.Add(_sparkline);
            }

            double w = HistoryCanvas.ActualWidth;
            double h = HistoryCanvas.ActualHeight > 0 ? HistoryCanvas.ActualHeight : 70;
            int    n = _historyCount;

            var points = new PointCollection(n);
            for (int i = 0; i < n; i++)
            {
                int    idx = (_historyHead - n + i + HistorySize) % HistorySize;
                double x   = i * (w / (n - 1));
                double y   = (1.0 - _ramHistory[idx] / 100.0) * h;
                points.Add(new Point(x, y));
            }
            _sparkline.Points = points;
        }

        private void HistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _sparkline = null;
            DrawSparkline();
        }

        private void OnPressureChanged(AppResourceGuardService.PressureLevel level)
        {
            Dispatcher.InvokeAsync(() =>
            {
                (PressureText, PressureColor, PressureBorderColor) = level switch
                {
                    AppResourceGuardService.PressureLevel.Critical =>
                        (L("RamCleaner_Pressure_Critical"), _brushRed,    _brushBorderCritical),
                    AppResourceGuardService.PressureLevel.High =>
                        (L("RamCleaner_Pressure_High"),     _brushOrange, _brushBorderHigh),
                    AppResourceGuardService.PressureLevel.Elevated =>
                        (L("RamCleaner_Pressure_Elevated"), _brushYellow, _brushBorderElevated),
                    _ =>
                        (L("RamCleaner_Pressure_Normal"),   _brushGreen,  _brushBorderNormal),
                };
            });
        }

        private void OnGuardMessage(string msg)
            => Dispatcher.InvokeAsync(() => GuardStatusText = msg);

        private async void BtnClean_Click(object sender, RoutedEventArgs e)
            => await RunCleanAsync();

        private async void BtnQuickClean_Click(object sender, RoutedEventArgs e)
            => await RunProfileCleanAsync(MemoryCleanerService.CleanProfile.Quick);

        private async void BtnNormalClean_Click(object sender, RoutedEventArgs e)
            => await RunProfileCleanAsync(MemoryCleanerService.CleanProfile.Normal);

        private async void BtnDeepClean_Click(object sender, RoutedEventArgs e)
            => await RunProfileCleanAsync(MemoryCleanerService.CleanProfile.Deep);

        private async void BtnTrimSelf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(MemoryCleanerService.TrimSelfWorkingSet);
                GuardStatusText = L("RamCleaner_WorkingSetFreed") + DateTime.Now.ToString("HH:mm:ss");
            }
            catch { }
        }

        private void BtnRefreshProc_Click(object sender, RoutedEventArgs e)
            => RefreshProcesses();

        private async Task RunProfileCleanAsync(MemoryCleanerService.CleanProfile profile)
        {
            if (IsCleaning) return;
            IsCleaning = true;
            HasResult  = false;
            _cleanCts?.Cancel();
            _cleanCts?.Dispose();
            _cleanCts = new CancellationTokenSource();

            try
            {
                var ct       = _cleanCts.Token;
                var progress = new Progress<string>(msg => CleaningStatus = msg);
                var result   = await MemoryCleanerService.CleanByProfileAsync(profile, progress, ct);

                _cumulativeFreedBytes += result.TotalFreed;
                CumulativeFreedText    = MemoryCleanerService.FormatBytes(_cumulativeFreedBytes);
                ResultTotal            = MemoryCleanerService.FormatBytes(result.TotalFreed);
                ResultWorkingSets      = MemoryCleanerService.FormatBytes(result.WorkingSetsFreed);
                ResultStandby          = MemoryCleanerService.FormatBytes(result.StandbyFreed);
                ResultModifiedFileCache= MemoryCleanerService.FormatBytes(result.ModifiedFileCacheFreed);
                LastCleanedText        = L("RamCleaner_LastCleaned") + DateTime.Now.ToString("HH:mm:ss");
                HasResult              = true;
                CleaningStatus         = L("RamCleaner_Done");

                if (_notifEnabled)
                    ToastService.Show(L("RamCleaner_Freed") + MemoryCleanerService.FormatBytes(result.TotalFreed));

                RefreshStats();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { CleaningStatus = L("RamCleaner_Error") + ex.Message; }
            finally { IsCleaning = false; }
        }

        private async Task RunCleanAsync()
        {
            if (IsCleaning) return;
            IsCleaning = true;
            HasResult  = false;
            _cleanCts?.Cancel();
            _cleanCts?.Dispose();
            _cleanCts = new CancellationTokenSource();

            try
            {
                var ct     = _cleanCts.Token;
                var before = MemoryCleanerService.GetRamInfo();
                long ws = 0, sb = 0, lp = 0, mp = 0, cp = 0, mfc = 0, sfc = 0, rc = 0, dns = 0, gc = 0;

                if (ChkWorkingSets.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepWorkingSets");
                    ws = await Task.Run(MemoryCleanerService.EmptyWorkingSets, ct);
                }
                if (ChkModifiedFileCache.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepModifiedFileCache");
                    mfc = await Task.Run(MemoryCleanerService.FlushModifiedFileCache, ct);
                }
                if (ChkSystemFileCache.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepSystemFileCache");
                    sfc = await Task.Run(MemoryCleanerService.ClearSystemFileCache, ct);
                }
                if (ChkRegistryCache.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepRegistry");
                    rc = await Task.Run(MemoryCleanerService.FlushRegistryCache, ct);
                }
                if (ChkStandby.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepStandby");
                    sb = await Task.Run(MemoryCleanerService.FlushStandbyList, ct);
                }
                if (ChkLowPriorityStandby.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepLowPriStandby");
                    lp = await Task.Run(MemoryCleanerService.FlushLowPriorityStandbyList, ct);
                }
                if (ChkCombinedPages.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepCombined");
                    cp = await Task.Run(MemoryCleanerService.FlushCombinedPageList, ct);
                }
                if (ChkModifiedPages.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepModifiedPages");
                    mp = await Task.Run(MemoryCleanerService.FlushModifiedPageList, ct);
                }
                if (ChkDnsCache.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepDns");
                    dns = await Task.Run(MemoryCleanerService.FlushDnsCache, ct);
                }
                if (ChkGcCollect.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepGc");
                    gc = await Task.Run(MemoryCleanerService.ForceGcCollect, ct);
                }
                if (ChkStandbyFast.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepStandbyFast");
                    ws += await Task.Run(MemoryCleanerService.FlushStandbyListFast, ct);
                }
                if (ChkKernelTrim.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepKernel");
                    ws += await Task.Run(MemoryCleanerService.TrimKernelWorkingSet, ct);
                }
                if (ChkArpCache.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepArp");
                    dns += await Task.Run(MemoryCleanerService.FlushArpCache, ct);
                }
                if (ChkNetBios.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepNetBios");
                    dns += await Task.Run(MemoryCleanerService.FlushNetBiosCache, ct);
                }
                if (ChkAllSessions.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepAllSessions");
                    ws += await Task.Run(MemoryCleanerService.TrimAllSessionsWorkingSets, ct);
                }
                if (ChkClipboard.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepClipboard");
                    gc += await Task.Run(MemoryCleanerService.ClearClipboard, ct);
                }
                if (ChkCompactHeaps.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepHeaps");
                    gc += await Task.Run(MemoryCleanerService.CompactAllHeaps, ct);
                }
                if (ChkSuperFetch.IsChecked == true)
                {
                    CleaningStatus = L("RamCleaner_StepSuperFetch");
                    gc += await MemoryCleanerService.FlushSuperFetchAsync(ct);
                }

                await Task.Delay(600, ct);
                var after = MemoryCleanerService.GetRamInfo();
                long total = Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);

                ResultWorkingSets        = MemoryCleanerService.FormatBytes(ws);
                ResultStandby            = MemoryCleanerService.FormatBytes(sb);
                ResultLowPriorityStandby = MemoryCleanerService.FormatBytes(lp);
                ResultModifiedPages      = MemoryCleanerService.FormatBytes(mp);
                ResultCombinedPages      = MemoryCleanerService.FormatBytes(cp);
                ResultModifiedFileCache  = MemoryCleanerService.FormatBytes(mfc);
                ResultSystemFileCache    = MemoryCleanerService.FormatBytes(sfc);
                ResultRegistryCache      = MemoryCleanerService.FormatBytes(rc);
                ResultDnsCache           = MemoryCleanerService.FormatBytes(dns);
                ResultTotal              = MemoryCleanerService.FormatBytes(total);
                LastCleanedText          = L("RamCleaner_LastCleaned") + DateTime.Now.ToString("HH:mm:ss");
                HasResult                = true;
                CleaningStatus           = L("RamCleaner_Done");

                _cumulativeFreedBytes   += total;
                CumulativeFreedText      = MemoryCleanerService.FormatBytes(_cumulativeFreedBytes);

                if (_notifEnabled)
                    ToastService.Show(L("RamCleaner_Freed") + MemoryCleanerService.FormatBytes(total));

                RefreshStats();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { CleaningStatus = L("RamCleaner_Error") + ex.Message; }
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
}
