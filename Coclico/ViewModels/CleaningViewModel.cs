using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Coclico.Services;

namespace Coclico.ViewModels
{
    public class CleaningViewModel : INotifyPropertyChanged
    {
        private readonly CleaningService _cleaningService = new();
        private readonly DeepCleaningService _deepCleaning = new();
        private CancellationTokenSource? _cts;
        private bool _isCleaning;
        private string _statusMessage;

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string _estimatedSpace = "";
        public string EstimatedSpace
        {
            get => _estimatedSpace;
            set { _estimatedSpace = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DeepCleaningService.CleaningCategory> Categories { get; } = new();

        public CleaningViewModel()
        {
            _statusMessage = LocalizationService.Instance.Get("Cleaning_Ready");
            _ = LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var cats = _deepCleaning.GetAvailableCategories();
                foreach (var c in cats) Categories.Add(c);

                var bytes = await _deepCleaning.EstimateCleanableBytesAsync();
                EstimatedSpace = FormatBytes(bytes);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "CleaningVM.LoadCategories"); }
        }

        public bool IsCleaning
        {
            get => _isCleaning;
            set { _isCleaning = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public async Task StartCleaningAsync()
        {
            var loc = LocalizationService.Instance;

            if (MessageBox.Show(
                    loc.Get("Cleaning_Confirm"),
                    loc.Get("Cleaning_ConfirmTitle"),
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            IsCleaning = true;
            StatusMessage = loc.Get("Cleaning_Running");

            try
            {
                await _cleaningService.LaunchWindowsCleanupAsync();
                StatusMessage = loc.Get("Cleaning_Done");
                MessageBox.Show(loc.Get("Cleaning_Success"), loc.Get("Cleaning_SuccessTitle"));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{loc.Get("Cleaning_Error")} : {ex.Message}", loc.Get("Cleaning_Error"));
            }
            finally
            {
                IsCleaning = false;
                StatusMessage = loc.Get("Cleaning_Ready");
            }
        }

        public async Task StartDeepCleaningAsync()
        {
            if (IsCleaning) return;
            IsCleaning = true;
            Progress = 0;
            var loc = LocalizationService.Instance;
            StatusMessage = loc.Get("DeepCleaning_Running");
            _cts = new CancellationTokenSource();

            var selected = Categories.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = loc.Get("DeepCleaning_NoCategorySelected");
                IsCleaning = false;
                return;
            }

            var progressReporter = new Progress<(string status, int percent, long bytesFreed)>(p =>
            {
                Progress = p.percent;
                StatusMessage = $"{p.status} ({p.percent}%)";
            });

            try
            {
                var result = await _deepCleaning.ExecuteDeepCleanAsync(selected, progressReporter, _cts.Token);
                StatusMessage = string.Format(loc.Get("DeepCleaning_Done"), FormatBytes(result.TotalBytesFreed), result.FilesDeleted);
                Progress = 100;

                var bytes = await _deepCleaning.EstimateCleanableBytesAsync();
                EstimatedSpace = FormatBytes(bytes);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = loc.Get("DeepCleaning_Cancelled");
            }
            catch (Exception ex)
            {
                StatusMessage = $"{loc.Get("Cleaning_Error")} : {ex.Message}";
                LoggingService.LogException(ex, "CleaningVM.DeepClean");
            }
            finally
            {
                IsCleaning = false;
            }
        }

        public void CancelCleaning()
        {
            _cts?.Cancel();
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F1} {sizes[order]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            else
                Application.Current?.Dispatcher?.Invoke(
                    () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }
}
