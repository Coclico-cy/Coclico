using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<NetworkAdapterHardwareDetails>> DiscoverPhysicalAdaptersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DynamicNetworkParameter>> DiscoverAdapterParametersAsync(string adapterIdOrGuid, string? adapterName = null, CancellationToken ct = default);
    Task<bool> RefreshWifiTelemetryAsync(NetworkAdapterHardwareDetails adapter, CancellationToken ct = default);
}