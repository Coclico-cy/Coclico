using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Coclico.Modules.DiskAnalyzer.Services;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public sealed class DriveRow : INotifyPropertyChanged
{
    private Brush _cardBrush = Brushes.Transparent;

    public required string Label { get; init; }
    public required string SizeText { get; init; }
    public required string UsageText { get; init; }
    public required double Percent { get; init; }
    public required double UsedBarWidth { get; init; }
    public required Brush BarBrush { get; init; }
    public required string RootPath { get; init; }

    public Brush CardBrush
    {
        get => _cardBrush;
        set
        {
            if (ReferenceEquals(_cardBrush, value))
            {
                return;
            }

            _cardBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBrush)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ZoneRow
{
    public required string Label { get; init; }
    public required string SizeText { get; init; }
    public required string PercentText { get; init; }
    public required double Percent { get; init; }
    public required Brush ZoneBrush { get; init; }
    public required DiskItemNode? Node { get; init; }
}

public sealed class Crumb
{
    public required string Label { get; init; }
    public required DiskItemNode Node { get; init; }
}

public sealed class DuplicateFileRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required string Folder { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DuplicateGroupRow
{
    public required string FileName { get; init; }
    public required string CountText { get; init; }
    public required string WastedText { get; init; }
    public required IReadOnlyList<DuplicateFileRow> Files { get; init; }
}

public sealed class LargeFileRow
{
    public required string FileName { get; init; }
    public required string Path { get; init; }
    public required string SizeText { get; init; }
}

public sealed record Zone(
    string Name,
    string FullName,
    long Size,
    long Files,
    IReadOnlyList<Zone> Children,
    DiskItemNode? Node);

public enum SunburstRender
{
    Clear,
    LiveFadeIn,
    RestoreOpacity,
    Empty,
    Full,
    Animated
}

public partial class DiskAnalyzerViewModel : INotifyPropertyChanged
{
    private const int MaxZonesPerRing = 30;
    private const double MinSegmentAngle = 1.0;
    private const int RingCount = 3;
    private const int LiveRenderIntervalMs = 250;
    private const int DuplicateGroupsShown = 100;

    public static readonly Color[] Palette =
    [
        Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0xD9, 0x46, 0xEF), Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0xF4, 0x3F, 0x5E), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x84, 0xCC, 0x16), Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0x10, 0xB9, 0x81),
        Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x0E, 0xA5, 0xE9),
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x63, 0x66, 0xF1)
    ];

    private DiskItemNode? _root;
    private DiskItemNode? _current;
    private readonly Stack<DiskItemNode> _history = new();
    private Zone? _liveRoot;
    private long _driveFreeBytes;
    private string? _selectedDriveRoot;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _duplicatesCts;
    private CancellationTokenSource? _largeCts;
    private bool _liveRenderShown;
    private DateTime _lastLiveRender = DateTime.MinValue;
    private List<DriveRow> _driveRows = [];
    private List<DuplicateGroupRow> _duplicateGroups = [];
    private List<LargeFileRow> _largeFiles = [];

    public Func<string, string, bool>? Confirm { get; set; }
    public Func<string, string?>? BrowseFolder { get; set; }

    public event Action<SunburstRender>? SunburstRenderRequested;
    public event Action<bool>? NavigationAnimationRequested;
    public event Action<string>? ModePanelAnimationRequested;

    public ObservableCollection<DriveRow> Drives { get; } = [];

    public bool IsScanning { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }

    public string ScanStatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = L("Disk_SelectDrive");

    public string CenterTitleText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "Coclico";
    public string CenterSizeText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public string CenterHintText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public string ZonesTitleText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = L("Disk_Zones");
    public string ZonesSubTitleText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public IReadOnlyList<ZoneRow>? Zones
    {
        get;
        private set { field = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<Crumb>? BreadcrumbItems
    {
        get;
        private set { field = value; OnPropertyChanged(); }
    }

    public string SelectedNameText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = "---";
    public string SelectedDetailsText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public bool CanOpenExplorer { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } }

    public IReadOnlyList<DuplicateGroupRow>? DuplicatesGroups
    {
        get;
        private set { field = value; OnPropertyChanged(); }
    }

    public string DuplicatesRoot
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public int DuplicatesMinSizeIndex
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public bool IsDuplicatesScanning { get; private set { if (field == value) return; field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotDuplicatesScanning)); } }
    public bool IsNotDuplicatesScanning => !IsDuplicatesScanning;
    public string DuplicatesSummaryText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;
    public string DuplicatesStatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public string LargeRoot
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public int LargeTopCountIndex
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = 1;

    public IReadOnlyList<LargeFileRow>? LargeFiles
    {
        get;
        private set { field = value; OnPropertyChanged(); }
    }

    public bool IsLargeScanning { get; private set { if (field == value) return; field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotLargeScanning)); } }
    public bool IsNotLargeScanning => !IsLargeScanning;
    public string LargeStatusText { get; private set { if (field == value) return; field = value; OnPropertyChanged(); } } = string.Empty;

    public string CurrentMode
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSpaceMode));
            OnPropertyChanged(nameof(IsDuplicatesMode));
            OnPropertyChanged(nameof(IsLargeFilesMode));
            OnPropertyChanged(nameof(SpaceModeBg));
            OnPropertyChanged(nameof(SpaceModeFg));
            OnPropertyChanged(nameof(DuplicatesModeBg));
            OnPropertyChanged(nameof(DuplicatesModeFg));
            OnPropertyChanged(nameof(LargeFilesModeBg));
            OnPropertyChanged(nameof(LargeFilesModeFg));
        }
    } = "Space";

    public bool IsSpaceMode => CurrentMode == "Space";
    public bool IsDuplicatesMode => CurrentMode == "Duplicates";
    public bool IsLargeFilesMode => CurrentMode == "LargeFiles";

    private static Brush ResourceBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    public Brush SpaceModeBg => IsSpaceMode ? ResourceBrush("PrimaryBrush", Brushes.Transparent) : Brushes.Transparent;
    public Brush SpaceModeFg => IsSpaceMode ? Brushes.White : ResourceBrush("TextSecondaryBrush", Brushes.White);
    public Brush DuplicatesModeBg => IsDuplicatesMode ? ResourceBrush("PrimaryBrush", Brushes.Transparent) : Brushes.Transparent;
    public Brush DuplicatesModeFg => IsDuplicatesMode ? Brushes.White : ResourceBrush("TextSecondaryBrush", Brushes.White);
    public Brush LargeFilesModeBg => IsLargeFilesMode ? ResourceBrush("PrimaryBrush", Brushes.Transparent) : Brushes.Transparent;
    public Brush LargeFilesModeFg => IsLargeFilesMode ? Brushes.White : ResourceBrush("TextSecondaryBrush", Brushes.White);

    public Zone? CurrentZone => IsScanning ? _liveRoot : (_current != null ? BuildZone(_current, 0) : null);
    public bool IsCurrentRoot => _root != null && ReferenceEquals(_current, _root);
    public long DriveFreeBytes => _driveFreeBytes;

    private static string L(string key)
    {
        return Application.Current?.TryFindResource(key) as string
            ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key)
            ?? key;
    }

    public static Color ShadeColor(Color baseColor, int depth)
    {
        if (depth <= 0)
        {
            return baseColor;
        }

        double factor = 1.0 + (0.16 * depth);
        return Color.FromRgb(
            (byte)Math.Clamp(baseColor.R * factor, 0, 255),
            (byte)Math.Clamp(baseColor.G * factor, 0, 255),
            (byte)Math.Clamp(baseColor.B * factor, 0, 255));
    }

    public void Initialize()
    {
        LoadDrives();
        string root = _selectedDriveRoot
            ?? DiskAnalyzerService.GetReadyDrives().FirstOrDefault()?.RootDirectory.FullName
            ?? "C:\\";
        if (string.IsNullOrWhiteSpace(DuplicatesRoot))
        {
            DuplicatesRoot = root;
        }

        if (string.IsNullOrWhiteSpace(LargeRoot))
        {
            LargeRoot = root;
        }

        SetMode("Space");
    }

    public void Detach()
    {
        CancelScanCore();
        _duplicatesCts?.Cancel();
        _largeCts?.Cancel();
    }

    private void LoadDrives()
    {
        var rows = new List<DriveRow>();
        foreach (DriveInfo drive in DiskAnalyzerService.GetReadyDrives())
        {
            double totalGb = drive.TotalSize / 1073741824.0;
            double freeGb = drive.AvailableFreeSpace / 1073741824.0;
            double usedPct = drive.TotalSize > 0 ? (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize : 0;

            rows.Add(new DriveRow
            {
                Label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.VolumeLabel} ({drive.Name[..1]})",
                SizeText = $"{totalGb - freeGb:0.#} / {totalGb:0.#} Go",
                UsageText = $"{usedPct:0}% utilisés · {freeGb:0.#} Go libres",
                Percent = Math.Min(100, usedPct),
                UsedBarWidth = Math.Max(2, usedPct * 1.2),
                BarBrush = new SolidColorBrush(usedPct > 88 ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0xF5, 0x9E, 0x0B)),
                RootPath = drive.RootDirectory.FullName
            });
        }

        _driveRows = rows;
        Drives.Clear();
        foreach (DriveRow row in rows)
        {
            Drives.Add(row);
        }

        if (rows.Count > 0 && string.IsNullOrWhiteSpace(_selectedDriveRoot))
        {
            _selectedDriveRoot = rows[0].RootPath;
        }

        HighlightSelectedDrive();
    }

    private void HighlightSelectedDrive()
    {
        Brush selected = ResourceBrush("PrimaryBrush", Brushes.Transparent);
        Brush idle = ResourceBrush("BorderSubtleBrush", Brushes.Transparent);
        foreach (DriveRow row in _driveRows)
        {
            row.CardBrush = string.Equals(row.RootPath, _selectedDriveRoot, StringComparison.OrdinalIgnoreCase)
                ? selected
                : idle;
        }
    }

    [RelayCommand]
    private void SelectDrive(DriveRow? row)
    {
        if (row != null)
        {
            _selectedDriveRoot = row.RootPath;
            HighlightSelectedDrive();
            StartScan(row.RootPath);
        }
    }

    [RelayCommand]
    private void Scan()
    {
        if (!string.IsNullOrWhiteSpace(_selectedDriveRoot))
        {
            StartScan(_selectedDriveRoot);
        }
    }

    [RelayCommand]
    private void CancelScan() => CancelScanCore();

    private void CancelScanCore()
    {
        try
        {
            _scanCts?.Cancel();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.CancelScan");
        }
    }

    private void StartScan(string rootPath)
        => AsyncEventSafe.Run(() => StartScanAsync(rootPath), "DiskAnalyzerViewModel.StartScan");

    private async Task StartScanAsync(string rootPath)
    {
        if (IsScanning)
        {
            CancelScanCore();
            await Task.Delay(150).ConfigureAwait(true);
        }

        var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;
        IsScanning = true;
        _liveRenderShown = false;
        ScanStatusText = L("Disk_StatusAnalyzing");
        _root = null;
        _current = null;
        _history.Clear();
        _liveRoot = null;
        SunburstRenderRequested?.Invoke(SunburstRender.Clear);
        Zones = null;
        ZonesTitleText = L("Disk_Zones");
        ZonesSubTitleText = string.Empty;
        CenterTitleText = L("Disk_StatusScanning");
        CenterSizeText = string.Empty;
        CenterHintText = string.Empty;

        var progress = new Progress<DiskScanProgress>(p =>
        {
            ScanStatusText = $"{DiskItemNode.FormatSize(p.Bytes)} · {p.Files:N0} fichiers · {p.Folders:N0} dossiers";
        });

        try
        {
            DriveInfo drive = new(rootPath);
            _driveFreeBytes = drive.AvailableFreeSpace;

            DiskItemNode root = await DiskAnalyzerService.ScanAsync(rootPath, progress, node => OnScanWaveCompleted(node, scanCts), scanCts.Token);
            if (!ReferenceEquals(_scanCts, scanCts))
            {
                return;
            }

            _root = root;
            _current = root;
            _history.Clear();
            _liveRoot = null;
            IsScanning = false;

            UpdateCenter();
            UpdateBreadcrumb();
            UpdateZonesList();
            SunburstRenderRequested?.Invoke(SunburstRender.Animated);

            ScanStatusText = string.Format(L("Disk_StatusDone"), DiskItemNode.FormatSize(root.SizeBytes), $"{root.FileCount:N0}");
        }
        catch (OperationCanceledException)
        {
            if (!ReferenceEquals(_scanCts, scanCts))
            {
                return;
            }

            ScanStatusText = L("Disk_StatusCancelled");
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_scanCts, scanCts))
            {
                return;
            }

            ScanStatusText = L("Disk_StatusError") + ex.Message;
        }
        finally
        {
            scanCts.Dispose();
            if (ReferenceEquals(_scanCts, scanCts))
            {
                _scanCts = null;
                IsScanning = false;
                _liveRoot = null;
                SunburstRenderRequested?.Invoke(SunburstRender.RestoreOpacity);
                if (_root == null)
                {
                    SunburstRenderRequested?.Invoke(SunburstRender.Empty);
                }
            }
        }
    }

    private void OnScanWaveCompleted(DiskItemNode root, CancellationTokenSource scanCts)
    {
        if (!ReferenceEquals(_scanCts, scanCts) || !IsScanning)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _lastLiveRender).TotalMilliseconds < LiveRenderIntervalMs)
        {
            return;
        }

        _lastLiveRender = now;

        DiskZoneSnapshot? snapshot = null;
        try
        {
            snapshot = DiskAnalyzerService.BuildSnapshot(root, RingCount, MaxZonesPerRing);
        }
        catch
        {
            return;
        }

        if (snapshot == null)
        {
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_scanCts, scanCts) || !IsScanning)
            {
                return;
            }

            _liveRoot = SnapshotToZone(snapshot);
            SunburstRenderRequested?.Invoke(SunburstRender.Full);
            UpdateCenter();
            if (!_liveRenderShown)
            {
                _liveRenderShown = true;
                SunburstRenderRequested?.Invoke(SunburstRender.LiveFadeIn);
            }
        });
    }

    private static Zone SnapshotToZone(DiskZoneSnapshot snapshot)
    {
        return new Zone(
            snapshot.Name,
            snapshot.FullName,
            snapshot.SizeBytes,
            snapshot.FileCount,
            snapshot.Children.Select(SnapshotToZone).ToList(),
            Node: null);
    }

    private static Zone BuildZone(DiskItemNode node, int depth)
    {
        var children = new List<Zone>();
        if (depth < RingCount)
        {
            var sorted = node.Children
                .Where(c => c.SizeBytes > 0)
                .OrderByDescending(c => c.SizeBytes)
                .ToList();

            foreach (DiskItemNode child in sorted.Take(MaxZonesPerRing))
            {
                children.Add(BuildZone(child, depth + 1));
            }

            long rest = sorted.Skip(MaxZonesPerRing).Sum(c => c.SizeBytes);
            if (rest > 0)
            {
                children.Add(new Zone(
                    $"{L("Disk_Others")} ({sorted.Count - Math.Min(MaxZonesPerRing, sorted.Count)})",
                    node.FullName,
                    rest,
                    0,
                    [],
                    Node: null));
            }
        }

        return new Zone(
            string.IsNullOrEmpty(node.Name) ? node.FullName : node.Name,
            node.FullName,
            node.SizeBytes,
            node.FileCount,
            children,
            node);
    }

    private void UpdateCenter()
    {
        Zone? zone = CurrentZone;
        if (zone == null)
        {
            CenterTitleText = "Coclico";
            CenterSizeText = L("Disk_StatusPickDrive");
            CenterHintText = string.Empty;
            return;
        }

        string name = zone.Name.Length > 28 ? zone.Name[..25] + "..." : zone.Name;
        CenterTitleText = name;
        CenterSizeText = DiskItemNode.FormatSize(zone.Size);
        CenterHintText = IsScanning ? "analyse en cours..." :
            IsCurrentRoot ? L("Disk_HintZoom") : L("Disk_HintUp");
    }

    private void UpdateBreadcrumb()
    {
        if (IsScanning || _root == null || _current == null)
        {
            BreadcrumbItems = null;
            return;
        }

        var chain = new List<Crumb>();
        var visited = new HashSet<DiskItemNode>();
        BuildChain(_root, _current, chain, visited);
        chain.Insert(0, new Crumb { Label = _root.Name, Node = _root });
        BreadcrumbItems = chain;

        static bool BuildChain(DiskItemNode from, DiskItemNode target, List<Crumb> chain, HashSet<DiskItemNode> visited)
        {
            if (ReferenceEquals(from, target))
            {
                return true;
            }

            if (!visited.Add(from))
            {
                return false;
            }

            foreach (DiskItemNode child in from.Children)
            {
                if (BuildChain(child, target, chain, visited))
                {
                    chain.Insert(0, new Crumb { Label = child.Name, Node = child });
                    return true;
                }
            }

            return false;
        }
    }

    public void UpdateSelectedInfo(DiskItemNode? node)
    {
        if (node == null)
        {
            SelectedNameText = "---";
            SelectedDetailsText = string.Empty;
            CanOpenExplorer = false;
            return;
        }

        SelectedNameText = node.Name;
        SelectedDetailsText = $"{DiskItemNode.FormatSize(node.SizeBytes)} · {node.FileCount:N0} fichiers{Environment.NewLine}{node.FullName}";
        CanOpenExplorer = node.IsDirectory;
    }

    private void UpdateZonesList()
    {
        Zone? zone = CurrentZone;
        if (zone == null)
        {
            Zones = null;
            ZonesTitleText = L("Disk_Zones");
            ZonesSubTitleText = string.Empty;
            return;
        }

        ZonesTitleText = string.Format(L("Disk_ZonesFor"), zone.Name.ToUpperInvariant());
        long total = Math.Max(1, zone.Size);
        ZonesSubTitleText = $"{zone.Children.Count:N0} éléments · {zone.Files:N0} fichiers · {DiskItemNode.FormatSize(zone.Size)}";
        var rows = new List<ZoneRow>();
        foreach (Zone child in zone.Children)
        {
            double pct = child.Size * 100.0 / total;
            int index = rows.Count % Palette.Length;
            rows.Add(new ZoneRow
            {
                Label = child.Name,
                SizeText = DiskItemNode.FormatSize(child.Size),
                PercentText = $"{pct:0.#}%",
                Percent = Math.Min(100, pct),
                ZoneBrush = new SolidColorBrush(ShadeColor(Palette[index], 0)),
                Node = child.Node
            });
        }

        Zones = rows;
    }

    private void SetCurrent(DiskItemNode node)
    {
        _current = node;
        UpdateCenter();
        UpdateBreadcrumb();
        UpdateZonesList();
        SunburstRenderRequested?.Invoke(SunburstRender.Full);
    }

    public void NavigateInto(DiskItemNode? node)
    {
        if (node == null || _current == null || IsScanning)
        {
            return;
        }

        _history.Push(_current);
        SetCurrent(node);
        NavigationAnimationRequested?.Invoke(false);
    }

    [RelayCommand]
    private void GoUp()
    {
        if (_history.Count > 0)
        {
            SetCurrent(_history.Pop());
            NavigationAnimationRequested?.Invoke(true);
        }
        else if (_root != null && !ReferenceEquals(_current, _root))
        {
            SetCurrent(_root);
            NavigationAnimationRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private void GoHome()
    {
        if (_root != null)
        {
            _history.Clear();
            SetCurrent(_root);
            NavigationAnimationRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private void NavigateToCrumb(Crumb? crumb)
    {
        if (crumb != null)
        {
            _history.Clear();
            SetCurrent(crumb.Node);
            NavigationAnimationRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private void NavigateToZone(ZoneRow? row)
    {
        if (row != null)
        {
            UpdateSelectedInfo(row.Node);
            if (row.Node != null)
            {
                NavigateInto(row.Node);
            }
        }
    }

    [RelayCommand]
    private void OpenExplorer()
    {
        if (_current == null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _current.FullName, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.OpenExplorer");
        }
    }

    private void SetMode(string mode)
    {
        if (CurrentMode == mode)
        {
            return;
        }

        CurrentMode = mode;
        ModePanelAnimationRequested?.Invoke(mode);
    }

    [RelayCommand]
    private void SelectMode(string? mode)
    {
        if (!string.IsNullOrEmpty(mode))
        {
            SetMode(mode);
        }
    }

    private void BrowseFolderCore(string current)
    {
        string? folder = BrowseFolder?.Invoke(current);
        if (folder != null)
        {
            DuplicatesRoot = folder;
        }
    }

    [RelayCommand]
    private void BrowseDuplicatesRoot() => BrowseFolderCore(DuplicatesRoot);

    [RelayCommand]
    private void BrowseLargeRoot()
    {
        string? folder = BrowseFolder?.Invoke(LargeRoot);
        if (folder != null)
        {
            LargeRoot = folder;
        }
    }

    [RelayCommand]
    private void DuplicatesScan() => _ = RunDuplicatesScanAsync();

    [RelayCommand]
    private void DuplicatesCancel()
    {
        _duplicatesCts?.Cancel();
        DuplicatesSummaryText = L("Disk_DupCancelling");
    }

    private async Task RunDuplicatesScanAsync()
    {
        string root = DuplicatesRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            DuplicatesSummaryText = L("Disk_StatusFolderMissing");
            return;
        }

        long minSize = DuplicatesMinSizeIndex switch
        {
            1 => 10L * 1024 * 1024,
            2 => 50L * 1024 * 1024,
            3 => 100L * 1024 * 1024,
            _ => 1024L * 1024
        };

        _duplicatesCts?.Cancel();
        _duplicatesCts = new CancellationTokenSource();
        CancellationToken ct = _duplicatesCts.Token;

        DuplicatesGroups = null;
        DuplicatesSummaryText = L("Disk_DupInventoryPlain");
        IsDuplicatesScanning = true;

        var progress = new Progress<DuplicateScanProgress>(p =>
        {
            DuplicatesSummaryText = p.Phase switch
            {
                1 => string.Format(L("Disk_DupInventory"), $"{p.FilesExamined:N0}"),
                2 => string.Format(L("Disk_DupQuickHash"), $"{p.FilesExamined:N0}"),
                3 => L("Disk_DupVerify"),
                _ => L("Disk_DupDone")
            };
        });

        try
        {
            IReadOnlyList<DuplicateGroup> groups = await DuplicateFinderService.FindDuplicatesAsync(
                new DuplicateScanOptions(root, minSize, DuplicateGroupsShown), progress, ct);

            List<DuplicateGroupRow> rows = groups.Select(g => new DuplicateGroupRow
            {
                FileName = g.FileName,
                CountText = $"{g.FileCount} fichiers identiques",
                WastedText = string.Format(L("Disk_Wasted"), DiskItemNode.FormatSize(g.WastedBytes)),
                Files = g.Paths.Select(p => new DuplicateFileRow
                {
                    Path = p,
                    FileName = System.IO.Path.GetFileName(p),
                    Folder = System.IO.Path.GetDirectoryName(p) ?? p
                }).ToList()
            }).ToList();

            _duplicateGroups = rows;
            DuplicatesGroups = rows;
            long wasted = groups.Sum(g => g.WastedBytes);
            DuplicatesSummaryText = rows.Count == 0
                ? L("Disk_DupNone")
                : string.Format(L("Disk_DupSummary"), rows.Count, DiskItemNode.FormatSize(wasted));
            DuplicatesStatusText = "";
        }
        catch (OperationCanceledException)
        {
            DuplicatesSummaryText = L("Disk_DupCancelled");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.DuplicatesScan");
            DuplicatesSummaryText = L("Disk_DupError");
        }
        finally
        {
            IsDuplicatesScanning = false;
        }
    }

    [RelayCommand]
    private void DuplicatesSelectCopies()
    {
        foreach (DuplicateGroupRow group in _duplicateGroups)
        {
            for (int i = 0; i < group.Files.Count; i++)
            {
                group.Files[i].IsSelected = i > 0;
            }
        }
    }

    [RelayCommand]
    private void DuplicatesDelete()
        => AsyncEventSafe.Run(DuplicatesDeleteAsync, "DiskAnalyzerViewModel.DuplicatesDelete");

    private async Task DuplicatesDeleteAsync()
    {
        List<string> targets = _duplicateGroups
            .SelectMany(g => g.Files)
            .Where(f => f.IsSelected)
            .Select(f => f.Path)
            .ToList();

        if (targets.Count == 0)
        {
            DuplicatesStatusText = L("Disk_StatusNoFiles");
            return;
        }

        long bytes = 0;
        foreach (string path in targets)
        {
            try
            {
                bytes += new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        bool confirm = Confirm?.Invoke(
            string.Format(L("Disk_DupConfirmDelete"), targets.Count, DiskItemNode.FormatSize(bytes)),
            "Suppression des doublons sélectionnés") ?? false;

        if (!confirm)
        {
            return;
        }

        try
        {
            if (RecycleBinHelper.SendToRecycleBin(targets))
            {
                ToastService.Show(string.Format(L("Disk_BinSentMany"), targets.Count));
                await RunDuplicatesScanAsync();
            }
            else
            {
                DuplicatesStatusText = L("Disk_BinRefusedSome");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.DeleteDuplicates");
            DuplicatesStatusText = L("Disk_DupDeleteError");
        }
    }

    [RelayCommand]
    private void LargeScan() => _ = RunLargeScanAsync();

    [RelayCommand]
    private void LargeCancel()
    {
        _largeCts?.Cancel();
        LargeStatusText = L("Disk_StatusDupCancel");
    }

    private async Task RunLargeScanAsync()
    {
        string root = LargeRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            LargeStatusText = L("Disk_StatusFolderMissing");
            return;
        }

        int count = LargeTopCountIndex switch
        {
            0 => 50,
            2 => 200,
            _ => 100
        };

        _largeCts?.Cancel();
        _largeCts = new CancellationTokenSource();
        CancellationToken ct = _largeCts.Token;

        LargeFiles = null;
        LargeStatusText = L("Disk_LargeScanning");
        IsLargeScanning = true;

        try
        {
            IReadOnlyList<LargeFileEntry> files = await DuplicateFinderService.FindLargestFilesAsync(root, count, null, ct);

            var rows = files.Select(f => new LargeFileRow
            {
                FileName = f.FileName,
                Path = f.Path,
                SizeText = DiskItemNode.FormatSize(f.SizeBytes)
            }).ToList();

            _largeFiles = rows;
            LargeFiles = rows;
            LargeStatusText = files.Count == 0
                ? L("Disk_StatusNoFiles")
                : string.Format(L("Disk_LargeDone"), files.Count, DiskItemNode.FormatSize(files[0].SizeBytes));
        }
        catch (OperationCanceledException)
        {
            LargeStatusText = L("Disk_StatusCancelled");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.LargeScan");
            LargeStatusText = L("Disk_LargeError");
        }
        finally
        {
            IsLargeScanning = false;
        }
    }

    [RelayCommand]
    private void OpenDuplicate(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            OpenInExplorer(path, selectFile: false);
        }
    }

    [RelayCommand]
    private void OpenLargeLocation(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            OpenInExplorer(path, selectFile: true);
        }
    }

    [RelayCommand]
    private void DeleteLargeFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (!RecycleBinHelper.SendToRecycleBin([path]))
            {
                LargeStatusText = L("Disk_BinRefusedOne");
                return;
            }

            ToastService.Show(L("Disk_BinSentOne"));
            if (_largeFiles.Count > 0)
            {
                _ = _largeFiles.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                LargeFiles = null;
                LargeFiles = _largeFiles;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.DeleteLargeFile");
            LargeStatusText = "Erreur pendant la suppression.";
        }
    }

    private static void OpenInExplorer(string path, bool selectFile)
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string args = selectFile ? $"/select,\"{path}\"" : $"\"{directory}\"";
            _ = Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerViewModel.OpenInExplorer");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
