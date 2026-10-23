using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class NetworkOptimizerView : UserControl
{
    public NetworkOptimizerViewModel ViewModel { get; }

    public NetworkOptimizerView()
    {
        InitializeComponent();
        ViewModel = ServiceContainer.GetOptional<NetworkOptimizerViewModel>() ?? new NetworkOptimizerViewModel();
        DataContext = ViewModel;

        ((INotifyCollectionChanged)ViewModel.OutputLog).CollectionChanged += (_, _) =>
        {
            if (OutputListBox.Dispatcher.CheckAccess())
            {
                ScrollLogToEnd();
            }
            else
            {
                _ = OutputListBox.Dispatcher.InvokeAsync(ScrollLogToEnd);
            }
        };

        Loaded += (_, _) =>
        {
            ServiceContainer.GetOptional<NetworkMonitorService>()?.SetConsumerVisible(true);
        };

        Unloaded += (_, _) =>
        {
            ServiceContainer.GetOptional<NetworkMonitorService>()?.SetConsumerVisible(false);
        };
    }

    private void ScrollLogToEnd()
    {
        if (OutputListBox.Items.Count > 0)
        {
            OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);
        }
    }
}
