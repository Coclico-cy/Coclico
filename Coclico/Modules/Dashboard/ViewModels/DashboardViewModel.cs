using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Coclico.Models;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public class ActivityEntry
{
    public string Description { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public Brush AccentColor { get; init; } = Brushes.Gray;
}

public partial class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private const int HistoryWindowSize = 100;

    private readonly DispatcherTimer _refreshTimer;
    private readonly object _historyLock = new();
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _ramHistory = new();
    private readonly CustomShortcutsService _shortcutsService =
        ServiceContainer.GetOptional<CustomShortcutsService>() ?? new CustomShortcutsService();

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _procCounter;
    private CancellationTokenSource? _cts;
    private volatile bool _isRefreshing;

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
    public double RamUsagePercent => RamTotalGb > 0 ? RamUsedGb / RamTotalGb * 100.0 : 0;

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
    public double DiskUsagePercent => DiskTotalGb > 0 ? DiskUsedGb / DiskTotalGb * 100.0 : 0;

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

    public string WindowsDrive
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "C:\\";

    private int _processCount;
    public int ProcessCount
    {
        get => _processCount;
        set { _processCount = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ActivityEntry> RecentActivities { get; } = [];
    public bool HasNoActivities => RecentActivities.Count == 0;

    public IEnumerable<double> CpuHistory { get { lock (_historyLock) { return _cpuHistory.ToArray(); } } }
    public IEnumerable<double> RamHistory { get { lock (_historyLock) { return _ramHistory.ToArray(); } } }

    public HomeCustomizationService Customization { get; } =
        ServiceContainer.GetOptional<HomeCustomizationService>() ?? new HomeCustomizationService();

    public HomeCustomizationSettings CustomizationSettings => Customization.Settings;

    public CornerRadius TileCornerRadius => Customization.TileCornerRadius;
    public CornerRadius CardCornerRadius => Customization.CardCornerRadius;
    public CornerRadius IconCornerRadius => Customization.IconCornerRadius;
    public int GridColumns => Customization.GridColumns;
    public Thickness TilePadding => Customization.TilePadding;
    public double TileIconBoxSize => Customization.TileIconBoxSize;
    public double TileIconSymbolSize => Customization.TileIconSymbolSize;
    public double TileTitleFontSize => Customization.TileTitleFontSize;
    public double TileDescFontSize => Customization.TileDescFontSize;

    public bool ShowHeader => Customization.Settings.ShowHeader;
    public bool ShowMetrics => Customization.Settings.ShowMetrics;
    public bool ShowQuickModes => Customization.Settings.ShowQuickModes;
    public bool ShowCoreTiles => Customization.Settings.ShowCoreTiles;
    public bool ShowCustomShortcuts => Customization.Settings.ShowCustomShortcuts;
    public bool ShowRecentActivity => Customization.Settings.ShowRecentActivity;

    public bool ShowTileRam => Customization.Settings.ShowTileRam;
    public bool ShowTileCleaning => Customization.Settings.ShowTileCleaning;
    public bool ShowTilePrograms => Customization.Settings.ShowTilePrograms;
    public bool ShowTileInstaller => Customization.Settings.ShowTileInstaller;
    public bool ShowTileHealth => Customization.Settings.ShowTileHealth;
    public bool ShowTileNetwork => Customization.Settings.ShowTileNetwork;

    public ObservableCollection<CustomShortcut> CustomShortcuts => _shortcutsService.Shortcuts;
    public bool HasNoShortcuts => CustomShortcuts.Count == 0;

    public Action<string>? NavigationRequested { get; set; }

    public DashboardViewModel()
    {
        _cts = new CancellationTokenSource();

        WindowsDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

        _ = Task.Run(async () =>
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ = _cpuCounter.NextValue();

                _procCounter = new PerformanceCounter("System", "Processes");
                _ = _procCounter.NextValue();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "DashboardViewModel.CounterInit");
            }

            await RefreshAllAsync(_cts.Token).ConfigureAwait(false);
        });

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                await RefreshAllAsync(_cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                _isRefreshing = false;
            }
        };
        _refreshTimer.Start();

        Customization.CustomizationChanged += OnCustomizationChanged;
    }

    public void AddActivity(string description, string hexColor = "#818CF8")
    {
        _ = (Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            var brush = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
            while (RecentActivities.Count >= 5)
            {
                RecentActivities.RemoveAt(RecentActivities.Count - 1);
            }

            RecentActivities.Insert(0, new ActivityEntry
            {
                Description = description,
                TimeText = DateTime.Now.ToString("HH:mm"),
                AccentColor = brush
            });
            OnPropertyChanged(nameof(HasNoActivities));
        }));
    }

    private void OnCustomizationChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Customization));
        OnPropertyChanged(nameof(TileCornerRadius));
        OnPropertyChanged(nameof(CardCornerRadius));
        OnPropertyChanged(nameof(IconCornerRadius));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(TilePadding));
        OnPropertyChanged(nameof(TileIconBoxSize));
        OnPropertyChanged(nameof(TileIconSymbolSize));
        OnPropertyChanged(nameof(TileTitleFontSize));
        OnPropertyChanged(nameof(TileDescFontSize));
        OnPropertyChanged(nameof(ShowHeader));
        OnPropertyChanged(nameof(ShowMetrics));
        OnPropertyChanged(nameof(ShowQuickModes));
        OnPropertyChanged(nameof(ShowCoreTiles));
        OnPropertyChanged(nameof(ShowCustomShortcuts));
        OnPropertyChanged(nameof(ShowRecentActivity));
        OnPropertyChanged(nameof(ShowTileRam));
        OnPropertyChanged(nameof(ShowTileCleaning));
        OnPropertyChanged(nameof(ShowTilePrograms));
        OnPropertyChanged(nameof(ShowTileInstaller));
        OnPropertyChanged(nameof(ShowTileHealth));
        OnPropertyChanged(nameof(ShowTileNetwork));
        OnPropertyChanged(nameof(CustomizationSettings));
    }

    [RelayCommand]
    private async Task RunModeZenAsync()
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Quick, cleanTemp: true); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardViewModel.ModeZen"); }
    }

    [RelayCommand]
    private async Task RunModeGamerAsync()
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Deep, cleanTemp: false); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardViewModel.ModeGamer"); }
    }

    [RelayCommand]
    private async Task RunModeWorkAsync()
    {
        try { await RunModeAsync(MemoryCleanerService.CleanProfile.Normal, cleanTemp: false); }
        catch (Exception ex) { LoggingService.LogException(ex, "DashboardViewModel.ModeWork"); }
    }

    private async Task RunModeAsync(MemoryCleanerService.CleanProfile profile, bool cleanTemp)
    {
        try
        {
            _ = await MemoryCleanerService.CleanByProfileAsync(profile);

            if (cleanTemp)
            {
                string temp = Path.GetTempPath();
                await Task.Run(() =>
                {
                    foreach (string f in Directory.EnumerateFiles(temp, "*",
                        SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(f); } catch (Exception ex) { LoggingService.LogException(ex, "DashboardViewModel.RunModeAsync"); }
                    }
                });
            }

            string key = Application.Current.Resources["Dashboard_ModeActivated"] as string ?? "Mode activ\u00e9 !";
            ToastService.Show("\u2713 " + key);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DashboardViewModel.RunModeAsync");
        }
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
                try
                {
                    if (_cpuCounter != null)
                    {
                        cpu = Math.Round(_cpuCounter.NextValue(), 1);
                    }
                }
                catch (Exception ex) { cpu = 0; LoggingService.LogException(ex, "DashboardVM.CpuSample"); }

                try
                {
                    MemoryCleanerService.RamInfo info = MemoryCleanerService.GetRamInfo();
                    ramTotal = Math.Round(info.TotalPhysBytes / (1024.0 * 1024 * 1024), 1);
                    ramUsed = Math.Round(info.UsedPhysBytes / (1024.0 * 1024 * 1024), 1);
                }
                catch (Exception ex) { LoggingService.LogException(ex, "DashboardVM.RamSample"); }

                try
                {
                    var drive = new DriveInfo(WindowsDrive.TrimEnd('\\', '/'));
                    diskTotal = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 1);
                    diskUsed = Math.Round((drive.TotalSize - drive.TotalFreeSpace) / (1024.0 * 1024 * 1024), 1);
                }
                catch (Exception ex) { LoggingService.LogException(ex, "DashboardVM.DiskSample"); }

                try
                {
                    var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    uptime = up.TotalDays >= 1
                        ? $"{(int)up.TotalDays}j {up.Hours:D2}h {up.Minutes:D2}m"
                        : $"{up.Hours:D2}h {up.Minutes:D2}m {up.Seconds:D2}s";
                }
                catch { uptime = "—"; }

                try
                {
                    if (_procCounter != null)
                    {
                        procs = (int)_procCounter.NextValue();
                    }
                }
                catch { procs = 0; }
            }, ct);

            int apps = ServiceContainer.GetRequired<InstalledProgramsService>().GetMemoryCacheIconPaths().Count;

            PushHistory(cpu, ramUsed);

            _ = (Application.Current?.Dispatcher.BeginInvoke(
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

                    PropertyChangedEventHandler? ev = PropertyChanged;
                    if (ev == null)
                    {
                        return;
                    }

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
                }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DashboardViewModel.RefreshAllAsync");
        }
    }

    private void PushHistory(double cpuValue, double ramValue)
    {
        lock (_historyLock)
        {
            while (_cpuHistory.Count >= HistoryWindowSize)
            {
                _ = _cpuHistory.Dequeue();
            }

            while (_ramHistory.Count >= HistoryWindowSize)
            {
                _ = _ramHistory.Dequeue();
            }

            _cpuHistory.Enqueue(cpuValue);
            _ramHistory.Enqueue(ramValue);
        }

        OnPropertyChanged(nameof(CpuHistory));
        OnPropertyChanged(nameof(RamHistory));
    }

    public void StartRefresh()
    {
        _refreshTimer.Start();
        _cts ??= new CancellationTokenSource();
        _ = Task.Run(() => RefreshAllAsync(_cts.Token));
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
        Customization.CustomizationChanged -= OnCustomizationChanged;
    }

    [RelayCommand]
    private async Task ExecuteShortcutAsync(CustomShortcut? shortcut)
    {
        if (shortcut == null)
        {
            return;
        }

        await _shortcutsService.ExecuteShortcutAsync(shortcut, tag => NavigationRequested?.Invoke(tag));
    }

    [RelayCommand]
    private void RemoveShortcut(CustomShortcut? shortcut)
    {
        if (shortcut == null)
        {
            return;
        }

        _shortcutsService.RemoveShortcut(shortcut.Id);
        OnPropertyChanged(nameof(HasNoShortcuts));
    }

    public void AddShortcut(CustomShortcut shortcut)
    {
        _shortcutsService.AddShortcut(shortcut);
        OnPropertyChanged(nameof(HasNoShortcuts));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        else
        {
            _ = (Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Normal,
                () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name))));
        }
    }
}