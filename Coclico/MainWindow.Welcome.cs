using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Coclico;

public partial class MainWindow
{
    private void ShowWelcome()
    {
        WelcomeOverlay.Opacity = 0;
        WelcomeOverlay.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(0, 1, Motion.WelcomeOpen)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(Motion.WelcomeSlideInDistance, 0, Motion.WelcomeOpen)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        WelcomeOverlay.BeginAnimation(OpacityProperty, fade);
        WelcomeTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        _ = Dispatcher.InvokeAsync(() => BtnDismissWelcome.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void DismissWelcome()
    {
        if (WelcomeOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        var fade = new DoubleAnimation(1, 0, Motion.WelcomeClose)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var slide = new DoubleAnimation(0, 10, Motion.WelcomeClose)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        };
        WelcomeOverlay.BeginAnimation(OpacityProperty, fade);
        WelcomeTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void BtnDismissWelcome_Click(object sender, RoutedEventArgs e)
    {
        DismissWelcome();
    }

    private void WelcomeBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DismissWelcome();
    }
}
