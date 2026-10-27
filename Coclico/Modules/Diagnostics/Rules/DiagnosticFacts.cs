namespace Coclico.Modules.Diagnostics.Rules;

using Coclico.Modules.Diagnostics.Models;

public sealed record VolumeFacts(
    string VolumeName,
    long TotalBytes,
    long FreeBytes,
    string FileSystem,
    string DriveType);

public sealed record DiskReliabilityFacts(
    string DiskName,
    long? TemperatureC,
    long? WearPercent,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? PowerOnHours,
    string? HealthStatus);

public sealed record StorageFacts(
    IReadOnlyList<VolumeFacts> Volumes,
    IReadOnlyList<DiskReliabilityFacts> Disks,
    bool DisksEnumerated);

public sealed record MemoryFacts(
    long TotalPhysicalBytes,
    long AvailablePhysicalBytes,
    long CommitTotalBytes,
    long CommitLimitBytes,
    int ProcessCount,
    string? TopProcessName,
    long? TopProcessWorkingSetBytes);

public sealed record CpuFacts(
    string ProcessorName,
    int LogicalCoreCount,
    double LoadPercentMin,
    double LoadPercentMax,
    double? TemperatureC,
    bool TemperatureAvailable,
    string? TopProcessName,
    double? TopProcessCpuPercent);

public sealed record GpuAdapterFacts(
    string Name,
    string? DriverVersion,
    long? AdapterRamBytes,
    string? Status);

public sealed record GatewayProbeFacts(
    string? GatewayAddress,
    int ProbesSent,
    int ProbesReceived,
    double? MedianMs,
    double? P95Ms);

public sealed record DnsProbeFacts(
    string HostName,
    bool Resolved,
    double? ResolveMs);

public sealed record ExternalProbeFacts(
    string Destination,
    int ProbesSent,
    int ProbesReceived,
    double? MedianMs,
    double? P95Ms);

public sealed record NetworkFacts(
    IReadOnlyList<string> UpAdapters,
    bool AnyAdapterUp,
    string? ActiveAdapterName,
    long? LinkSpeedMbps,
    GatewayProbeFacts Gateway,
    DnsProbeFacts Dns,
    IReadOnlyList<ExternalProbeFacts> ExternalProbes);

public sealed record EventGroupFacts(
    string Provider,
    long EventId,
    int Count,
    DateTime? LastSeenUtc,
    string? Level);

public sealed record WindowsFacts(
    string OsVersion,
    string OsBuild,
    TimeSpan Uptime,
    int UnexpectedShutdowns7d,
    IReadOnlyList<EventGroupFacts> CriticalGroups7d,
    bool EventLogAccessible);

public sealed record DefenderFacts(
    bool StatusReadable,
    bool RealTimeProtectionEnabled,
    bool AntivirusEnabled,
    bool AntispywareEnabled,
    string? SignatureAgeDays);

public sealed record DeviceProblemFacts(
    string DeviceName,
    string? DeviceClass,
    int ConfigManagerErrorCode);

public sealed record BatteryFacts(
    bool BatteryPresent,
    bool OnAcPower,
    int? ChargePercent,
    int? EstimatedRuntimeMinutes,
    double? FullLifeRatio,
    bool DataAvailable);

public sealed record ThermalFacts(
    double? ThermalZoneC,
    bool ThermalZoneAvailable);

public sealed record ProcessFacts(
    int ProcessCount,
    string? TopCpuProcessName,
    double? TopCpuPercent,
    string? TopRamProcessName,
    double? TopRamPercent,
    bool StartupHealthReadable,
    bool StartupHealthy,
    string? StartupHealthMessage);
