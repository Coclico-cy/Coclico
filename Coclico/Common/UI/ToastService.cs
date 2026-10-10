using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Coclico.Services;

public static class ToastService
{
    private static ISnackbarService? _svc;

    public static void Initialize(SnackbarPresenter presenter)
    {
        _svc = new SnackbarService();
        _svc.SetSnackbarPresenter(presenter);
    }

    public static void Show(string message)
    {
        ShowOnUi("Coclico", message, ControlAppearance.Success, TimeSpan.FromSeconds(3));
    }

    public static void ShowError(string message)
    {
        ShowOnUi("Coclico", message, ControlAppearance.Danger, TimeSpan.FromSeconds(4));
    }

    public static void ShowWarning(string message)
    {
        ShowOnUi("Coclico", message, ControlAppearance.Caution, TimeSpan.FromSeconds(4));
    }

    public static void ShowInfo(string message)
    {
        ShowOnUi("Coclico", message, ControlAppearance.Secondary, TimeSpan.FromSeconds(3));
    }

    private static void ShowOnUi(string title, string message, ControlAppearance appearance, TimeSpan duration)
    {
        ISnackbarService? svc = _svc;
        if (svc == null)
        {
            return;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
            svc.Show(title, message, appearance, null, duration));
    }
}