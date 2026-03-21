using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Coclico.Models;
using Coclico.Services;
using Coclico.Views;

namespace Coclico.ViewModels
{
    public class FlowChainsViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly FlowChainService _flowChainService = new();
        private readonly FlowChainExecutionService _executionService = new();
        private readonly Dictionary<string, DispatcherTimer> _autoTimers = new();
        private readonly Dictionary<string, int> _hotkeyIds = new();

        private readonly Subject<string>    _searchSubject      = new();
        private readonly IDisposable        _searchSubscription;

        private FlowChain? _selectedChain;
        private FlowChain? _subscribedChain;
        private FlowItem? _selectedItem;
        private bool _isPropertiesOpen;
        private bool _isAddNodePickerOpen;
        private bool _isBusy;
        private string _busyMessage = string.Empty;

        public ObservableCollection<FlowChain> Chains { get; private set; }

        private int _insertAfterIndex = -1;

        public FlowChain? SelectedChain
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

        private void SubscribeChainItems(FlowChain? chain)
        {
            if (_subscribedChain != null)
                _subscribedChain.Items.CollectionChanged -= OnChainItemsChanged;
            _subscribedChain = chain;
            if (chain != null)
                chain.Items.CollectionChanged += OnChainItemsChanged;
        }

        private void OnChainItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => RebuildNodeViewItems();

        public FlowItem? SelectedItem
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

        private List<InstalledProgramsService.ProgramInfo> _allInstalledApps = [];
        private string _appPickerSearch = string.Empty;

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
                    .Where(a => a.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void LoadInstalledApps()
        {
            Task.Run(async () =>
            {
                var apps = await InstalledProgramsService.Instance.GetAllInstalledProgramsAsync();
                _allInstalledApps = apps.OrderBy(a => a.Name).ToList();
                System.Windows.Application.Current?.Dispatcher.Invoke(() => OnPropertyChanged(nameof(FilteredInstalledApps)));
            });
        }

        public ICommand AddChainCommand           { get; }
        public ICommand DeleteChainCommand        { get; }
        public ICommand RenameChainCommand        { get; }
        public ICommand ExecuteChainCommand       { get; }
        public ICommand ShowAddNodePickerCommand  { get; }
        public ICommand AddNodeAfterCommand       { get; }
        public ICommand AddNodeCommand            { get; }
        public ICommand DeleteItemCommand         { get; }
        public ICommand SelectItemCommand           { get; }
        public ICommand ClosePropertiesCommand      { get; }
        public ICommand CloseAddNodePickerCommand   { get; }
        public ICommand AddItemCommand              { get; }
        public ICommand SelectAppCommand            { get; }
        public ICommand ApplyTriggerCommand         { get; }

        public FlowChainsViewModel()
        {
            Chains = _flowChainService.GetFlowChains();
            _selectedChain = Chains.FirstOrDefault();

            Chains.CollectionChanged += (_, _) => { Save(); RefreshAllAutoTriggers(); };

            AddChainCommand           = new RelayCommand(_ => AddChain());
            DeleteChainCommand        = new RelayCommand(_ => DeleteChain(), _ => SelectedChain != null);
            RenameChainCommand        = new RelayCommand(p => RenameChain(p as string));
            ExecuteChainCommand       = new RelayCommandAsync(async _ => await ExecuteChain(), _ => SelectedChain != null && !IsBusy);
            ShowAddNodePickerCommand  = new RelayCommand(_ => { _insertAfterIndex = -1; IsAddNodePickerOpen = true; }, _ => SelectedChain != null);
            AddNodeAfterCommand       = new RelayCommand(p => { if (p is int idx) { _insertAfterIndex = idx; IsAddNodePickerOpen = true; } }, _ => SelectedChain != null);
            AddNodeCommand            = new RelayCommand(p => AddNode(p));
            DeleteItemCommand         = new RelayCommand(p => DeleteItem(p as FlowItem ?? SelectedItem));
            SelectItemCommand         = new RelayCommand(p => { if (p is FlowItem fi) SelectedItem = fi; });
            ClosePropertiesCommand    = new RelayCommand(_ => IsPropertiesOpen = false);
            CloseAddNodePickerCommand = new RelayCommand(_ => IsAddNodePickerOpen = false);
            AddItemCommand            = new RelayCommand(p => AddItemFromLibrary(p));
            ApplyTriggerCommand       = new RelayCommand(_ => { if (SelectedChain != null) { Save(); RefreshChainAutoTrigger(SelectedChain); } }, _ => SelectedChain != null);
            SelectAppCommand          = new RelayCommand(p =>
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

            SubscribeChainItems(_selectedChain);
            RebuildNodeViewItems();
            LoadInstalledApps();
            RefreshAllAutoTriggers();

            _searchSubscription = _searchSubject
                .Throttle(TimeSpan.FromMilliseconds(150), Scheduler.Default)
                .ObserveOn(DispatcherScheduler.Current)
                .Subscribe(_ => OnPropertyChanged(nameof(FilteredInstalledApps)));
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

        private void RefreshChainAutoTrigger(FlowChain chain)
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
                    try { await _executionService.ExecuteChainAsync(chain); }
                    catch { }
                }
            };
            timer.Start();
            _autoTimers[chain.Id] = timer;
        }

