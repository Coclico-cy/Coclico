namespace Coclico.Modules.UpdateCenter.Models;

public enum UpdateItemStatus
{
    Available,
    UnknownVersion,
    UpToDate,
    Updating,
    Succeeded,
    Failed,
    Skipped
}
