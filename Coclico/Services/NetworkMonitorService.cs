using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Coclico.Services
{
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
        private static readonly Lazy<NetworkMonitorService> _lazy = new(() => new NetworkMonitorService());
        public static NetworkMonitorService Instance => _lazy.Value;

        private readonly BehaviorSubject<NetworkStats> _subject;
        private readonly System.Timers.Timer _timer;
        private DateTime _lastSnapshot = DateTime.UtcNow;
        private readonly Dictionary<string, (long Sent, long Received)> _lastInterfaceBytes = new();

        public IObservable<NetworkStats> StatsStream => _subject.AsObservable();
        public NetworkStats CurrentStats => _subject.Value;

        private NetworkMonitorService()
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
                var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up)
                    .Where(i => i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                long totalSent = 0, totalReceived = 0;
                foreach (var ni in ifaces)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalSent += stats.BytesSent;
                    totalReceived += stats.BytesReceived;

                    _lastInterfaceBytes.TryGetValue(ni.Id, out var last);
                    _lastInterfaceBytes[ni.Id] = (stats.BytesSent, stats.BytesReceived);
                }

                var elapsed = Math.Max((now - _lastSnapshot).TotalSeconds, 0.001);
                double uploadKbps = 0;
                double downloadKbps = 0;

                if (_lastInterfaceBytes.Count > 0)
                {
                    var previousTotalSent = _lastInterfaceBytes.Values.Sum(x => x.Sent);
                    var previousTotalReceived = _lastInterfaceBytes.Values.Sum(x => x.Received);

                    uploadKbps = Math.Max(0, (totalSent - previousTotalSent) * 8.0 / 1024.0 / elapsed);
                    downloadKbps = Math.Max(0, (totalReceived - previousTotalReceived) * 8.0 / 1024.0 / elapsed);
                }

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
}
