using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Coclico.Services;

namespace Coclico.ViewModels
{
    public class ScannerViewModel : INotifyPropertyChanged
    {
        private bool _isLoading;
        private string _statusMessage;

        private ObservableCollection<InstalledProgramsService.ProgramInfo> _programs = new();
        public ObservableCollection<InstalledProgramsService.ProgramInfo> Programs
        {
            get => _programs;
            private set { _programs = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand ScanCommand { get; }

        public ScannerViewModel()
        {
            _statusMessage = LocalizationService.Instance.Get("Scanner_Ready");
            if (string.IsNullOrEmpty(_statusMessage)) _statusMessage = "Prêt à analyser.";

            ScanCommand = new RelayCommandAsync(async _ => await ScanApplicationsAsync());
        }

        private async Task ScanApplicationsAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            StatusMessage = LocalizationService.Instance.Get("Scanner_Scanning") is { Length: > 0 } s ? s : "Analyse en cours…";

            try
            {
                var programsList = await InstalledProgramsService.Instance.GetAllInstalledProgramsAsync();

                Programs.Clear();
                foreach (var item in programsList)
                {
                    Programs.Add(new InstalledProgramsService.ProgramInfo
                    {
                        Name = item.Name ?? string.Empty,
                        Publisher = item.Publisher ?? string.Empty,
                        InstallPath = item.InstallPath ?? string.Empty,
                        ExePath = item.ExePath ?? string.Empty,
                        Version = item.Version ?? string.Empty,
                        Source = item.Source ?? string.Empty,
                        IconPath = item.IconPath ?? string.Empty,
                        Category = item.Category ?? string.Empty,
                        SizeBytes = item.SizeBytes
                    });
                }

                var doneKey = LocalizationService.Instance.Get("Scanner_Done");
                StatusMessage = string.IsNullOrEmpty(doneKey)
                    ? $"Analyse terminée — {Programs.Count} application(s) trouvée(s)."
                    : string.Format(doneKey, Programs.Count);
            }
            catch (System.Exception ex)
            {
                LoggingService.LogException(ex, "ScannerViewModel.ScanApplicationsAsync");
                var errKey = LocalizationService.Instance.Get("Scanner_Error");
                StatusMessage = string.IsNullOrEmpty(errKey)
                    ? $"Erreur lors de l'analyse : {ex.Message}"
                    : $"{errKey} {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

