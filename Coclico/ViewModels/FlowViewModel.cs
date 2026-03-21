using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Coclico.Services;
using Coclico.Views;

namespace Coclico.ViewModels
{
    public class FlowViewModel : INotifyPropertyChanged
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

        public class FlowItem : INotifyPropertyChanged
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

            public FlowItem(InstalledProgramsService.ProgramInfo program)
            {
                Program = program;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class FlowConnection
        {
            public FlowItem StartItem { get; }
            public FlowItem EndItem { get; }
            public RuleType Rule { get; set; }

            public FlowConnection(FlowItem start, FlowItem end, RuleType rule)
            {
                StartItem = start;
                EndItem = end;
                Rule = rule;
            }
        }

        public ObservableCollection<FlowItem> Items { get; } = new();
        public ObservableCollection<FlowConnection> Connections { get; } = new();

        public ICommand SelectItemForConnectionCommand { get; }
        private FlowItem? _firstSelectedItemForConnection;

        public FlowViewModel()
        {
            SelectItemForConnectionCommand = new RelayCommandAsync(async item => await SelectItemForConnection(item));
            _ = LoadProgramsAsync();
        }

        private async Task SelectItemForConnection(object? item)
        {
            if (item is not FlowItem selectedItem) return;

            if (_firstSelectedItemForConnection == null)
            {
                _firstSelectedItemForConnection = selectedItem;
            }
            else
            {
                if (_firstSelectedItemForConnection == selectedItem) return;

                bool connectionExists = Connections.Any(c =>
                    (c.StartItem == _firstSelectedItemForConnection && c.EndItem == selectedItem) ||
                    (c.StartItem == selectedItem && c.EndItem == _firstSelectedItemForConnection));

                if (!connectionExists)
                {
                    var dialog = new RuleSelectionDialog();
                    var window = new Window
                    {
                        Content = dialog,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        MinWidth = 400,
                        MinHeight = 200,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        WindowStyle = WindowStyle.ToolWindow,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E")!),
                        ResizeMode = ResizeMode.NoResize,
                        Title = ""
                    };

                    if (System.Windows.Application.Current.MainWindow is Window mainWindow)
                    {
                        window.Owner = mainWindow;
                    }

                    if (window.ShowDialog() == true && dialog.SelectedRule is RuleType selectedRule)
                    {
                        Connections.Add(new FlowConnection(_firstSelectedItemForConnection, selectedItem, selectedRule));
                    }
                }

                _firstSelectedItemForConnection = null;
            }
        }

        private async Task LoadProgramsAsync()
        {
            var programs = await InstalledProgramsService.Instance.GetAllInstalledProgramsAsync();

            double currentX = 50;
            double currentY = 50;
            const double cardWidth = 150;
            const double cardHeight = 60;
            const double padding = 20;

            foreach (var prog in programs)
            {
                Items.Add(new FlowItem(prog)
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
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
