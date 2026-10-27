using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class HealthDefenseView : UserControl
{
    public HealthDefenseView()
    {
        InitializeComponent();
        SystemHealthService healthService = ServiceContainer.GetRequired<SystemHealthService>();
        var vm = new HealthDefenseViewModel(healthService);
        DataContext = vm;

        vm.OutputLog.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && ConsoleListBox.Items.Count > 0)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (ConsoleListBox.Items.Count > 0)
                    {
                        ConsoleListBox.ScrollIntoView(ConsoleListBox.Items[^1]);
                    }
                }, DispatcherPriority.Background);
            }
        };
    }
}
