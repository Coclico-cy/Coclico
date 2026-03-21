using System;
using System.Threading.Tasks;
using System.Windows;
using Coclico.Services;

namespace Coclico.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public async Task RunStartupAsync()
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));

                this.Closing += (s, e) => { try { cts.Cancel(); } catch { } };

            var progress = new Progress<StartupProgress>(p =>
            {
                _ = SetProgress(p.Status, p.Percent);
            });

            try
            {
                await StartupService.Instance.RunStartupAsync(progress, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LoggingService.LogInfo("Startup timed out or was cancelled from splash");
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "SplashWindow.RunStartupAsync");
            }
        }

        private async Task SetProgress(string status, double value)
        {
            TxtStatus.Text   = status;
            PbProgress.Value = value;
            await Task.Delay(55);
        }
    }
}
