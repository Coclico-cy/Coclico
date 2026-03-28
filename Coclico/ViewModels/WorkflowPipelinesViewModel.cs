#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Coclico.Models;
using Coclico.Services;
using Coclico.Views;

namespace Coclico.ViewModels;

public class WorkflowPipelinesViewModel : INotifyPropertyChanged, IDisposable
{
    public enum RuleType
    {
        [Description("On A Launch -> Launch B")]
        OnLaunchThenLaunch,
        [Description("On A Launch -> Kill B")]
        OnLaunchThenKill,
        [Description("Custom Shortcut -> Launch Both")]
        Shortcut
    }

    public class VisualPipelineStep : INotifyPropertyChanged
    {
        private double _x;
        private double _y;

        public InstalledProgramsService.ProgramInfo Program { get; }
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); OnPropertyChanged(nameof(CenterY)); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); OnPropertyChanged(nameof(CenterY)); }
        }

        public double Width { get; set; } = 150;
        public double Height { get; set; } = 60;
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
        public string Name => Program.Name;

        public VisualPipelineStep(InstalledProgramsService.ProgramInfo program)
        {
            Program = program;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class VisualPipelineConnection(VisualPipelineStep start, VisualPipelineStep end, RuleType rule)
    {
        public VisualPipelineStep StartItem { get; } = start;
        public VisualPipelineStep EndItem { get; } = end;
        public RuleType Rule { get; set; } = rule;
    }

    public ObservableCollection<VisualPipelineStep> VisualItems { get; } = new();
    public ObservableCollection<VisualPipelineConnection> VisualConnections { get; } = new();
    public ICommand SelectItemForConnectionCommand { get; }

    private VisualPipelineStep? _firstSelectedItemForConnection;


    private readonly WorkflowService _workflowService = new();
    private readonly Dictionary<string, DispatcherTimer> _autoTimers = new();
    private readonly Dictionary<string, int> _hotkeyIds = new();
    private readonly Subject<string> _searchSubject = new();
    private readonly IDisposable _searchSubscription;

    private WorkflowPipeline? _selectedChain;
    private WorkflowPipeline? _subscribedChain;
    private PipelineStep? _selectedItem;
    private bool _isPropertiesOpen;
    private bool _isAddNodePickerOpen;
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private int _insertAfterIndex = -1;
    private List<InstalledProgramsService.ProgramInfo> _allInstalledApps = [];
    private string _appPickerSearch = string.Empty;

    public ObservableCollection<WorkflowPipeline> Chains { get; private set; }

    public WorkflowPipeline? SelectedChain
    {
        get => _selectedChain;
        set
        {
            _selectedChain = value;
            SelectedItem = null;
            SubscribeChainItems(value);
            RebuildNodeViewItems();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedChain));
        }
    }

    public PipelineStep? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            IsPropertiesOpen = value != null && value.NodeType != NodeType.Start && value.NodeType != NodeType.End;
            OnPropertyChanged();
        }
    }

    public bool IsPropertiesOpen
    {
        get => _isPropertiesOpen;
        set { _isPropertiesOpen = value; OnPropertyChanged(); }
    }

    public bool IsAddNodePickerOpen
    {
        get => _isAddNodePickerOpen;
        set { _isAddNodePickerOpen = value; OnPropertyChanged(); }
    }

    public bool IsCompact { get; } = false;

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        set { _busyMessage = value; OnPropertyChanged(); }
    }

    public bool HasSelectedChain => SelectedChain != null;

    public string AppPickerSearch
    {
        get => _appPickerSearch;
        set
        {
            _appPickerSearch = value;
            OnPropertyChanged();
            _searchSubject.OnNext(value);
        }
    }

    public IEnumerable<InstalledProgramsService.ProgramInfo> FilteredInstalledApps
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_appPickerSearch))
                return _allInstalledApps;
            var q = _appPickerSearch.Trim();
            return _allInstalledApps
                .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public ICommand AddChainCommand { get; }
    public ICommand DeleteChainCommand { get; }
    public ICommand RenameChainCommand { get; }
    public ICommand ExecuteChainCommand { get; }
    public ICommand ShowAddNodePickerCommand { get; }
    public ICommand AddNodeAfterCommand { get; }
    public ICommand AddNodeCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand ClosePropertiesCommand { get; }
    public ICommand CloseAddNodePickerCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand SelectAppCommand { get; }
    public ICommand ApplyTriggerCommand { get; }
    public ObservableCollection<object> NodeViewItems { get; } = new();

    public WorkflowPipelinesViewModel()
    {
        Chains = new ObservableCollection<WorkflowPipeline>();
        Chains.CollectionChanged += (_, _) => { Save(); RefreshAllAutoTriggers(); };

        AddChainCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => AddChain());
        DeleteChainCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => DeleteChain(), _ => SelectedChain != null);
        RenameChainCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => RenameChain(p as string));
        ExecuteChainCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<object?>(async _ => await ExecuteChain(), _ => SelectedChain != null && !IsBusy);
        ShowAddNodePickerCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => { _insertAfterIndex = -1; IsAddNodePickerOpen = true; }, _ => SelectedChain != null);
        AddNodeAfterCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => { if (p is int idx) { _insertAfterIndex = idx; IsAddNodePickerOpen = true; } }, _ => SelectedChain != null);
        AddNodeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => AddNode(p));
        DeleteItemCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => DeleteItem(p as PipelineStep ?? SelectedItem));
        SelectItemCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => { if (p is PipelineStep fi) SelectedItem = fi; });
        ClosePropertiesCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsPropertiesOpen = false);
        CloseAddNodePickerCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => IsAddNodePickerOpen = false);
        AddItemCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p => AddItemFromLibrary(p));
        ApplyTriggerCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => { if (SelectedChain != null) { Save(); RefreshChainAutoTrigger(SelectedChain); } }, _ => SelectedChain != null);
        SelectAppCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(p =>
        {
            if (p is InstalledProgramsService.ProgramInfo app && SelectedItem != null)
            {
                if (SelectedItem.NodeType == NodeType.KillProcess)
                {
                    SelectedItem.ProcessName = System.IO.Path.GetFileNameWithoutExtension(app.ExePath);
                    SelectedItem.Name = $"Tuer: {app.Name}";
                }
                else
                {
                    SelectedItem.ProgramPath = app.ExePath;
                    SelectedItem.Name = string.IsNullOrWhiteSpace(app.Name) ? SelectedItem.Name : app.Name;
                }
                AppPickerSearch = string.Empty;
            }
        });

        SelectItemForConnectionCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<object?>(
            async item => await SelectVisualItemForConnectionAsync(item));

        _searchSubscription = _searchSubject
            .Throttle(TimeSpan.FromMilliseconds(150), Scheduler.Default)
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(_ => OnPropertyChanged(nameof(FilteredInstalledApps)));

        LoadChainsAsync();
        LoadInstalledApps();
        _ = LoadVisualProgramsAsync();
    }

    private async Task SelectVisualItemForConnectionAsync(object? item)
    {
        if (item is not VisualPipelineStep selectedItem) return;

        if (_firstSelectedItemForConnection == null)
        {
            _firstSelectedItemForConnection = selectedItem;
        }
        else
        {
            if (_firstSelectedItemForConnection == selectedItem) return;

            bool connectionExists = VisualConnections.Any(c =>
                (c.StartItem == _firstSelectedItemForConnection && c.EndItem == selectedItem) ||
                (c.StartItem == selectedItem && c.EndItem == _firstSelectedItemForConnection));

            if (!connectionExists)
            {
                var dialog = new Views.RuleSelectionDialog();
                var window = new Window
                {
                    Content = dialog,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    MinWidth = 400,
                    MinHeight = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E2E")!),
                    ResizeMode = ResizeMode.NoResize,
                    Title = string.Empty
                };

                if (Application.Current.MainWindow is Window mainWindow)
                    window.Owner = mainWindow;

                if (window.ShowDialog() == true && dialog.SelectedRule is RuleType selectedRule)
                    VisualConnections.Add(new VisualPipelineConnection(_firstSelectedItemForConnection, selectedItem, selectedRule));
            }

            _firstSelectedItemForConnection = null;
        }
    }

    private async Task LoadVisualProgramsAsync()
    {
        var programs = await ServiceContainer.GetRequired<InstalledProgramsService>().GetAllInstalledProgramsAsync()
            .ConfigureAwait(false);

        double currentX = 50;
        double currentY = 50;
        const double cardWidth = 150;
        const double cardHeight = 60;
        const double padding = 20;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var prog in programs)
            {
                VisualItems.Add(new VisualPipelineStep(prog)
                {
                    X = currentX,
                    Y = currentY,
                    Width = cardWidth,
                    Height = cardHeight
                });

                currentX += cardWidth + padding;
                if (currentX + cardWidth > 800)
                {
                    currentX = 50;
                    currentY += cardHeight + padding;
                }
            }
        });
    }

    private void SubscribeChainItems(WorkflowPipeline? chain)
    {
        if (_subscribedChain != null)
            _subscribedChain.Items.CollectionChanged -= OnChainItemsChanged;
        _subscribedChain = chain;
        if (chain != null)
            chain.Items.CollectionChanged += OnChainItemsChanged;
    }

    private void OnChainItemsChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RebuildNodeViewItems();

    private void LoadInstalledApps()
    {
        Task.Run(async () =>
        {
            var apps = await ServiceContainer.GetRequired<InstalledProgramsService>().GetAllInstalledProgramsAsync();
            _allInstalledApps = apps.OrderBy(a => a.Name).ToList();
            Application.Current?.Dispatcher.Invoke(() => OnPropertyChanged(nameof(FilteredInstalledApps)));
        });
    }

    private async void LoadChainsAsync()
    {
        try
        {
            var loaded = await _workflowService.GetWorkflowPipelinesAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var chain in loaded)
                    Chains.Add(chain);
                _selectedChain = Chains.FirstOrDefault();
                SubscribeChainItems(_selectedChain);
                RebuildNodeViewItems();
                OnPropertyChanged(nameof(SelectedChain));
                OnPropertyChanged(nameof(HasSelectedChain));
                RefreshAllAutoTriggers();
            });
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "WorkflowPipelinesViewModel.LoadChainsAsync");
        }
    }

    private void RefreshAllAutoTriggers()
    {
        foreach (var chain in Chains)
            RefreshChainAutoTrigger(chain);
        var activeIds = Chains.Select(c => c.Id).ToHashSet();
        foreach (var key in _autoTimers.Keys.Where(k => !activeIds.Contains(k)).ToList())
        {
            _autoTimers[key].Stop();
            _autoTimers.Remove(key);
        }
    }

    private void RefreshChainAutoTrigger(WorkflowPipeline chain)
    {
        if (_autoTimers.TryGetValue(chain.Id, out var existing))
        {
            existing.Stop();
            _autoTimers.Remove(chain.Id);
        }

        if (!chain.AutoTriggerEnabled) return;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, chain.AutoTriggerIntervalSec))
        };
        timer.Tick += async (_, _) =>
        {
            if (!chain.IsRunning)
            {
                try { await _workflowService.ExecuteChainAsync(chain); }
                catch { }
            }
        };
        timer.Start();
        _autoTimers[chain.Id] = timer;
    }

    private void AddChain()
    {
        var chain = new WorkflowPipeline { Name = $"Chaîne {Chains.Count + 1}" };
        chain.Items.Add(new PipelineStep { Name = "Départ", NodeType = NodeType.Start, X = 40, Y = 120, Width = 140, Height = 64 });
        chain.Items.Add(new PipelineStep { Name = "Fin", NodeType = NodeType.End, X = 380, Y = 120, Width = 140, Height = 64 });
        Chains.Add(chain);
        SelectedChain = chain;
    }

    private void DeleteChain()
    {
        if (SelectedChain == null) return;
        Chains.Remove(SelectedChain);
        SelectedChain = Chains.FirstOrDefault();
    }

    private void RenameChain(string? newName)
    {
        if (SelectedChain != null && !string.IsNullOrWhiteSpace(newName))
        {
            SelectedChain.Name = newName;
            Save();
        }
    }

    private void AddNode(object? param)
    {
        if (SelectedChain == null) return;

        NodeType type = NodeType.OpenApp;
        if (param is NodeType nt) type = nt;
        else if (param is string s && Enum.TryParse<NodeType>(s, out var parsed)) type = parsed;

        var endNode = SelectedChain.Items.LastOrDefault(i => i.NodeType == NodeType.End);
        int insertIdx;
        if (_insertAfterIndex >= 0 && _insertAfterIndex < SelectedChain.Items.Count)
            insertIdx = _insertAfterIndex + 1;
        else
            insertIdx = endNode != null ? SelectedChain.Items.IndexOf(endNode) : SelectedChain.Items.Count;
        _insertAfterIndex = -1;

        double lastX = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.X + i.Width).DefaultIfEmpty(40).Max();
        double avgY = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.Y).DefaultIfEmpty(120).Average();

        string defaultName = type switch
        {
            NodeType.OpenApp => "Ouvrir application",
            NodeType.CloseApp => "Fermer application",
            NodeType.RunCommand => "Commande",
            NodeType.KillProcess => "Tuer processus",
            NodeType.Delay => "Délai",
            NodeType.SendKeys => "Envoyer touches",
            NodeType.TriggerShortcut => "Raccourci de déclenchement",
            _ => "Action"
        };

        var item = new PipelineStep
        {
            Name = defaultName,
            NodeType = type,
            X = lastX + 32,
            Y = avgY,
            Width = 160,
            Height = 72
        };

        SelectedChain.Items.Insert(insertIdx, item);

        if (endNode != null)
        {
            var prevConn = SelectedChain.Connections.FirstOrDefault(c => c.EndItem == endNode);
            if (prevConn != null)
            {
                SelectedChain.Connections.Add(new PipelineConnection { StartItem = prevConn.StartItem, EndItem = item });
                SelectedChain.Connections.Remove(prevConn);
            }
            else if (SelectedChain.Items.Count >= 2)
            {
                var prevItem = SelectedChain.Items.ElementAtOrDefault(insertIdx - 1);
                if (prevItem != null) SelectedChain.Connections.Add(new PipelineConnection { StartItem = prevItem, EndItem = item });
            }
            SelectedChain.Connections.Add(new PipelineConnection { StartItem = item, EndItem = endNode });
        }

        if (endNode != null) endNode.X = item.X + item.Width + 32;

        IsAddNodePickerOpen = false;
        SelectedItem = item;
        Save();
    }

    private void DeleteItem(PipelineStep? item)
    {
        if (item == null || SelectedChain == null) return;
        if (item.NodeType == NodeType.Start || item.NodeType == NodeType.End) return;

        var inConn = SelectedChain.Connections.FirstOrDefault(c => c.EndItem == item);
        var outConn = SelectedChain.Connections.FirstOrDefault(c => c.StartItem == item);

        if (inConn != null && outConn != null)
            SelectedChain.Connections.Add(new PipelineConnection { StartItem = inConn.StartItem, EndItem = outConn.EndItem });

        if (inConn != null) SelectedChain.Connections.Remove(inConn);
        if (outConn != null) SelectedChain.Connections.Remove(outConn);

        SelectedChain.Items.Remove(item);
        if (SelectedItem == item) SelectedItem = null;
        Save();
    }

    private void AddItemFromLibrary(object? program)
    {
        if (SelectedChain == null) return;

        string? name = null, path = null;
        if (program is ProgramsView.ProgramDisplayInfo pd) { name = pd.Name; path = pd.ExePath; }
        else if (program is InstalledProgramsService.ProgramInfo pi) { name = pi.Name; path = pi.ExePath; }
        if (name == null) return;

        var item = new PipelineStep { Name = name, NodeType = NodeType.OpenApp, ProgramPath = path };
        var endNode = SelectedChain.Items.LastOrDefault(i => i.NodeType == NodeType.End);
        int idx = endNode != null ? SelectedChain.Items.IndexOf(endNode) : SelectedChain.Items.Count;
        double lastX = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.X + i.Width).DefaultIfEmpty(40).Max();
        double avgY = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.Y).DefaultIfEmpty(120).Average();
        item.X = lastX + 32; item.Y = avgY; item.Width = 160; item.Height = 72;
        SelectedChain.Items.Insert(idx, item);
        if (endNode != null) { SelectedChain.Connections.Add(new PipelineConnection { StartItem = item, EndItem = endNode }); endNode.X = item.X + item.Width + 32; }
        SelectedItem = item;
        Save();
    }

    private async Task ExecuteChain()
    {
        if (SelectedChain == null) return;
        IsBusy = true; BusyMessage = "Exécution en cours...";
        try { await _workflowService.ExecuteChainAsync(SelectedChain); }
        finally { IsBusy = false; BusyMessage = string.Empty; }
    }

    private void Save()
    {
        var snapshot = new ObservableCollection<WorkflowPipeline>(Chains);
        _ = Task.Run(() => _workflowService.SaveWorkflowPipelinesAsync(snapshot));
    }

    private void RebuildNodeViewItems()
    {
        NodeViewItems.Clear();
        if (SelectedChain == null) return;
        var items = SelectedChain.Items;
        for (int i = 0; i < items.Count; i++)
        {
            NodeViewItems.Add(items[i]);
            if (items[i].NodeType != NodeType.End)
                NodeViewItems.Add(new PipelineInsertMarker { AfterIndex = i });
        }
    }

    public void Dispose()
    {
        _searchSubscription.Dispose();
        _searchSubject.Dispose();
        foreach (var t in _autoTimers.Values) t.Stop();
        _autoTimers.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
