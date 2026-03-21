using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public class CleaningService
    {
        private static string GetWindowsDrive()
        {
            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string? root  = Path.GetPathRoot(winDir);
                if (!string.IsNullOrEmpty(root) && root.Length >= 1)
                    return root[0].ToString().ToUpperInvariant();
            }
            catch { }
            return "C";
        }

        public async Task LaunchWindowsCleanupAsync()
        {
            string drive = GetWindowsDrive();
            await Task.Run(() =>
            {
                try
                {
                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName        = "cleanmgr.exe",
                            Arguments       = $"/d {drive}:",
                            UseShellExecute = true,
                            Verb            = "runas",
                            WindowStyle     = ProcessWindowStyle.Normal
                        }
                    };

                    process.Start();
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    process.WaitForExit(300_000);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Impossible de lancer le nettoyage Windows : {ex.Message}", ex);
                }
            });
        }
    }
}
