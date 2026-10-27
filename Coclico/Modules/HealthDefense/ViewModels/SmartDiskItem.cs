using Coclico.Services;

namespace Coclico.ViewModels;

public sealed class SmartDiskItem
{
    public SmartDiskItem(SmartDiskStatus status)
    {
        InstanceName = status.InstanceName;
        IsWarning = status.PredictsFailure;
        Advisory = status.Advisory;
    }

    public string InstanceName { get; }

    public bool IsWarning { get; }

    public bool IsOk => !IsWarning;

    public string Advisory { get; }
}
