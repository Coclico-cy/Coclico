namespace Coclico.Modules.UpdateCenter.Models;

public sealed record UpdateItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required UpdateKind Kind { get; init; }
    public required UpdateSource Source { get; init; }
    public required UpdateItemStatus Status { get; init; }
    public string? CurrentVersion { get; init; }
    public string? AvailableVersion { get; init; }
    public long? SizeBytes { get; init; }
    public string? DetailUrl { get; init; }
    public string? Error { get; init; }

    public static UpdateItem Unknown(string id, string name, UpdateKind kind, UpdateSource source) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Source = source,
        Status = UpdateItemStatus.UnknownVersion
    };

    public static UpdateItem UpToDate(string id, string name, UpdateKind kind, UpdateSource source) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Source = source,
        Status = UpdateItemStatus.UpToDate
    };
}
