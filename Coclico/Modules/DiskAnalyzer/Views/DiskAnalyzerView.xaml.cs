using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Coclico.Modules.DiskAnalyzer.Services;
using Coclico.Services;
using Coclico.ViewModels;
using Path = System.Windows.Shapes.Path;

namespace Coclico.Modules.DiskAnalyzer.Views;

public partial class DiskAnalyzerView : UserControl
{
    private const int MaxZonesPerRing = 30;
    private const double MinSegmentAngle = 1.0;
    private const int RingCount = 3;

    private static readonly SolidColorBrush SliceStrokeBrush = FrozenBrush(0x07, 0x08, 0x0B);
    private static readonly SolidColorBrush FreeSpaceBrush = FrozenBrush(0x2A, 0x2E, 0x3A);
    private static readonly SolidColorBrush RestSliceBrush = FrozenBrush(0x55, 0x5B, 0x66);

    private readonly DispatcherTimer _resizeDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };

    public DiskAnalyzerViewModel ViewModel { get; }

    public DiskAnalyzerView()
    {
        InitializeComponent();

        ViewModel = ServiceContainer.GetOptional<DiskAnalyzerViewModel>() ?? new DiskAnalyzerViewModel();
        DataContext = ViewModel;

        ViewModel.Confirm = (message, title) => MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        ViewModel.BrowseFolder = current =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Disk_StatusPickFolder") };
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                dialog.InitialDirectory = current;
            }

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        };

        ViewModel.SunburstRenderRequested += OnSunburstRenderRequested;
        ViewModel.NavigationAnimationRequested += PlayNavigationAnimation;
        ViewModel.ModePanelAnimationRequested += AnimateModePanel;

        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            RenderSunburst();
        };

        Loaded += (_, _) => ViewModel.Initialize();
        Unloaded += (_, _) =>
        {
            _resizeDebounce.Stop();
            ViewModel.Detach();
        };
    }

    private static string L(string key)
    {
        return Application.Current?.TryFindResource(key) as string
            ?? ServiceContainer.GetOptional<LocalizationService>()?.Get(key)
            ?? key;
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnSunburstRenderRequested(SunburstRender kind)
    {
        switch (kind)
        {
            case SunburstRender.Clear:
                SunburstCanvas.Children.Clear();
                SunburstCanvas.BeginAnimation(UIElement.OpacityProperty, null);
                SunburstCanvas.Opacity = 0;
                break;
            case SunburstRender.LiveFadeIn:
                FadeLiveCanvasIn();
                break;
            case SunburstRender.RestoreOpacity:
                SunburstCanvas.BeginAnimation(UIElement.OpacityProperty, null);
                SunburstCanvas.Opacity = 1;
                break;
            case SunburstRender.Empty:
            case SunburstRender.Full:
                RenderSunburst();
                break;
            case SunburstRender.Animated:
                RenderSunburstWithAnimation();
                break;
        }
    }

    private void FadeLiveCanvasIn()
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        SunburstCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
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

    private void AnimateModePanel(string mode)
    {
        Grid? panel = mode switch
        {
            "Duplicates" => DuplicatesPanel,
            "LargeFiles" => LargeFilesPanel,
            _ => SpaceModePanel
        };

        if (panel != null && panel.Visibility == Visibility.Visible)
        {
            AnimatePanelIn(panel);
        }
    }

    private static void AnimatePanelIn(UIElement panel)
    {
        panel.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void ChartHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void RenderSunburst()
    {
        SunburstCanvas.Children.Clear();
        Zone? root = ViewModel.CurrentZone;
        if (root == null)
        {
            RenderEmptyRings();
            return;
        }

        double width = ChartHost.ActualWidth, height = ChartHost.ActualHeight;
        if (width < 80 || height < 80)
        {
            return;
        }

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
        if (!ViewModel.IsScanning && ViewModel.IsCurrentRoot && ViewModel.DriveFreeBytes > 0)
        {
            long freeBytes = ViewModel.DriveFreeBytes;
            availableSweep = 360.0 * totalSize / Math.Max(1, totalSize + freeBytes);
            SunburstCanvas.Children.Add(CreateSlice(cx, cy, innerRadius, innerRadius + ringWidth,
                availableSweep, 360.0 - availableSweep,
                FreeSpaceBrush, L("Disk_FreeSpace"), freeBytes, totalSize + freeBytes, null));
        }

        RenderLevel(root.Children, cx, cy, innerRadius, ringWidth, 0, availableSweep, 0, 0);
    }

    private void RenderEmptyRings()
    {
        double width = ChartHost.ActualWidth, height = ChartHost.ActualHeight;
        if (width < 80 || height < 80)
        {
            return;
        }

        double outerRadius = Math.Min(width, height) / 2.0 - 8;
        if (outerRadius < 40)
        {
            return;
        }

        double ringWidth = outerRadius * 0.86 / RingCount;
        Brush stroke = TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray;
        for (int i = 0; i < RingCount; i++)
        {
            double radius = outerRadius - (i * ringWidth);
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = stroke,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection([4, 5]),
                IsHitTestVisible = false,
                Opacity = 0.55
            };
            Canvas.SetLeft(ring, (width / 2.0) - radius);
            Canvas.SetTop(ring, (height / 2.0) - radius);
            SunburstCanvas.Children.Add(ring);
        }
    }

    private void RenderSunburstWithAnimation()
    {
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

    private void RenderLevel(IReadOnlyList<Zone> children, double cx, double cy,
        double innerRadius, double ringWidth, double startAngle, double sweepAngle, int colorIndex, int depth)
    {
        if (children.Count == 0 || sweepAngle <= 0 || depth >= RingCount)
        {
            return;
        }

        long total = children.Sum(c => c.Size);
        if (total <= 0)
        {
            return;
        }

        var visible = children.Where(c => c.Size * 360.0 / total >= MinSegmentAngle).Take(MaxZonesPerRing).ToList();
        long visibleSum = visible.Sum(c => c.Size);
        long rest = total - visibleSum;
        double cursor = startAngle, sweepPerByte = sweepAngle / total;
        double r0 = innerRadius + (depth * ringWidth), r1 = innerRadius + ((depth + 1) * ringWidth);

        for (int i = 0; i < visible.Count; i++)
        {
            Zone child = visible[i];
            double span = child.Size * sweepPerByte, gap = Math.Min(0.4, span * 0.04);
            Color color = DiskAnalyzerViewModel.ShadeColor(DiskAnalyzerViewModel.Palette[(colorIndex + i) % DiskAnalyzerViewModel.Palette.Length], depth);
            var sliceBrush = new SolidColorBrush(color);
            sliceBrush.Freeze();
            Path slice = CreateSlice(cx, cy, r0, r1, cursor, span - gap,
                sliceBrush, child.Name, child.Size, total, child);
            SunburstCanvas.Children.Add(slice);
            if (span > 4.0 && depth + 1 < RingCount && child.Children.Count > 0)
            {
                RenderLevel(child.Children, cx, cy, innerRadius, ringWidth, cursor, span - gap,
                    (colorIndex + i + 3) % DiskAnalyzerViewModel.Palette.Length, depth + 1);
            }

            cursor += span;
        }

        if (rest > 0)
        {
            double span = rest * sweepPerByte;
            SunburstCanvas.Children.Add(CreateSlice(cx, cy, r0, r1, cursor, span,
                RestSliceBrush, "Autres", rest, total, null));
        }
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

        geometry.Freeze();
        double pct = totalSize > 0 ? sizeBytes * 100.0 / totalSize : 0;
        string trimmed = label.Length > 40 ? label[..37] + "..." : label;
        var path = new Path
        {
            Data = geometry,
            Fill = fill,
            Stroke = SliceStrokeBrush,
            StrokeThickness = 1.2,
            ToolTip = $"{trimmed} · {DiskItemNode.FormatSize(sizeBytes)} ({pct:0.#}%)",
            Cursor = zone?.Node != null ? Cursors.Hand : null
        };

        path.MouseEnter += (s, e) =>
        {
            path.Stroke = Brushes.White;
            path.StrokeThickness = 1.8;
        };
        path.MouseLeave += (s, e) =>
        {
            path.Stroke = SliceStrokeBrush;
            path.StrokeThickness = 1.2;
        };
        if (zone?.Node != null)
        {
            path.MouseLeftButtonUp += (s, e) =>
            {
                ViewModel.UpdateSelectedInfo(zone.Node);
                ViewModel.NavigateInto(zone.Node);
            };
        }

        return path;
    }
}
