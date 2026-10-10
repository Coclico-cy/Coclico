using System.Net.NetworkInformation;

namespace Coclico.Services.Network;

public sealed class NetworkWatchdogService : INetworkWatchdogService
{
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private readonly object _lock = new();

    public bool IsMonitoring { get; private set; }

    public void StartMonitoring(string adapterId, string gatewayIp, Func<string, Task> onEmergencyRollback)
    {
        lock (_lock)
        {
            StopMonitoring();

            _cts = new CancellationTokenSource();
            IsMonitoring = true;
            CancellationToken token = _cts.Token;

            _ = Task.Run(async () =>
            {
                using var ping = new Ping();
                int consecutiveFailures = 0;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                        NetworkInterface? adapter = interfaces.FirstOrDefault(i => string.Equals(i.Id, adapterId, StringComparison.OrdinalIgnoreCase));
                        if (adapter != null && adapter.OperationalStatus == OperationalStatus.Down)
                        {
                            LoggingService.LogError("[NetworkWatchdogService] WATCHDOG: Interface réseau tombée en panne !");
                            await onEmergencyRollback("Interface réseau déconnectée (Link Down)").ConfigureAwait(false);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(gatewayIp))
                        {
                            PingReply reply = await ping.SendPingAsync(gatewayIp, 600).WaitAsync(TimeSpan.FromMilliseconds(700), token).ConfigureAwait(false);
                            if (reply.Status != IPStatus.Success)
                            {
                                consecutiveFailures++;
                                if (consecutiveFailures >= 3)
                                {
                                    consecutiveFailures = 0;
                                    LoggingService.LogError($"[NetworkWatchdogService] WATCHDOG: Passerelle locale {gatewayIp} injoignable après 3 essais !");
                                    await onEmergencyRollback($"Passerelle {gatewayIp} injoignable (Perte de lien local)").ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                consecutiveFailures = 0;
                            }
                        }

                        await Task.Delay(400, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogException(ex, "NetworkWatchdogService.Loop");
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                }

                IsMonitoring = false;
            }, token);
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            IsMonitoring = false;
        }
    }

    public async Task<bool> QuickHealthCheckAsync(string gatewayIp, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            if (!string.IsNullOrEmpty(gatewayIp))
            {
                PingReply rep = await ping.SendPingAsync(gatewayIp, 800).WaitAsync(TimeSpan.FromMilliseconds(900), ct).ConfigureAwait(false);
                if (rep.Status != IPStatus.Success)
                {
                    return false;
                }
            }

            PingReply netRep = await ping.SendPingAsync("1.1.1.1", 1000).WaitAsync(TimeSpan.FromMilliseconds(1100), ct).ConfigureAwait(false);
            return netRep.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitoring();
    }
}