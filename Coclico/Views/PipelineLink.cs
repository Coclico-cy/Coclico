#nullable enable
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Coclico.Views;

public class PipelineLink : INotifyPropertyChanged
{
    private double _startX, _startY, _endX, _endY;

    public double StartX { get => _startX; set { _startX = value; OnPropertyChanged(); } }
    public double StartY { get => _startY; set { _startY = value; OnPropertyChanged(); } }
    public double EndX { get => _endX; set { _endX = value; OnPropertyChanged(); } }
    public double EndY { get => _endY; set { _endY = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