        private void AddChain()
        {
            var chain = new FlowChain { Name = $"Chaîne {Chains.Count + 1}" };
            chain.Items.Add(new FlowItem { Name = "Départ",  NodeType = NodeType.Start, X = 40,  Y = 120, Width = 140, Height = 64 });
            chain.Items.Add(new FlowItem { Name = "Fin",     NodeType = NodeType.End,   X = 380, Y = 120, Width = 140, Height = 64 });
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
            else if (param is string s && System.Enum.TryParse<NodeType>(s, out var parsed)) type = parsed;

            var endNode = SelectedChain.Items.LastOrDefault(i => i.NodeType == NodeType.End);
            int insertIdx;
            if (_insertAfterIndex >= 0 && _insertAfterIndex < SelectedChain.Items.Count)
                insertIdx = _insertAfterIndex + 1;
            else
                insertIdx = endNode != null ? SelectedChain.Items.IndexOf(endNode) : SelectedChain.Items.Count;
            _insertAfterIndex = -1;

            double lastX = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.X + i.Width).DefaultIfEmpty(40).Max();
            double avgY  = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.Y).DefaultIfEmpty(120).Average();

            string defaultName = type switch
            {
                NodeType.OpenApp          => "Ouvrir application",
                NodeType.CloseApp         => "Fermer application",
                NodeType.RunCommand       => "Commande",
                NodeType.KillProcess      => "Tuer processus",
                NodeType.Delay            => "Délai",
                NodeType.SendKeys         => "Envoyer touches",
                NodeType.TriggerShortcut  => "Raccourci de déclenchement",
                _                         => "Action"
            };

            var item = new FlowItem
            {
                Name       = defaultName,
                NodeType   = type,
                X          = lastX + 32,
                Y          = avgY,
                Width      = 160,
                Height     = 72
            };

            SelectedChain.Items.Insert(insertIdx, item);

            if (endNode != null)
            {
                var prevConn = SelectedChain.Connections.FirstOrDefault(c => c.EndItem == endNode);
                if (prevConn != null)
                {
                    SelectedChain.Connections.Add(new FlowConnection { StartItem = prevConn.StartItem, EndItem = item });
                    SelectedChain.Connections.Remove(prevConn);
                }
                else if (SelectedChain.Items.Count >= 2)
                {
                    var prevItem = SelectedChain.Items.ElementAtOrDefault(insertIdx - 1);
                    if (prevItem != null) SelectedChain.Connections.Add(new FlowConnection { StartItem = prevItem, EndItem = item });
                }
                SelectedChain.Connections.Add(new FlowConnection { StartItem = item, EndItem = endNode });
            }

            if (endNode != null) endNode.X = item.X + item.Width + 32;

            IsAddNodePickerOpen = false;
            SelectedItem = item;
            Save();
        }

        private void DeleteItem(FlowItem? item)
        {
            if (item == null || SelectedChain == null) return;
            if (item.NodeType == NodeType.Start || item.NodeType == NodeType.End) return;

            var inConn  = SelectedChain.Connections.FirstOrDefault(c => c.EndItem == item);
            var outConn = SelectedChain.Connections.FirstOrDefault(c => c.StartItem == item);

            if (inConn != null && outConn != null)
                SelectedChain.Connections.Add(new FlowConnection { StartItem = inConn.StartItem, EndItem = outConn.EndItem });

            if (inConn  != null) SelectedChain.Connections.Remove(inConn);
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

            var item = new FlowItem { Name = name, NodeType = NodeType.OpenApp, ProgramPath = path };
            var endNode = SelectedChain.Items.LastOrDefault(i => i.NodeType == NodeType.End);
            int idx = endNode != null ? SelectedChain.Items.IndexOf(endNode) : SelectedChain.Items.Count;
            double lastX = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.X + i.Width).DefaultIfEmpty(40).Max();
            double avgY  = SelectedChain.Items.Where(i => i.NodeType != NodeType.End).Select(i => i.Y).DefaultIfEmpty(120).Average();
            item.X = lastX + 32; item.Y = avgY; item.Width = 160; item.Height = 72;
            SelectedChain.Items.Insert(idx, item);
            if (endNode != null) { SelectedChain.Connections.Add(new FlowConnection { StartItem = item, EndItem = endNode }); endNode.X = item.X + item.Width + 32; }
            SelectedItem = item;
            Save();
        }

        private async Task ExecuteChain()
        {
            if (SelectedChain == null) return;
            IsBusy = true; BusyMessage = "Exécution en cours...";
            try { await _executionService.ExecuteChainAsync(SelectedChain); }
            finally { IsBusy = false; BusyMessage = string.Empty; }
        }

        private void Save() => _flowChainService.SaveFlowChains(Chains);

        public ObservableCollection<object> NodeViewItems { get; } = new();

        private void RebuildNodeViewItems()
        {
            NodeViewItems.Clear();
            if (SelectedChain == null) return;
            var items = SelectedChain.Items;
            for (int i = 0; i < items.Count; i++)
            {
                NodeViewItems.Add(items[i]);
                if (items[i].NodeType != NodeType.End)
                    NodeViewItems.Add(new FlowInsertMarker { AfterIndex = i });
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
}
