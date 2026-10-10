using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class InstallerView : UserControl
{
    private InstallerViewModel? _subscribedVm;
    private NotifyCollectionChangedEventHandler? _logHandler;
    private bool _isSubscribed;

    public InstallerView()
    {
        InitializeComponent();
        InstallerViewModel vm = ServiceContainer.GetOptional<InstallerViewModel>()
            ?? new InstallerViewModel(ServiceContainer.GetRequired<InstallerService>());
        DataContext = vm;

        _logHandler = OnOutputLogChanged;
        vm.OutputLog.CollectionChanged += _logHandler;
        _subscribedVm = vm;
        _isSubscribed = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnOutputLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && OutputListBox.Items.Count > 0)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (OutputListBox.Items.Count > 0)
                {
                    OutputListBox.ScrollIntoView(OutputListBox.Items[^1]);
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm != null && _logHandler != null && !_isSubscribed)
        {
            _subscribedVm.OutputLog.CollectionChanged += _logHandler;
            _isSubscribed = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm != null && _logHandler != null && _isSubscribed)
        {
            _subscribedVm.OutputLog.CollectionChanged -= _logHandler;
            _isSubscribed = false;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InstallerViewModel vm)
        {
            _ = vm.SearchAsync();
        }
    }
}
