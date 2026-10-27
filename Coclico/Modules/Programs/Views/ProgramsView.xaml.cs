using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coclico.Services;
using Coclico.ViewModels;
using Microsoft.Win32;

namespace Coclico.Views;

public partial class ProgramsView : UserControl
{
    public ProgramsViewModel ViewModel { get; }

    public ProgramsView()
    {
        InitializeComponent();

        ViewModel = ServiceContainer.GetOptional<ProgramsViewModel>() ?? new ProgramsViewModel();
        DataContext = ViewModel;

        ViewModel.Confirm = (message, title) => MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        ViewModel.Notify = (message, title) => _ = title == null
            ? MessageBox.Show(message)
            : MessageBox.Show(message, title);

        ViewModel.PickExecutable = () =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Sélectionner une application"
            };

            return dialog.ShowDialog() is true ? dialog.FileName : null;
        };

        ViewModel.BeginEditName = () =>
        {
            _ = TxtProgramName.Focus();
            TxtProgramName.SelectAll();
        };

        Unloaded += (_, _) => ViewModel.Detach();
    }

    private void ProgramItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is ProgramDisplayInfo app)
        {
            ViewModel.ProgramClicked(app, e.ClickCount);
        }
    }
}
