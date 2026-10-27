namespace Coclico.Modules.UpdateCenter.Models;

public sealed record DriverInfo
{
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public string? Manufacturer { get; init; }
    public string? DriverVersion { get; init; }
    public DateTime? DriverDate { get; init; }
    public bool IsUpdatable { get; init; }

    public int AgeMonths => DriverDate is { } d
        ? Math.Max(0, (int)((DateTime.UtcNow - d.ToUniversalTime()).TotalDays / 30.4375))
        : 0;
}
