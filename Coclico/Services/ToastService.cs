#nullable enable
using System;

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
        => _svc?.Show("Coclico", message, ControlAppearance.Success, null, TimeSpan.FromSeconds(3));

    public static void ShowError(string message)
        => _svc?.Show("Coclico", message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));

    public static void ShowInfo(string message)
        => _svc?.Show("Coclico", message, ControlAppearance.Secondary, null, TimeSpan.FromSeconds(3));
}
