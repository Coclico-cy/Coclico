using System.ComponentModel;
using System.Runtime.CompilerServices;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.ViewModels;

public sealed class UpdateItemViewModel : INotifyPropertyChanged
{
    private UpdateItemStatus? _status;
    private bool _isSelected;

    public required UpdateItem Model { get; init; }

    public string Name => Model.Name;
    public string Id => Model.Id;
    public UpdateKind Kind => Model.Kind;
    public UpdateSource Source => Model.Source;
    public string? CurrentVersion => Model.CurrentVersion;
    public string? AvailableVersion => Model.AvailableVersion;

    public string SourceLabel => Source switch
    {
        UpdateSource.Winget => "Winget",
        UpdateSource.WindowsUpdate => "Windows Update",
        UpdateSource.GitHub => "GitHub",
        _ => "—"
    };

    public bool IsSelectable => Status is UpdateItemStatus.Available or UpdateItemStatus.UnknownVersion;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsSelectable)
                value = false;
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public UpdateItemStatus Status
    {
        get => _status ?? Model.Status;
        private set
        {
            if (Status == value)
                return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelectable));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string StatusLabel => Status switch
    {
        UpdateItemStatus.Available => "Disponible",
        UpdateItemStatus.UnknownVersion => "Version inconnue",
        UpdateItemStatus.UpToDate => "À jour",
        UpdateItemStatus.Updating => "En cours…",
        UpdateItemStatus.Succeeded => "Terminé",
        UpdateItemStatus.Failed => "Échec",
        UpdateItemStatus.Skipped => "Ignoré",
        _ => "—"
    };

    public void SetStatus(UpdateItemStatus status) => Status = status;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
