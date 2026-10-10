using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

public class HealthDefenseViewModel : INotifyPropertyChanged
{
    private readonly SystemHealthService _healthService;
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> OutputLog { get; } = [];

    public bool IsBusy
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string BusyActionTitle
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public int CurrentStepNumber
    {
        get;
        set { field = value; OnPropertyChanged(); }
    }

    public string CurrentStepDescription
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public string LastScanSummary
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = string.Empty;

    public bool IsAntivirusActive
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = true;

    public bool IsRealTimeProtectionOn
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = true;

    public string SignatureVersion
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "À jour";

    public string LastUpdatedText
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Récemment";

    public IAsyncRelayCommand RunFullRepairCommand { get; }
    public IAsyncRelayCommand RunQuickScanCommand { get; }
    public IAsyncRelayCommand RunFullScanCommand { get; }
    public IAsyncRelayCommand RunOfflineScanCommand { get; }
    public IAsyncRelayCommand UpdateSignaturesCommand { get; }
    public IAsyncRelayCommand LaunchMrtCommand { get; }
    public IAsyncRelayCommand RunSfcCommand { get; }
    public IAsyncRelayCommand RunDismRestoreCommand { get; }
    public IAsyncRelayCommand RunDismCleanupCommand { get; }
    public IAsyncRelayCommand RunChkdskCommand { get; }
    public IAsyncRelayCommand ResetNetworkCommand { get; }
    public IAsyncRelayCommand ResetUpdateCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ClearLogCommand { get; }

    public HealthDefenseViewModel(SystemHealthService healthService)
    {
        _healthService = healthService;

        RunFullRepairCommand = new AsyncRelayCommand(RunFullRepairAsync);
        RunQuickScanCommand = new AsyncRelayCommand(RunQuickScanAsync);
        RunFullScanCommand = new AsyncRelayCommand(RunFullScanAsync);
        RunOfflineScanCommand = new AsyncRelayCommand(RunOfflineScanAsync);
        UpdateSignaturesCommand = new AsyncRelayCommand(UpdateSignaturesAsync);
        LaunchMrtCommand = new AsyncRelayCommand(LaunchMrtAsync);
        RunSfcCommand = new AsyncRelayCommand(RunSfcAsync);
        RunDismRestoreCommand = new AsyncRelayCommand(RunDismRestoreAsync);
        RunDismCleanupCommand = new AsyncRelayCommand(RunDismCleanupAsync);
        RunChkdskCommand = new AsyncRelayCommand(RunChkdskAsync);
        ResetNetworkCommand = new AsyncRelayCommand(ResetNetworkAsync);
        ResetUpdateCommand = new AsyncRelayCommand(ResetUpdateAsync);
        CancelCommand = new RelayCommand(CancelOperation);
        ClearLogCommand = new RelayCommand(OutputLog.Clear);

        _ = RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        SystemHealthService.DefenderStatusInfo status = await _healthService.GetDefenderStatusAsync();
        IsAntivirusActive = status.IsAntivirusActive;
        IsRealTimeProtectionOn = status.IsRealTimeProtectionOn;
        SignatureVersion = status.SignatureVersion;
        LastUpdatedText = status.LastUpdatedTime.HasValue
            ? status.LastUpdatedTime.Value.ToString("dd/MM/yyyy HH:mm")
            : "Récemment";
    }

    private void AppendLog(string message)
    {
        _ = (Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OutputLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (OutputLog.Count > 300)
            {
                OutputLog.RemoveAt(0);
            }
        }));
    }

    private async Task ExecuteActionWrapperAsync(string actionTitle, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyActionTitle = actionTitle;
        _cts = new CancellationTokenSource();

        try
        {
            AppendLog($"Démarrage : {actionTitle}");
            await action(_cts.Token);
            AppendLog($"Terminé : {actionTitle}");
        }
        catch (OperationCanceledException)
        {
            AppendLog($"⚠️ Opération annulée par l'utilisateur.");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Erreur : {ex.Message}");
            LoggingService.LogException(ex, $"HealthDefenseVM.{actionTitle}");
        }
        finally
        {
            IsBusy = false;
            BusyActionTitle = string.Empty;
            _cts?.Dispose();
            _cts = null;
            await RefreshStatusAsync();
        }
    }

    private async Task RunFullRepairAsync()
    {
        await ExecuteActionWrapperAsync("Réparation Intégrale Windows 1-Clic", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunFullSystemRepairAsync(
                onOutput: AppendLog,
                onStep: (step, desc) =>
                {
                    CurrentStepNumber = step;
                    CurrentStepDescription = desc;
                },
                ct: ct);

            LastScanSummary = res.Summary;
        });
    }

    private async Task RunQuickScanAsync()
    {
        await ExecuteActionWrapperAsync("Analyse Rapide Windows Defender", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunQuickDefenderScanAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task RunFullScanAsync()
    {
        await ExecuteActionWrapperAsync("Analyse Complète Windows Defender", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunFullDefenderScanAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task RunOfflineScanAsync()
    {
        MessageBoxResult confirm = MessageBox.Show(
            "L'analyse hors-ligne va redémarrer votre ordinateur pour analyser le système avant le chargement de Windows.\n\nSouhaitez-vous continuer ?",
            "Confirmation Analyse Hors-Ligne",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ExecuteActionWrapperAsync("Analyse Hors-Ligne Defender", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.ScheduleOfflineDefenderScanAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task UpdateSignaturesAsync()
    {
        await ExecuteActionWrapperAsync("Mise à jour des définitions Defender", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.UpdateDefenderSignaturesAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task LaunchMrtAsync()
    {
        AppendLog("Lancement de l'Outil de suppression des logiciels malveillants Microsoft (MRT)...");
        bool started = await _healthService.LaunchMrtAsync(quiet: false);
        if (started)
        {
            AppendLog("✅ Fenêtre MRT ouverte avec succès.");
        }
        else
        {
            AppendLog("⚠️ Impossible d'ouvrir MRT.");
        }
    }

    private async Task RunSfcAsync()
    {
        await ExecuteActionWrapperAsync("Vérification des fichiers système (SFC /scannow)", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunSfcScanAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task RunDismRestoreAsync()
    {
        await ExecuteActionWrapperAsync("Restauration de l'image Windows (DISM /RestoreHealth)", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunDismRestoreHealthAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task RunDismCleanupAsync()
    {
        await ExecuteActionWrapperAsync("Nettoyage du magasin des composants (WinSxS)", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunDismComponentCleanupAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task RunChkdskAsync()
    {
        await ExecuteActionWrapperAsync("Analyse du système de fichiers (CHKDSK /scan)", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.RunChkdskScanAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task ResetNetworkAsync()
    {
        await ExecuteActionWrapperAsync("Réinitialisation Réseau & Winsock", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.ResetNetworkStackAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private async Task ResetUpdateAsync()
    {
        await ExecuteActionWrapperAsync("Réinitialisation de Windows Update", async ct =>
        {
            SystemHealthService.DiagnosticResult res = await _healthService.ResetWindowsUpdateAsync(AppendLog, ct);
            LastScanSummary = res.Summary;
        });
    }

    private void CancelOperation()
    {
        _cts?.Cancel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
