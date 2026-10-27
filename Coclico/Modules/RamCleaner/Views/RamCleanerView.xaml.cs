using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class RamCleanerView : UserControl
{
    private static readonly Brush _sparklineBrush = CreateSparklineBrush();

    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _gpuTimer = new();
    private Polyline? _sparkline;

    public RamCleanerViewModel ViewModel { get; }

    public RamCleanerView()
    {
        InitializeComponent();
        ViewModel = ServiceContainer.GetOptional<RamCleanerViewModel>() ?? new RamCleanerViewModel();
        DataContext = ViewModel;

        ViewModel.RestartGpuPipelineConfirm = ConfirmRestartGpuPipeline;

        _monitorTimer.Interval = TimeSpan.FromMilliseconds(ViewModel.MonitorIntervalMs);
        _monitorTimer.Tick += OnMonitorTimerTick;

        _gpuTimer.Interval = TimeSpan.FromSeconds(60);
        _gpuTimer.Tick += (_, _) => Task.Run(ViewModel.RefreshGpuAsync);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Attach();
        _monitorTimer.Start();
        _gpuTimer.Start();

        ViewModel.RefreshStats();
        RedrawSparkline();
        _ = Task.Run(ViewModel.RefreshGpuAsync);
        ViewModel.RefreshProcesses();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Detach();
        _monitorTimer.Stop();
        _gpuTimer.Stop();
    }

    private void OnMonitorTimerTick(object? sender, EventArgs e)
    {
        _monitorTimer.Interval = TimeSpan.FromMilliseconds(ViewModel.MonitorIntervalMs);
        ViewModel.RefreshStats();
        RedrawSparkline();
    }

    private bool ConfirmRestartGpuPipeline()
        => MessageBox.Show(
            TryFindResource("RamCleaner_GpuPipelineConfirm") as string ?? string.Empty,
            TryFindResource("RamCleaner_GpuPipelineTitle") as string ?? string.Empty,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void HistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _sparkline = null;
        RedrawSparkline();
    }

    private void RedrawSparkline()
    {
        if (HistoryCanvas == null || HistoryCanvas.ActualWidth <= 0)
        {
            return;
        }

        double[] history = ViewModel.GetRamHistorySnapshot();
        if (history.Length < 2)
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
        int n = history.Length;

        var points = new PointCollection(n);
        for (int i = 0; i < n; i++)
        {
            double x = i * (w / (n - 1));
            double y = (1.0 - (history[i] / 100.0)) * h;
            points.Add(new Point(x, y));
        }
        _sparkline.Points = points;
    }

    private static Brush CreateSparklineBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
        brush.Freeze();
        return brush;
    }
}
