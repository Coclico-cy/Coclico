using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkSnapshotService
{
    NetworkConfigurationSnapshot? InitialBaselineSnapshot { get; }
    Task<NetworkConfigurationSnapshot> CaptureSnapshotAsync(string adapterIdOrGuid, string adapterName, string label, CancellationToken ct = default);
    Task<bool> RollbackToSnapshotAsync(NetworkConfigurationSnapshot snapshot, CancellationToken ct = default);
    Task<bool> RollbackSingleParameterAsync(string adapterIdOrGuid, string keyword, string originalValue, CancellationToken ct = default);
    Task<bool> RollbackToBaselineAsync(CancellationToken ct = default);
    IReadOnlyList<NetworkConfigurationSnapshot> GetStoredSnapshots();
}