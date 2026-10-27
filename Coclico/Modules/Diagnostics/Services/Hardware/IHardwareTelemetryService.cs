namespace Coclico.Modules.Diagnostics.Services.Hardware;

using Coclico.Modules.Diagnostics.Models;

public sealed record HardwareSensorValue(
    string SensorId,
    string DisplayNameKey,
    double? Value,
    string Unit,
    DiagnosticValueState State);

public sealed record HardwareSensorSnapshot(
    DateTime ReadAtUtc,
    IReadOnlyList<HardwareSensorValue> Values,
    bool AnySourceAvailable);

public interface IHardwareTelemetryService : IAsyncDisposable
{
    Task<HardwareSensorSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}
