using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Modules.DiskAnalyzer.Services;
using Coclico.Services;
using Path = System.Windows.Shapes.Path;

namespace Coclico.Modules.DiskAnalyzer.Views;

public sealed class DriveRow
{
    public required string Label { get; init; }
    public required string SizeText { get; init; }
    public required string UsageText { get; init; }
    public required double UsedBarWidth { get; init; }
    public required Brush BarBrush { get; init; }
    public required string RootPath { get; init; }
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

public partial class DiskAnalyzerView : UserControl
{
    private const int MaxZonesPerRing = 30;
    private const double MinSegmentAngle = 1.0;
    private const int RingCount = 3;
    private const int LiveRenderIntervalMs = 250;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0xD9, 0x46, 0xEF), Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0xF4, 0x3F, 0x5E), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x84, 0xCC, 0x16), Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0x10, 0xB9, 0x81),
        Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x0E, 0xA5, 0xE9),
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x63, 0x66, 0xF1)
    ];

    private static string L(string key)
    {
        return Application.Current?.TryFindResource(key) as string
            ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key)
            ?? key;
    }

    private sealed record Zone(
        string Name,
        string FullName,
        long Size,
        long Files,
        IReadOnlyList<Zone> Children,
        DiskItemNode? Node);

    private DiskItemNode? _root;
    private DiskItemNode? _current;
    private readonly Stack<DiskItemNode> _history = new();
    private Zone? _liveRoot;
    private long _driveFreeBytes;
    private string? _selectedDriveRoot;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _duplicatesCts;
    private CancellationTokenSource? _largeCts;
    private bool _scanning;
    private DateTime _lastLiveRender = DateTime.MinValue;

    public DiskAnalyzerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadDrives();
            InitializeToolRoots();
        };
        Unloaded += (_, _) =>
        {
            CancelScan();
            _duplicatesCts?.Cancel();
            _largeCts?.Cancel();
        };
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
                UsageText = $"{usedPct:0}% utilisés --- {freeGb:0.#} Go libres",
                UsedBarWidth = Math.Max(2, usedPct * 1.2),
                BarBrush = new SolidColorBrush(usedPct > 88 ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0xF5, 0x9E, 0x0B)),
                RootPath = drive.RootDirectory.FullName
            });
        }

        DrivesList.ItemsSource = rows;
        if (rows.Count > 0)
        {
            _selectedDriveRoot = rows[0].RootPath;
        }
    }

    private void DriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveRow row })
        {
            _selectedDriveRoot = row.RootPath;
            StartScan(row.RootPath);
        }
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedDriveRoot))
        {
            StartScan(_selectedDriveRoot);
        }
    }

    private async void StartScan(string rootPath)
    {
        if (_scanning)
        {
            CancelScan();
            await Task.Delay(150).ConfigureAwait(true);
        }

        _scanning = true;
        _scanCts = new CancellationTokenSource();
        BtnScan.Visibility = Visibility.Collapsed;
        BtnCancelScan.Visibility = Visibility.Visible;
        ScanProgress.Visibility = Visibility.Visible;
        ScanStatus.Text = L("Disk_StatusAnalyzing");
        _root = null;
        _current = null;
        _history.Clear();
        _liveRoot = null;
        SunburstCanvas.Children.Clear();
        ZonesList.ItemsSource = null;
        CenterTitle.Text = L("Disk_StatusScanning");
        CenterSize.Text = "";
        CenterHint.Text = "";

        var progress = new Progress<DiskScanProgress>(p =>
        {
            ScanStatus.Text = $"{DiskItemNode.FormatSize(p.Bytes)} --- {p.Files:N0} fichiers --- {p.Folders:N0} dossiers";
        });

        try
        {
            DriveInfo drive = new(rootPath);
            _driveFreeBytes = drive.AvailableFreeSpace;

            DiskItemNode root = await DiskAnalyzerService.ScanAsync(rootPath, progress, OnScanWaveCompleted, _scanCts.Token);
            _root = root;
            _current = root;
            _history.Clear();
            _liveRoot = null;
            _scanning = false;

            UpdateCenter();
            UpdateBreadcrumb();
            UpdateZonesList();

            RenderSunburstWithAnimation();

            ScanStatus.Text = string.Format(L("Disk_StatusDone"), DiskItemNode.FormatSize(root.SizeBytes), $"{root.FileCount:N0}");
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = L("Disk_StatusCancelled");
        }
        catch (Exception ex)
        {
            ScanStatus.Text = L("Disk_StatusError") + ex.Message;
        }
        finally
        {
            _scanning = false;
            _liveRoot = null;
            BtnScan.Visibility = Visibility.Visible;
            BtnCancelScan.Visibility = Visibility.Collapsed;
            ScanProgress.Visibility = Visibility.Collapsed;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void OnScanWaveCompleted(DiskItemNode root)
    {
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

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!_scanning)
            {
                return;
            }

            _liveRoot = SnapshotToZone(snapshot);
            RenderSunburst();
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

    private void BtnCancelScan_Click(object sender, RoutedEventArgs e)
    {
        CancelScan();
    }

    private void CancelScan()
    {
        try
        {
            _scanCts?.Cancel();
        }
        catch (Exception ex) { LoggingService.LogException(ex, "DiskAnalyzerView.CancelScan"); }
    }

    private Zone? CurrentZone => _scanning ? _liveRoot : (_current != null ? BuildZone(_current, 0) : null);

    private void RefreshAll()
    {
        UpdateCenter();
        UpdateBreadcrumb();
        RenderSunburst();
        UpdateZonesList();
    }

    private void SetCurrent(DiskItemNode node)
    {
        _current = node;
        RefreshAll();
    }

    private void NavigateInto(DiskItemNode? node)
    {
        if (node == null || _current == null || _scanning) return;
        _history.Push(_current);
        SetCurrent(node);
        PlayNavigationAnimation(false);
    }

    private void CenterButton_Click(object sender, RoutedEventArgs e) => GoUp();
    private void BtnGoUp_Click(object sender, RoutedEventArgs e) => GoUp();

    private void BtnGoHome_Click(object sender, RoutedEventArgs e)
    {
        if (_root != null)
        {
            _history.Clear();
            SetCurrent(_root);
            PlayNavigationAnimation(true);
        }
    }

    private void GoUp()
    {
        if (_history.Count > 0)
        {
            SetCurrent(_history.Pop());
            PlayNavigationAnimation(true);
        }
        else if (_root != null && !ReferenceEquals(_current, _root))
        {
            SetCurrent(_root);
            PlayNavigationAnimation(true);
        }
    }

    private void PlayNavigationAnimation(bool zoomOut)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        double from = zoomOut ? 1.06 : 0.86;
        var scaleX = new DoubleAnimation(from, 1.0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(from, 1.0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease };
        ChartScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ChartScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void RenderSunburstWithAnimation()
    {
        var oldElements = new List<UIElement>(SunburstCanvas.Children.Cast<UIElement>());
        SunburstCanvas.Children.Clear();

        RenderSunburst();

        var newElements = new List<UIElement>(SunburstCanvas.Children.Cast<UIElement>());
        SunburstCanvas.Children.Clear();

        var fadeIn = new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromMilliseconds(600) };
        var scaleIn = new DoubleAnimation { From = 0.7, To = 1.0, Duration = TimeSpan.FromMilliseconds(800) };
        scaleIn.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        foreach (var element in newElements)
        {
            element.Opacity = 0.0;
            element.RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 };
            SunburstCanvas.Children.Add(element);
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn.Clone());
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn.Clone());
            ((ScaleTransform)element.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn.Clone());
        }
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Crumb crumb })
        {
            _history.Clear();
            SetCurrent(crumb.Node);
            PlayNavigationAnimation(true);
        }
    }

    private void ZoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ZoneRow row })
        {
            UpdateSelectedInfo(row.Node, row.ZoneBrush);
            if (row.Node != null && !row.Label.StartsWith("Autres"))
                NavigateInto(row.Node);
        }
    }

    private void BtnOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _current.FullName, UseShellExecute = true });
        }
        catch (Exception ex) { LoggingService.LogException(ex, "DiskAnalyzerView.BtnOpenExplorer_Click"); }
    }

    private void UpdateCenter()
    {
        Zone? zone = CurrentZone;
        if (zone == null)
        {
            CenterTitle.Text = "Coclico";
            CenterSize.Text = L("Disk_StatusPickDrive");
            CenterHint.Text = string.Empty;
            return;
        }
        string name = zone.Name.Length > 28 ? zone.Name[..25] + "..." : zone.Name;
        CenterTitle.Text = name;
        CenterSize.Text = DiskItemNode.FormatSize(zone.Size);
        CenterHint.Text = _scanning ? "analyse en cours..." :
            ReferenceEquals(_current, _root) ? L("Disk_HintZoom") : L("Disk_HintUp");
    }

    private void UpdateBreadcrumb()
    {
        if (_scanning || _root == null || _current == null)
        {
            Breadcrumb.ItemsSource = null;
            return;
        }
        var chain = new List<Crumb>();
        var visited = new HashSet<DiskItemNode>();
        BuildChain(_root, _current, chain, visited);
        chain.Insert(0, new Crumb { Label = _root.Name, Node = _root });
        Breadcrumb.ItemsSource = chain;

        static bool BuildChain(DiskItemNode from, DiskItemNode target, List<Crumb> chain, HashSet<DiskItemNode> visited)
        {
            if (ReferenceEquals(from, target)) return true;
            if (!visited.Add(from)) return false;
            foreach (DiskItemNode child in from.Children)
                if (BuildChain(child, target, chain, visited))
                {
                    chain.Insert(0, new Crumb { Label = child.Name, Node = child });
                    return true;
                }
            return false;
        }
    }

    private void UpdateSelectedInfo(DiskItemNode? node, Brush brush)
    {
        if (node == null)
        {
            SelectedName.Text = "---";
            SelectedDetails.Text = string.Empty;
            BtnOpenExplorer.Visibility = Visibility.Collapsed;
            return;
        }
        SelectedName.Text = node.Name;
        SelectedDetails.Text = $"{DiskItemNode.FormatSize(node.SizeBytes)} * {node.FileCount:N0} fichiers\n{node.FullName}";
        BtnOpenExplorer.Visibility = node.IsDirectory ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateZonesList()
    {
        Zone? zone = CurrentZone;
        if (zone == null)
        {
            ZonesList.ItemsSource = null;
            ZonesTitle.Text = "ZONES";
            ZonesSubTitle.Text = string.Empty;
            return;
        }
        ZonesTitle.Text = string.Format(L("Disk_ZonesFor"), zone.Name.ToUpperInvariant());
        long total = Math.Max(1, zone.Size);
        ZonesSubTitle.Text = $"{zone.Children.Count:N0} elements --- {zone.Files:N0} fichiers --- {DiskItemNode.FormatSize(zone.Size)}";
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
        ZonesList.ItemsSource = rows;
    }

    private void ChartHost_SizeChanged(object sender, SizeChangedEventArgs e) => RenderSunburst();

    private void RenderSunburst()
    {
        SunburstCanvas.Children.Clear();
        Zone? root = CurrentZone;
        if (root == null) return;
        double width = ChartHost.ActualWidth, height = ChartHost.ActualHeight;
        if (width < 80 || height < 80) return;

        double cx = width / 2.0, cy = height / 2.0;
        double outerRadius = Math.Min(width, height) / 2.0 - 8;
        double ringWidth = outerRadius * 0.86 / RingCount;
        double innerRadius = outerRadius - (ringWidth * RingCount);
        double holeRadius = Math.Max(28, innerRadius - 6);
        CenterButton.Width = Math.Max(10, holeRadius * 1.9);
        CenterButton.Height = Math.Max(10, holeRadius * 1.9);
        CenterTitle.FontSize = holeRadius > 70 ? 13 : holeRadius > 40 ? 11.5 : 10;
        CenterSize.FontSize = holeRadius > 70 ? 11 : 10;
        CenterHint.Visibility = holeRadius > 46 ? Visibility.Visible : Visibility.Collapsed;

        long totalSize = Math.Max(1, root.Size);
        double availableSweep = 360.0;
        if (!_scanning && ReferenceEquals(_current, _root) && _root != null && _driveFreeBytes > 0)
        {
            long freeBytes = _driveFreeBytes;
            availableSweep = 360.0 * totalSize / Math.Max(1, totalSize + freeBytes);
            SunburstCanvas.Children.Add(CreateSlice(cx, cy, innerRadius, innerRadius + ringWidth,
                availableSweep, 360.0 - availableSweep,
                new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3A)), L("Disk_FreeSpace"), freeBytes, totalSize + freeBytes, null));
        }
        RenderLevel(root.Children, cx, cy, innerRadius, ringWidth, 0, availableSweep, 0, 0);
    }

    private void RenderLevel(IReadOnlyList<Zone> children, double cx, double cy,
        double innerRadius, double ringWidth, double startAngle, double sweepAngle, int colorIndex, int depth)
    {
        if (children.Count == 0 || sweepAngle <= 0 || depth >= RingCount) return;
        long total = children.Sum(c => c.Size);
        if (total <= 0) return;
        var visible = children.Where(c => c.Size * 360.0 / total >= MinSegmentAngle).Take(MaxZonesPerRing).ToList();
        long visibleSum = visible.Sum(c => c.Size);
        long rest = total - visibleSum;
        double cursor = startAngle, sweepPerByte = sweepAngle / total;
        double r0 = innerRadius + (depth * ringWidth), r1 = innerRadius + ((depth + 1) * ringWidth);

        for (int i = 0; i < visible.Count; i++)
        {
            Zone child = visible[i];
            double span = child.Size * sweepPerByte, gap = Math.Min(0.4, span * 0.04);
            Color color = ShadeColor(Palette[(colorIndex + i) % Palette.Length], depth);
            Path slice = CreateSlice(cx, cy, r0, r1, cursor, span - gap,
                new SolidColorBrush(color), child.Name, child.Size, total, child);
            SunburstCanvas.Children.Add(slice);
            if (span > 4.0 && depth + 1 < RingCount && child.Children.Count > 0)
                RenderLevel(child.Children, cx, cy, innerRadius, ringWidth, cursor, span - gap,
                    (colorIndex + i + 3) % Palette.Length, depth + 1);
            cursor += span;
        }
        if (rest > 0)
        {
            double span = rest * sweepPerByte;
            SunburstCanvas.Children.Add(CreateSlice(cx, cy, r0, r1, cursor, span,
                new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66)), "Autres", rest, total, null));
        }
    }

    private static Color ShadeColor(Color baseColor, int depth)
    {
        if (depth <= 0) return baseColor;
        double factor = 1.0 + (0.16 * depth);
        return Color.FromRgb(
            (byte)Math.Clamp(baseColor.R * factor, 0, 255),
            (byte)Math.Clamp(baseColor.G * factor, 0, 255),
            (byte)Math.Clamp(baseColor.B * factor, 0, 255));
    }

    private Path CreateSlice(double cx, double cy, double r0, double r1, double startAngle, double sweepAngle,
        Brush fill, string label, long sizeBytes, long totalSize, Zone? zone)
    {
        double a0 = (startAngle - 90) * Math.PI / 180.0;
        double a1 = (startAngle + sweepAngle - 90) * Math.PI / 180.0;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool large = sweepAngle > 180.0;
            var os = new Point(cx + r1 * Math.Cos(a0), cy + r1 * Math.Sin(a0));
            var oe = new Point(cx + r1 * Math.Cos(a1), cy + r1 * Math.Sin(a1));
            var ie = new Point(cx + r0 * Math.Cos(a1), cy + r0 * Math.Sin(a1));
            var is2 = new Point(cx + r0 * Math.Cos(a0), cy + r0 * Math.Sin(a0));
            ctx.BeginFigure(os, true, true);
            ctx.ArcTo(oe, new Size(r1, r1), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(ie, true, false);
            ctx.ArcTo(is2, new Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true, false);
        }

        double pct = totalSize > 0 ? sizeBytes * 100.0 / totalSize : 0;
        string trimmed = label.Length > 40 ? label[..37] + "..." : label;
        var path = new Path
        {
            Data = geometry,
            Fill = fill,
            Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0B)),
            StrokeThickness = 1.2,
            ToolTip = $"{trimmed} --- {DiskItemNode.FormatSize(sizeBytes)} ({pct:0.#}%)",
            Cursor = zone?.Node != null ? Cursors.Hand : null
        };

        path.MouseEnter += (s, e) => { path.Stroke = Brushes.White; path.StrokeThickness = 1.8; };
        path.MouseLeave += (s, e) => { path.Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0B)); path.StrokeThickness = 1.2; };
        if (zone?.Node != null) path.MouseLeftButtonUp += (s, e) => { UpdateSelectedInfo(zone.Node, fill); NavigateInto(zone.Node); };
        return path;
    }

    private void InitializeToolRoots()
    {
        string root = _selectedDriveRoot
            ?? DiskAnalyzerService.GetReadyDrives().FirstOrDefault()?.RootDirectory.FullName
            ?? "C:\\";
        DuplicatesRootBox.Text ??= root;
        LargeRootBox.Text ??= root;
        SetMode("Space");
    }

    private void BtnMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode })
        {
            SetMode(mode);
        }
    }

    private void SetMode(string mode)
    {
        SpaceModePanel.Visibility = mode == "Space" ? Visibility.Visible : Visibility.Collapsed;
        DuplicatesPanel.Visibility = mode == "Duplicates" ? Visibility.Visible : Visibility.Collapsed;
        LargeFilesPanel.Visibility = mode == "LargeFiles" ? Visibility.Visible : Visibility.Collapsed;

        StyleModeButton(BtnModeSpace, mode == "Space");
        StyleModeButton(BtnModeDuplicates, mode == "Duplicates");
        StyleModeButton(BtnModeLargeFiles, mode == "LargeFiles");
    }

    private void StyleModeButton(Button button, bool active)
    {
        button.Background = active ? FindResource("PrimaryBrush") as Brush : Brushes.Transparent;
        button.Foreground = active ? Brushes.White : FindResource("TextSecondaryBrush") as Brush;
    }

    private static void BrowseFolder(TextBox box)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Disk_StatusPickFolder") };
        if (!string.IsNullOrWhiteSpace(box.Text) && Directory.Exists(box.Text))
        {
            dialog.InitialDirectory = box.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            box.Text = dialog.FolderName;
        }
    }

    private void BtnDuplicatesBrowse_Click(object sender, RoutedEventArgs e) => BrowseFolder(DuplicatesRootBox);

    private void BtnLargeBrowse_Click(object sender, RoutedEventArgs e) => BrowseFolder(LargeRootBox);

    private void BtnDuplicatesScan_Click(object sender, RoutedEventArgs e) => _ = RunDuplicatesScanAsync();

    private void BtnDuplicatesCancel_Click(object sender, RoutedEventArgs e)
    {
        _duplicatesCts?.Cancel();
        DuplicatesSummary.Text = L("Disk_DupCancelling");
    }

    private async Task RunDuplicatesScanAsync()
    {
        string root = DuplicatesRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            DuplicatesSummary.Text = L("Disk_StatusFolderMissing");
            return;
        }

        long minSize = DuplicatesMinSize.SelectedIndex switch
        {
            1 => 10L * 1024 * 1024,
            2 => 50L * 1024 * 1024,
            3 => 100L * 1024 * 1024,
            _ => 1024L * 1024
        };

        _duplicatesCts?.Cancel();
        _duplicatesCts = new CancellationTokenSource();
        CancellationToken ct = _duplicatesCts.Token;

        DuplicatesList.ItemsSource = null;
        DuplicatesSummary.Text = L("Disk_DupInventoryPlain");
        DuplicatesProgress.Visibility = Visibility.Visible;
        BtnDuplicatesScan.IsEnabled = false;
        BtnDuplicatesCancel.Visibility = Visibility.Visible;

        var progress = new Progress<DuplicateScanProgress>(p =>
        {
            DuplicatesSummary.Text = p.Phase switch
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
                new DuplicateScanOptions(root, minSize), progress, ct);

            List<DuplicateGroupRow> rows = groups.Select(g => new DuplicateGroupRow
            {
                FileName = g.FileName,
                CountText = $"{g.FileCount} fichiers identiques",
                WastedText = string.Format(L("Disk_Wasted"), DiskItemNode.FormatSize(g.WastedBytes)),
                Files = g.Paths.Select(p => new DuplicateFileRow { Path = p }).ToList()
            }).ToList();

            DuplicatesList.ItemsSource = rows;
            long wasted = groups.Sum(g => g.WastedBytes);
            DuplicatesSummary.Text = rows.Count == 0
                ? L("Disk_DupNone")
                : string.Format(L("Disk_DupSummary"), rows.Count, DiskItemNode.FormatSize(wasted));
            DuplicatesStatus.Text = "";
        }
        catch (OperationCanceledException)
        {
            DuplicatesSummary.Text = L("Disk_DupCancelled");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerView.DuplicatesScan");
            DuplicatesSummary.Text = L("Disk_DupError");
        }
        finally
        {
            DuplicatesProgress.Visibility = Visibility.Collapsed;
            BtnDuplicatesScan.IsEnabled = true;
            BtnDuplicatesCancel.Visibility = Visibility.Collapsed;
        }
    }

    private void DuplicateOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            OpenInExplorer(path, selectFile: false);
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
            LoggingService.LogException(ex, "DiskAnalyzerView.OpenInExplorer");
        }
    }

    private void BtnDuplicatesSelectCopies_Click(object sender, RoutedEventArgs e)
    {
        if (DuplicatesList.ItemsSource is not IEnumerable<DuplicateGroupRow> rows)
        {
            return;
        }

        foreach (DuplicateGroupRow group in rows)
        {
            for (int i = 0; i < group.Files.Count; i++)
            {
                group.Files[i].IsSelected = i > 0;
            }
        }
    }

    private async void BtnDuplicatesDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DuplicatesList.ItemsSource is not IEnumerable<DuplicateGroupRow> rows)
        {
            return;
        }

        List<string> targets = rows
            .SelectMany(g => g.Files)
            .Where(f => f.IsSelected)
            .Select(f => f.Path)
            .ToList();

        if (targets.Count == 0)
        {
            DuplicatesStatus.Text = L("Disk_StatusNoFiles");
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

        MessageBoxResult result = MessageBox.Show(
            string.Format(L("Disk_DupConfirmDelete"), targets.Count, DiskItemNode.FormatSize(bytes)),
            "Suppression des doublons sélectionnés",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
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
                DuplicatesStatus.Text = L("Disk_BinRefusedSome");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerView.DeleteDuplicates");
            DuplicatesStatus.Text = L("Disk_DupDeleteError");
        }
    }

    private void BtnLargeScan_Click(object sender, RoutedEventArgs e) => _ = RunLargeScanAsync();

    private void BtnLargeCancel_Click(object sender, RoutedEventArgs e)
    {
        _largeCts?.Cancel();
        LargeStatus.Text = L("Disk_StatusDupCancel");
    }

    private async Task RunLargeScanAsync()
    {
        string root = LargeRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            LargeStatus.Text = L("Disk_StatusFolderMissing");
            return;
        }

        int count = LargeTopCount.SelectedIndex switch
        {
            0 => 50,
            2 => 200,
            _ => 100
        };

        _largeCts?.Cancel();
        _largeCts = new CancellationTokenSource();
        CancellationToken ct = _largeCts.Token;

        LargeFilesList.ItemsSource = null;
        LargeStatus.Text = L("Disk_LargeScanning");
        LargeProgress.Visibility = Visibility.Visible;
        BtnLargeScan.IsEnabled = false;
        BtnLargeCancel.Visibility = Visibility.Visible;

        try
        {
            IReadOnlyList<LargeFileEntry> files = await DuplicateFinderService.FindLargestFilesAsync(root, count, null, ct);

            LargeFilesList.ItemsSource = files.Select(f => new LargeFileRow
            {
                FileName = f.FileName,
                Path = f.Path,
                SizeText = DiskItemNode.FormatSize(f.SizeBytes)
            }).ToList();

            LargeStatus.Text = files.Count == 0
                ? L("Disk_StatusNoFiles")
                : string.Format(L("Disk_LargeDone"), files.Count, DiskItemNode.FormatSize(files[0].SizeBytes));
        }
        catch (OperationCanceledException)
        {
            LargeStatus.Text = L("Disk_StatusCancelled");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerView.LargeScan");
            LargeStatus.Text = L("Disk_LargeError");
        }
        finally
        {
            LargeProgress.Visibility = Visibility.Collapsed;
            BtnLargeScan.IsEnabled = true;
            BtnLargeCancel.Visibility = Visibility.Collapsed;
        }
    }

    private void LargeOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            OpenInExplorer(path, selectFile: true);
        }
    }

    private void LargeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        try
        {
            if (!RecycleBinHelper.SendToRecycleBin([path]))
            {
                LargeStatus.Text = L("Disk_BinRefusedOne");
                return;
            }

            ToastService.Show(L("Disk_BinSentOne"));
            if (LargeFilesList.ItemsSource is List<LargeFileRow> rows)
            {
                _ = rows.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                LargeFilesList.ItemsSource = null;
                LargeFilesList.ItemsSource = rows;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "DiskAnalyzerView.LargeDelete");
            LargeStatus.Text = "Erreur pendant la suppression.";
        }
    }
}