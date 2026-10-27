using System.Windows;
using System.Windows.Controls;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class DiagnosticView : UserControl
{
    private readonly DiagnosticViewModel? _viewModel;

    public DiagnosticView()
    {
        InitializeComponent();

        if (ServiceContainer.GetOptional<DiagnosticViewModel>() is { } vm)
        {
            _viewModel = vm;
            DataContext = vm;
            vm.ActionRequested += OnActionRequested;
            vm.OnViewLoaded();
        }
    }

    private void OnActionRequested(string navigationTag)
    {
        try
        {
            if (Application.Current?.MainWindow is MainWindow main)
            {
                main.NavigateTo(navigationTag);
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "DiagnosticView.OnActionRequested");
        }
    }

    private void OnFindingAction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DiagnosticFindingRow row })
        {
            _viewModel?.InvokeAction(row);
        }
    }
}
