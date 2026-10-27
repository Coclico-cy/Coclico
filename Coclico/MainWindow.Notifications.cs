using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Services;

namespace Coclico;

public partial class MainWindow
{
    private bool _isNotificationsOpen;

    private NotificationService? _notificationService;

    public void ToggleNotificationsPanel(bool? forceOpen = null)
    {
        bool target = forceOpen ?? !_isNotificationsOpen;
        if (target == _isNotificationsOpen)
        {
            return;
        }

        _isNotificationsOpen = target;

        if (_isNotificationsOpen)
        {
            if (_isDropdownOpen)
            {
                ToggleDropdownMenu(false);
            }

            _notificationService?.MarkAllRead();
            UpdateNotificationsEmptyState();

            NotificationsOverlay.Visibility = Visibility.Visible;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new DoubleAnimation(-Motion.DropdownOpenDistance, 0, Motion.DropdownSlideOpen) { EasingFunction = ease };
            var fade = new DoubleAnimation(0, 1, Motion.DropdownFadeOpen) { EasingFunction = ease };
            NotificationsTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            NotificationsOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var slide = new DoubleAnimation(0, -Motion.DropdownCloseDistance, Motion.DropdownSlideClose) { EasingFunction = ease };
            var fade = new DoubleAnimation(1, 0, Motion.DropdownFadeClose) { EasingFunction = ease };
            fade.Completed += (s, e) =>
            {
                if (!_isNotificationsOpen)
                {
                    NotificationsOverlay.Visibility = Visibility.Collapsed;
                }
            };
            NotificationsTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
            NotificationsOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    private void InitializeNotifications()
    {
        try
        {
            _notificationService = ServiceContainer.GetRequired<NotificationService>();
            NotificationsList.ItemsSource = _notificationService.History;
            _notificationService.UnreadCountChanged += count =>
                _ = Dispatcher.InvokeAsync(() => UpdateNotificationBadge(count));
            _notificationService.HistoryChanged += () =>
                _ = Dispatcher.InvokeAsync(UpdateNotificationsEmptyState);
            UpdateNotificationBadge(_notificationService.UnreadCount);
            UpdateNotificationsEmptyState();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "MainWindow.NotificationsInit");
        }
    }

    private void UpdateNotificationBadge(int count)
    {
        if (count > 0)
        {
            NotificationsBadgeText.Text = count > 99 ? "99+" : count.ToString();
            NotificationsBadgeBorder.Visibility = Visibility.Visible;
        }
        else
        {
            NotificationsBadgeText.Text = string.Empty;
            NotificationsBadgeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateNotificationsEmptyState()
    {
        NotificationsEmptyText.Visibility = _notificationService is { History.Count: 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;
        NotificationsList.Visibility = _notificationService is { History.Count: 0 }
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnNotifications_Click(object sender, RoutedEventArgs e)
    {
        ToggleNotificationsPanel();
    }

    private void NotificationsBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ToggleNotificationsPanel(false);
    }

    private void NotificationsClose_Click(object sender, RoutedEventArgs e)
    {
        ToggleNotificationsPanel(false);
    }

    private void NotificationsClear_Click(object sender, RoutedEventArgs e)
    {
        _notificationService?.Clear();
        UpdateNotificationsEmptyState();
    }
}
