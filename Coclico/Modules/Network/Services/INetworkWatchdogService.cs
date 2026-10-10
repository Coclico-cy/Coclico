namespace Coclico.Services.Network;

public interface INetworkWatchdogService : IDisposable
{
    bool IsMonitoring { get; }
    void StartMonitoring(string adapterId, string gatewayIp, Func<string, Task> onEmergencyRollback);
    void StopMonitoring();
    Task<bool> QuickHealthCheckAsync(string gatewayIp, CancellationToken ct = default);
}
