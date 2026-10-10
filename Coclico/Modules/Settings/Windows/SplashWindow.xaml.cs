using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using Coclico.Services;

namespace Coclico.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null && TxtVersion != null)
        {
            TxtVersion.Text = $"v{version.Major}.{version.Minor}.{version.Build} STABLE";
        }
    }

    public async Task RunStartupAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Closing += (s, e) => { try { cts.Cancel(); } catch (Exception ex) { LoggingService.LogException(ex, "SplashWindow.RunStartupAsync.ClosingCancel"); } };

        var progress = new Progress<StartupProgress>(ApplyProgress);

        try
        {
            await ServiceContainer.GetRequired<StartupService>().RunStartupAsync(progress, cts.Token);

            TriggerFlashIgnition();
            await Task.Delay(260);
        }
        catch (OperationCanceledException)
        {
            LoggingService.LogInfo("Startup timed out or was cancelled from splash");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SplashWindow.RunStartupAsync");
        }
    }

    private void ApplyProgress(StartupProgress p)
    {
        TxtStatus?.Text = p.Status;

        TxtSubDetail?.Text = p.SubDetail;

        TxtPercent?.Text = $"{p.Percent} %";

        if (PbProgress != null)
        {
            var anim = new DoubleAnimation
            {
                To = p.Percent,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            PbProgress.BeginAnimation(RangeBase.ValueProperty, anim);
        }
    }

    private void TriggerFlashIgnition()
    {
        if (FlashOverlay == null)
        {
            return;
        }

        var flashAnim = new DoubleAnimation
        {
            From = 0,
            To = 0.08,
            Duration = TimeSpan.FromMilliseconds(180),
            AutoReverse = true,
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        FlashOverlay.BeginAnimation(UIElement.OpacityProperty, flashAnim);
    }
}
