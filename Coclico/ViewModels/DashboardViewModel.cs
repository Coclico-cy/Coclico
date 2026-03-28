#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Coclico.Services;

namespace Coclico.ViewModels;

public class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _procCounter;
    private CancellationTokenSource? _cts;

    private double _cpuUsage;
    public double CpuUsage
    {
        get => _cpuUsage;
        set { _cpuUsage = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuUsageText)); }
    }
    public string CpuUsageText => $"{CpuUsage:F0}%";

    private double _ramUsedGb;
    private double _ramTotalGb;
    public double RamUsedGb
    {
        get => _ramUsedGb;
        set { _ramUsedGb = value; OnPropertyChanged(); OnPropertyChanged(nameof(RamUsageText)); OnPropertyChanged(nameof(RamUsagePercent)); }
    }
    public double RamTotalGb
    {
        get => _ramTotalGb;
        set { _ramTotalGb = value; OnPropertyChanged(); OnPropertyChanged(nameof(RamUsageText)); OnPropertyChanged(nameof(RamUsagePercent)); }
    }
    public string RamUsageText =>
        $"{RamUsedGb.ToString("F1", CultureInfo.CurrentCulture)} GB / {RamTotalGb.ToString("F1", CultureInfo.CurrentCulture)} GB";
    public double RamUsagePercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100.0 : 0;

    private double _diskUsedGb;
    private double _diskTotalGb;
    public double DiskUsedGb
    {
        get => _diskUsedGb;
        set { _diskUsedGb = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskUsageText)); OnPropertyChanged(nameof(DiskFreeGb)); OnPropertyChanged(nameof(DiskUsagePercent)); }
    }
    public double DiskTotalGb
    {
        get => _diskTotalGb;
        set { _diskTotalGb = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskUsageText)); OnPropertyChanged(nameof(DiskFreeGb)); OnPropertyChanged(nameof(DiskUsagePercent)); }
    }
    public double DiskFreeGb => DiskTotalGb - DiskUsedGb;
    public string DiskUsageText =>
        $"{DiskUsedGb.ToString("F1", CultureInfo.CurrentCulture)} GB / {DiskTotalGb.ToString("F1", CultureInfo.CurrentCulture)} GB";
    public double DiskUsagePercent => DiskTotalGb > 0 ? (DiskUsedGb / DiskTotalGb) * 100.0 : 0;

    private string _uptimeText = "—";
    public string UptimeText
    {
        get => _uptimeText;
        set { _uptimeText = value; OnPropertyChanged(); }
    }

    private int _installedAppsCount;
    public int InstalledAppsCount
    {
        get => _installedAppsCount;
        set { _installedAppsCount = value; OnPropertyChanged(); }
    }

    private string _windowsDrive = "C:\\";
    public string WindowsDrive
    {
        get => _windowsDrive;
        set { _windowsDrive = value; OnPropertyChanged(); }
    }

    private int _processCount;
    public int ProcessCount
    {
        get => _processCount;
        set { _processCount = value; OnPropertyChanged(); }
    }

    private const int HistoryWindowSize = 100;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _ramHistory = new();

    public IEnumerable<double> CpuHistory => _cpuHistory.ToArray();
    public IEnumerable<double> RamHistory => _ramHistory.ToArray();

    public DashboardViewModel()
    {
        _cts = new CancellationTokenSource();

        WindowsDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

        Task.Run(() =>
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();

                _procCounter = new PerformanceCounter("System", "Processes");
                _procCounter.NextValue();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "DashboardViewModel.CounterInit");
            }
        });

        _ = RefreshAllAsync(_cts.Token);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync(_cts?.Token ?? CancellationToken.None);
        _refreshTimer.Start();
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        try
        {
            double cpu = 0, ramUsed = 0, ramTotal = 0, diskUsed = 0, diskTotal = 0;
            string uptime = "—";
            int procs = 0;

            await Task.Run(() =>
            {
                try { if (_cpuCounter != null) cpu = Math.Round(_cpuCounter.NextValue(), 1); }
                catch { cpu = 0; }

                try
                {
                    var info = MemoryCleanerService.GetRamInfo();
                    ramTotal = Math.Round(info.TotalPhysBytes / (1024.0 * 1024 * 1024), 1);
                    ramUsed = Math.Round(info.UsedPhysBytes / (1024.0 * 1024 * 1024), 1);
                }
                catch { }

                try
                {
                    var drive = new DriveInfo(WindowsDrive.TrimEnd('\\', '/'));
                    diskTotal = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 1);
                    diskUsed = Math.Round((drive.TotalSize - drive.TotalFreeSpace) / (1024.0 * 1024 * 1024), 1);
                }
                catch { }

                try
                {
                    var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    uptime = up.TotalDays >= 1
                        ? $"{(int)up.TotalDays}j {up.Hours:D2}h {up.Minutes:D2}m"
                        : $"{up.Hours:D2}h {up.Minutes:D2}m {up.Seconds:D2}s";
                }
                catch { uptime = "—"; }

                try { if (_procCounter != null) procs = (int)_procCounter.NextValue(); }
                catch { procs = 0; }
            }, ct);

            int apps = ServiceContainer.GetRequired<InstalledProgramsService>().GetMemoryCacheIconPaths().Count;

            PushHistory(cpu, ramUsed);

            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                () =>
                {
                    _cpuUsage = cpu;
                    _ramUsedGb = ramUsed;
                    _ramTotalGb = ramTotal;
                    _diskUsedGb = diskUsed;
                    _diskTotalGb = diskTotal;
                    _uptimeText = uptime;
                    _processCount = procs;
                    _installedAppsCount = apps;

                    var ev = PropertyChanged;
                    if (ev == null) return;
                    ev(this, new PropertyChangedEventArgs(nameof(CpuUsage)));
                    ev(this, new PropertyChangedEventArgs(nameof(CpuUsageText)));
                    ev(this, new PropertyChangedEventArgs(nameof(RamUsedGb)));
                    ev(this, new PropertyChangedEventArgs(nameof(RamTotalGb)));
                    ev(this, new PropertyChangedEventArgs(nameof(RamUsageText)));
                    ev(this, new PropertyChangedEventArgs(nameof(RamUsagePercent)));
                    ev(this, new PropertyChangedEventArgs(nameof(DiskUsedGb)));
                    ev(this, new PropertyChangedEventArgs(nameof(DiskTotalGb)));
                    ev(this, new PropertyChangedEventArgs(nameof(DiskUsageText)));
                    ev(this, new PropertyChangedEventArgs(nameof(DiskFreeGb)));
                    ev(this, new PropertyChangedEventArgs(nameof(DiskUsagePercent)));
                    ev(this, new PropertyChangedEventArgs(nameof(UptimeText)));
                    ev(this, new PropertyChangedEventArgs(nameof(ProcessCount)));
                    ev(this, new PropertyChangedEventArgs(nameof(InstalledAppsCount)));
                });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DashboardViewModel.RefreshAllAsync");
        }
    }

    public void StartRefresh()
    {
        _refreshTimer.Start();
        _cts ??= new CancellationTokenSource();
        _ = Task.Run(async () => await RefreshAllAsync(_cts.Token));
    }

    private void PushHistory(double cpuValue, double ramValue)
    {
        while (_cpuHistory.Count >= HistoryWindowSize)
            _cpuHistory.Dequeue();
        while (_ramHistory.Count >= HistoryWindowSize)
            _ramHistory.Dequeue();

        _cpuHistory.Enqueue(cpuValue);
        _ramHistory.Enqueue(ramValue);

        OnPropertyChanged(nameof(CpuHistory));
        OnPropertyChanged(nameof(RamHistory));
    }

    public void StopRefresh()
    {
        _refreshTimer.Stop();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _cpuCounter?.Dispose();
        _cpuCounter = null;
        _procCounter?.Dispose();
        _procCounter = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == true)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        else
            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Normal,
                () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }
}
