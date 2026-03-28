#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public record NetworkStats(
    DateTime Timestamp,
    long TotalBytesSent,
    long TotalBytesReceived,
    double UploadKbps,
    double DownloadKbps,
    int ActiveTcpConnections,
    double PingMs);

public class NetworkMonitorService : IDisposable
{

    private readonly BehaviorSubject<NetworkStats> _subject;
    private readonly System.Timers.Timer _timer;
    private DateTime _lastSnapshot = DateTime.UtcNow;

    private readonly ConcurrentDictionary<string, (long Sent, long Received)> _lastInterfaceBytes = new();

    private long _prevTotalSent;
    private long _prevTotalReceived;

    public IObservable<NetworkStats> StatsStream => _subject.AsObservable();
    public NetworkStats CurrentStats => _subject.Value;

    public NetworkMonitorService()
    {
        _subject = new BehaviorSubject<NetworkStats>(new NetworkStats(
            DateTime.UtcNow, 0, 0, 0, 0, 0, 0));

        _timer = new System.Timers.Timer(5000)
        {
            AutoReset = true,
            Enabled = true
        };
        _timer.Elapsed += (_, _) => _ = RefreshAsync();

        _ = RefreshAsync();
    }

    private async Task<double> ProbeLatencyAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1000).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
                return reply.RoundtripTime;
        }
        catch { }
        return -1;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var now = DateTime.UtcNow;

            long totalSent = 0, totalReceived = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var stats = ni.GetIPv4Statistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;

                _lastInterfaceBytes.AddOrUpdate(ni.Id,
                    _ => (stats.BytesSent, stats.BytesReceived),
                    (_, _) => (stats.BytesSent, stats.BytesReceived));
            }

            var elapsed = Math.Max((now - _lastSnapshot).TotalSeconds, 0.001);

            long prevSent = Interlocked.Exchange(ref _prevTotalSent, totalSent);
            long prevReceived = Interlocked.Exchange(ref _prevTotalReceived, totalReceived);

            double uploadKbps = Math.Max(0, (totalSent - prevSent) * 8.0 / 1024.0 / elapsed);
            double downloadKbps = Math.Max(0, (totalReceived - prevReceived) * 8.0 / 1024.0 / elapsed);

            _lastSnapshot = now;

            var tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length;
            var pingMs = await ProbeLatencyAsync().ConfigureAwait(false);

            var snapshot = new NetworkStats(now, totalSent, totalReceived, uploadKbps, downloadKbps, tcpConnections, pingMs);
            _subject.OnNext(snapshot);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkMonitorService.RefreshAsync");
        }
    }

    public void Dispose()
    {
        try { _timer.Stop(); _timer.Dispose(); } catch { }
        try { _subject.OnCompleted(); _subject.Dispose(); } catch { }
        GC.SuppressFinalize(this);
    }
}
