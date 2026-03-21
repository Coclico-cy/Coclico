using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Coclico.Models;

namespace Coclico.Services
{
    public class FlowChainService
    {
        private string FlowChainsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "flow_chains.json");

        private string MigrationStampPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico", "flow_chains_v2.migrated");

        public ObservableCollection<FlowChain> GetFlowChains()
        {
            ApplyV2Migration();
            try
            {
                if (File.Exists(FlowChainsPath))
                {
                    string content = File.ReadAllText(FlowChainsPath);
                    return JsonSerializer.Deserialize<ObservableCollection<FlowChain>>(content) ?? GetDefaultFlowChains();
                }
            }
            catch { }
            return GetDefaultFlowChains();
        }

        private void ApplyV2Migration()
        {
            if (File.Exists(MigrationStampPath)) return;
            try
            {
                if (File.Exists(FlowChainsPath)) File.Delete(FlowChainsPath);
                string? dir = Path.GetDirectoryName(MigrationStampPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(MigrationStampPath, DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        public void SaveFlowChains(ObservableCollection<FlowChain> chains)
        {
            try
            {
                string? dir = Path.GetDirectoryName(FlowChainsPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string content = JsonSerializer.Serialize(chains);
                File.WriteAllText(FlowChainsPath, content);
            }
            catch { }
        }

        private static ObservableCollection<FlowChain> GetDefaultFlowChains()
            => new ObservableCollection<FlowChain>();
    }
}
