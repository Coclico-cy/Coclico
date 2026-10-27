namespace Coclico.Services;

public interface IDialogService
{
    void ShowMessage(string message, string title = "Coclico", System.Windows.Window? owner = null);

    bool ShowConfirm(string message, string title = "Coclico", System.Windows.Window? owner = null);

    string? ShowOpenFileDialog(string title, string filter);

    string? ShowSaveFileDialog(string title, string filter, string defaultExt);

    string? ShowFolderDialog(string title);

    bool? ShowDialog<TDialog>(object? viewModel = null) where TDialog : System.Windows.Window, new();
}
