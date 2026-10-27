using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Coclico;

public partial class MainWindow
{
    public void ToggleDropdownMenu(bool? forceOpen = null)
    {
        bool target = forceOpen ?? !_isDropdownOpen;
        if (target == _isDropdownOpen)
        {
            return;
        }

        _isDropdownOpen = target;

        if (_isDropdownOpen)
        {
            ArchitecturalDropdownOverlay.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(-Motion.DropdownOpenDistance, 0, Motion.DropdownSlideOpen) { EasingFunction = ease };
            var fade = new DoubleAnimation(0, 1, Motion.DropdownFadeOpen) { EasingFunction = ease };
            DropdownTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            ArchitecturalDropdownOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, -Motion.DropdownCloseDistance, Motion.DropdownSlideClose) { EasingFunction = ease };
            var fade = new DoubleAnimation(1, 0, Motion.DropdownFadeClose) { EasingFunction = ease };
            fade.Completed += (s, e) =>
            {
                if (!_isDropdownOpen)
                {
                    ArchitecturalDropdownOverlay.Visibility = Visibility.Collapsed;
                }
            };
            DropdownTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            ArchitecturalDropdownOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    private void BtnArchitecturalMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_isNotificationsOpen)
        {
            ToggleNotificationsPanel(false);
        }

        ToggleDropdownMenu();
    }

    private void DropdownBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ToggleDropdownMenu(false);
    }

    private void DropdownClose_Click(object sender, RoutedEventArgs e)
    {
        ToggleDropdownMenu(false);
    }

    private void DropdownItem_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string tag)
        {
            ToggleDropdownMenu(false);
            NavigateTo(tag);
        }
    }
}
