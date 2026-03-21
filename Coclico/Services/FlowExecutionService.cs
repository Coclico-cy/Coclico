using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace Coclico.Services
{
    public class FlowExecutionService
    {
        private static readonly Lazy<FlowExecutionService> _lazyInstance = new(() => new FlowExecutionService());
        public static FlowExecutionService Instance => _lazyInstance.Value;

        private readonly ProcessWatcherService _processWatcher;
        private ObservableCollection<ViewModels.FlowViewModel.FlowConnection>? _connections;

        private FlowExecutionService()
        {
            _processWatcher = ProcessWatcherService.Instance;
            _processWatcher.ProcessStarted += OnProcessStarted;
        }

        public void SetConnections(ObservableCollection<ViewModels.FlowViewModel.FlowConnection> connections)
        {
            _connections = connections;
        }

        private void OnProcessStarted(object? sender, string processName)
        {
            if (_connections == null) return;

            string cleanProcessName = processName.EndsWith(".exe") ? processName[..^4] : processName;

            var rulesToExecute = _connections.Where(c => 
                c.StartItem.Program.ExePath.EndsWith(processName, System.StringComparison.OrdinalIgnoreCase) ||
                c.StartItem.Program.Name.Equals(cleanProcessName, System.StringComparison.OrdinalIgnoreCase)
            ).ToList();

            foreach (var rule in rulesToExecute)
            {
                ExecuteRule(rule);
            }
        }

        private void ExecuteRule(ViewModels.FlowViewModel.FlowConnection connection)
        {
            switch (connection.Rule)
            {
                case ViewModels.FlowViewModel.RuleType.OnLaunchThenKill:
                    KillProcess(connection.EndItem);
                    break;
                case ViewModels.FlowViewModel.RuleType.OnLaunchThenLaunch:
                    LaunchProcess(connection.EndItem);
                    break;
            }
        }

        private void KillProcess(ViewModels.FlowViewModel.FlowItem item)
        {
            try
            {
                string processToKill = System.IO.Path.GetFileNameWithoutExtension(item.Program.ExePath);
                foreach (var process in Process.GetProcessesByName(processToKill))
                {
                    process.Kill();
                }
            }
            catch
            {
            }
        }

        private void LaunchProcess(ViewModels.FlowViewModel.FlowItem item)
        {
            try
            {
                if (!string.IsNullOrEmpty(item.Program.ExePath) && System.IO.File.Exists(item.Program.ExePath))
                {
                    Process.Start(item.Program.ExePath);
                }
            }
            catch
            {
            }
        }
    }
}
