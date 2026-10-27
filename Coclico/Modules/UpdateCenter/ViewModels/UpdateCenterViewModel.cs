using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;

namespace Coclico.Modules.UpdateCenter.ViewModels;

public enum UpdateCenterState
{
    Idle,
    Scanning,
    Ready,
    Updating
}

public sealed class UpdateCenterViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan AutoScanInterval = TimeSpan.FromMinutes(10);

    private readonly UpdateScanService _scanService;
    private readonly WingetUpdateService _wingetService;
    private readonly DriverUpdateService _driverService;
    private readonly RestorePointService _restorePointService;
    private CancellationTokenSource? _cts;
    private UpdateCenterState _state = UpdateCenterState.Idle;
    private DateTimeOffset? _lastScan;
    private string? _lastScanSummary;
    private string _searchText = string.Empty;

    public UpdateCenterViewModel(
        UpdateScanService scanService,
        WingetUpdateService wingetService,
        DriverUpdateService driverService,
        RestorePointService restorePointService)
    {
        _scanService = scanService;
        _wingetService = wingetService;
        _driverService = driverService;
        _restorePointService = restorePointService;

        CheckAllCommand = new RelayCommand(_ => CheckAll(), _ => State is UpdateCenterState.Idle or UpdateCenterState.Ready);
        UpdateSelectedCommand = new RelayCommand(_ => _ = UpdateSelectedAsync(), _ => State == UpdateCenterState.Ready && SelectedItems.Any());

        Sections.Add(Applications);
        Sections.Add(Drivers);
        Sections.Add(SystemSection);
    }

    public ObservableCollection<UpdateSectionViewModel> Sections { get; } = [];

    public UpdateSectionViewModel Applications { get; } = new() { Kind = UpdateKind.Application, IsExpanded = true };
    public UpdateSectionViewModel Drivers { get; } = new() { Kind = UpdateKind.Driver };
    public UpdateSectionViewModel SystemSection { get; } = new() { Kind = UpdateKind.Windows };

    public ICommand CheckAllCommand { get; }
    public ICommand UpdateSelectedCommand { get; }

    public UpdateCenterState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DateTimeOffset? LastScan
    {
        get => _lastScan;
        private set
        {
            if (_lastScan == value) return;
            _lastScan = value;
            OnPropertyChanged();
        }
    }

    public string? LastScanSummary
    {
        get => _lastScanSummary;
        private set
        {
            if (_lastScanSummary == value) return;
            _lastScanSummary = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            foreach (var section in Sections)
                section.ApplyFilter(value);
        }
    }

    public IEnumerable<UpdateItemViewModel> SelectedItems => Sections.SelectMany(s => s.Items).Where(i => i.IsSelected);

    public void CheckAll()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = CheckAllAsync(_cts.Token);
    }

    public void CheckAllIfStale()
    {
        if (State is UpdateCenterState.Scanning or UpdateCenterState.Updating)
        {
            return;
        }

        if (LastScan is { } last && DateTimeOffset.Now - last < AutoScanInterval)
        {
            return;
        }

        CheckAll();
    }

    private async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        State = UpdateCenterState.Scanning;
        foreach (var section in Sections)
        {
            section.Items.Clear();
            section.IsBusy = true;
        }

        try
        {
            var result = await Task.Run(() => _scanService.ScanAllAsync(cancellationToken), cancellationToken).ConfigureAwait(true);

            foreach (var item in result.Items)
                SectionFor(item.Kind).Items.Add(new UpdateItemViewModel { Model = item, IsSelected = false });

            LastScan = result.ScannedAt;
            LastScanSummary = result.HasFailures
                ? $"{result.AvailableCount} mise(s) à jour · {result.SourceErrors.Count} source(s) en erreur"
                : $"{result.AvailableCount} mise(s) à jour";

            State = UpdateCenterState.Ready;

            foreach (var section in Sections)
                section.ApplyFilter(SearchText);
        }
        catch (OperationCanceledException)
        {
            State = UpdateCenterState.Idle;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LastScanSummary = "Analyse interrompue";
            State = UpdateCenterState.Idle;
        }
        finally
        {
            foreach (var section in Sections)
            {
                section.IsBusy = false;
                section.RefreshCount();
            }
        }
    }

    public async Task UpdateSelectedAsync()
    {
        var selected = SelectedItems.Where(i => i.Kind is UpdateKind.Application or UpdateKind.Driver).ToList();
        if (selected.Count == 0 || State == UpdateCenterState.Updating)
            return;

        State = UpdateCenterState.Updating;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var hasDriver = selected.Any(i => i.Kind == UpdateKind.Driver);
        if (hasDriver)
            _ = _restorePointService.Create("Coclico — mise à jour de pilotes");

        var success = 0;
        var failures = new List<string>();

        foreach (var item in selected)
        {
            item.SetStatus(UpdateItemStatus.Updating);
            try
            {
                var updated = item.Kind switch
                {
                    UpdateKind.Application => await _wingetService.UpgradeAsync(item.Id, _cts.Token).ConfigureAwait(true),
                    UpdateKind.Driver => await _driverService.InstallAsync(item.Model, _cts.Token).ConfigureAwait(true),
                    _ => false
                };

                item.SetStatus(updated ? UpdateItemStatus.Succeeded : UpdateItemStatus.Failed);
                if (updated) success++;
                else failures.Add(item.Name);
            }
            catch (OperationCanceledException)
            {
                item.SetStatus(UpdateItemStatus.Skipped);
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                item.SetStatus(UpdateItemStatus.Failed);
                failures.Add($"{item.Name} ({ex.Message})");
            }
        }

        await CheckAllAsync(_cts.Token).ConfigureAwait(true);

        LastScanSummary = failures.Count == 0
            ? $"{success} élément(s) mis à jour"
            : $"{success} réussie(s) · {failures.Count} échec(s)";
    }

    public void Cancel()
    {
        _cts?.Cancel();
        State = UpdateCenterState.Idle;
    }

    private UpdateSectionViewModel SectionFor(UpdateKind kind) => kind switch
    {
        UpdateKind.Application => Applications,
        UpdateKind.Driver => Drivers,
        _ => SystemSection
    };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
    }
}
