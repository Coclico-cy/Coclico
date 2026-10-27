using System.Collections.ObjectModel;
using System.Windows;

namespace Coclico.Services;

public sealed class NotificationEntry
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public DateTime Timestamp { get; init; }
}

public sealed class NotificationService
{
    private const int MaxHistoryEntries = 50;

    private int _unreadCount;

    public event Action<int>? UnreadCountChanged;

    public event Action? HistoryChanged;

    public ObservableCollection<NotificationEntry> History { get; } = [];

    public int UnreadCount => _unreadCount;

    public void Publish(string message, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string resolvedTitle = string.IsNullOrWhiteSpace(title) ? "Coclico" : title;
        ToastService.ShowNotification(resolvedTitle, message);
    }

    public void Record(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        NotificationEntry entry = new()
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Coclico" : title,
            Message = message,
            Timestamp = DateTime.Now
        };

        RunOnUi(() =>
        {
            History.Insert(0, entry);
            while (History.Count > MaxHistoryEntries)
            {
                History.RemoveAt(History.Count - 1);
            }

            _unreadCount++;
            UnreadCountChanged?.Invoke(_unreadCount);
            HistoryChanged?.Invoke();
        });
    }

    public void MarkAllRead()
    {
        RunOnUi(() =>
        {
            if (_unreadCount == 0)
            {
                return;
            }

            _unreadCount = 0;
            UnreadCountChanged?.Invoke(0);
        });
    }

    public void Clear()
    {
        RunOnUi(() =>
        {
            History.Clear();
            _unreadCount = 0;
            UnreadCountChanged?.Invoke(0);
            HistoryChanged?.Invoke();
        });
    }

    private static void RunOnUi(Action action)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = app.Dispatcher.InvokeAsync(action);
        }
    }
}
