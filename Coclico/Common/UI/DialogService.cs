using System.Windows;
using Microsoft.Win32;

namespace Coclico.Services;

public sealed class DialogService : IDialogService
{
    public void ShowMessage(string message, string title = "Coclico", Window? owner = null)
    {
        _ = ResolveOwner(owner) is Window ownerWindow
            ? MessageBox.Show(ownerWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information)
            : MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowConfirm(string message, string title = "Coclico", Window? owner = null)
    {
        MessageBoxResult result = ResolveOwner(owner) is Window ownerWindow
            ? MessageBox.Show(ownerWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public string? ShowOpenFileDialog(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowSaveFileDialog(string title, string filter, string defaultExt)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, DefaultExt = defaultExt };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowFolderDialog(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public bool? ShowDialog<TDialog>(object? viewModel = null) where TDialog : Window, new()
    {
        var dialog = new TDialog();
        if (viewModel != null)
        {
            dialog.DataContext = viewModel;
        }

        dialog.Owner = Application.Current?.MainWindow;
        return dialog.ShowDialog();
    }

    private static Window? ResolveOwner(Window? owner)
    {
        return owner ?? Application.Current?.MainWindow;
    }
}
