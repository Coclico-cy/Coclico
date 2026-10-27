using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Coclico.Modules.Apps.Services;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Modules.Apps.Views;

public partial class AppsView : UserControl
{
    private readonly AppsViewModel? _subscribedVm;
    private readonly NotifyCollectionChangedEventHandler? _logHandler;
    private bool _isSubscribed;

    public AppsViewModel? ViewModel => DataContext as AppsViewModel;

    public AppsView()
    {
        InitializeComponent();
        AppsViewModel vm = ServiceContainer.GetOptional<AppsViewModel>()
            ?? new AppsViewModel(
                ServiceContainer.GetRequired<AppCatalogService>(),
                ServiceContainer.GetRequired<WingetEngineService>(),
                ServiceContainer.GetRequired<GitHubToolService>(),
                ServiceContainer.GetRequired<AppShortcutsService>());
        DataContext = vm;

        vm.Confirm = (message, title) => MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        vm.Notify = (message, title) =>
            ServiceContainer.GetOptional<NotificationService>()?.Publish(message, title);

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

    private void OnlineQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AppsViewModel vm)
        {
            vm.OnlineSearchCommand.Execute(null);
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
}
