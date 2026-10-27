using System.ComponentModel;
using System.Runtime.CompilerServices;
using Coclico.Services;

namespace Coclico.ViewModels;

public sealed class StartupEntryItem : INotifyPropertyChanged
{
    private readonly Func<StartupEntryItem, bool, bool> _toggle;
    private bool _isEnabled;

    public StartupEntryItem(StartupEntry entry, Func<StartupEntryItem, bool, bool> toggle)
    {
        Entry = entry;
        _toggle = toggle;
        _isEnabled = entry.IsEnabled;
    }

    public StartupEntry Entry { get; }

    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Source => Entry.Source;
    public bool CanToggle => Entry.CanToggle;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            if (_toggle(this, value))
            {
                _isEnabled = value;
            }

            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
