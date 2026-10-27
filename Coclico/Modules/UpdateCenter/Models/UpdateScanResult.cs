namespace Coclico.Modules.UpdateCenter.Models;

public sealed record UpdateScanResult
{
    public required IReadOnlyList<UpdateItem> Items { get; init; }
    public required DateTimeOffset ScannedAt { get; init; }
    public required IReadOnlyDictionary<string, string> SourceErrors { get; init; }

    public bool HasFailures => SourceErrors.Count > 0;
    public int AvailableCount => Items.Count(i => i.Status is UpdateItemStatus.Available or UpdateItemStatus.UnknownVersion);
}
