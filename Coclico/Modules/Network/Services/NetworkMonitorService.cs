using System.Net.NetworkInformation;
using System.Reactive.Linq;
using System.Reactive.Subjects;

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
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(5);
    private const string Subsystem = "NetworkMonitor";

    private readonly BehaviorSubject<NetworkStats> _subject;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Action<CentralActivityLevel> _onActivityChanged;
    private DateTime _lastSnapshot = DateTime.UtcNow;
    private DateTime _lastPingUtc = DateTime.MinValue;
    private long _prevTotalSent;
    private long _prevTotalReceived;
    private double _lastPingMs = -1;
    private volatile bool _pingDue = true;
    private volatile bool _consumerVisible;

    public IObservable<NetworkStats> StatsStream => _subject.AsObservable();
    public NetworkStats CurrentStats => _subject.Value;

    public NetworkMonitorService()
    {
        _subject = new BehaviorSubject<NetworkStats>(new NetworkStats(
            DateTime.UtcNow, 0, 0, 0, 0, 0, 0));

        _onActivityChanged = _ =>
        {
            _pingDue = true;
            CentralUnitService.Current.Flags.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow);
        };
        CentralUnitService.Current.Activity.LevelChanged += _onActivityChanged;

        CentralUnitWatchdog.Current.Register(Subsystem, TimeSpan.FromMinutes(5),
            () =>
            {
                _pingDue = true;
                CentralUnitService.Current.Flags.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow);
            });

        CentralUnitService.Current.Flags.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow + BaseInterval);
        _loop = Task.Run(() => RunStatsLoopAsync(_cts.Token));

        _ = RefreshAsync();
    }

    public void SetConsumerVisible(bool visible)
    {
        if (_consumerVisible == visible)
        {
            return;
        }

        _consumerVisible = visible;
        if (visible)
        {
            _pingDue = true;
        }

        CentralUnitService.Current.Flags.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow);
    }

    private async Task RunStatsLoopAsync(CancellationToken ct)
    {
        CentralUnitService.ApplyCurrentThreadBackgroundQos();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ = await CentralUnitService.Current.Flags.WaitAsync(CentralFlags.NetworkStatsDue, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CentralUnitService.ApplyCurrentThreadBackgroundQos();

            await RefreshAsync().ConfigureAwait(false);

            TimeSpan interval = CentralUnitService.Current.Activity.AdaptInterval(_consumerVisible ? ActiveInterval : BaseInterval);
            if (interval > TimeSpan.Zero)
            {
                CentralUnitService.Current.Flags.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow + interval);
            }
        }
    }

    private async Task<double> ProbeLatencyAsync()
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync("1.1.1.1", 1000).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkMonitorService.ProbeLatencyAsync");
        }
        return -1;
    }

    private async Task RefreshAsync()
    {
        if (!CentralCircuitBreaker.Current.IsAllowed(Subsystem))
        {
            return;
        }

        try
        {
            DateTime now = DateTime.UtcNow;

            long totalSent = 0, totalReceived = 0;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                IPv4InterfaceStatistics stats = ni.GetIPv4Statistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;
            }

            double elapsed = Math.Max((now - _lastSnapshot).TotalSeconds, 0.001);

            long prevSent = Interlocked.Exchange(ref _prevTotalSent, totalSent);
            long prevReceived = Interlocked.Exchange(ref _prevTotalReceived, totalReceived);

            double uploadKbps = Math.Max(0, (totalSent - prevSent) * 8.0 / 1024.0 / elapsed);
            double downloadKbps = Math.Max(0, (totalReceived - prevReceived) * 8.0 / 1024.0 / elapsed);

            _lastSnapshot = now;

            int tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length;
            double pingMs;
            if (_consumerVisible || _pingDue)
            {
                _pingDue = false;
                pingMs = await ProbeLatencyAsync().ConfigureAwait(false);
                _lastPingUtc = now;
                _lastPingMs = pingMs;
            }
            else
            {
                pingMs = _lastPingMs;
            }

            var snapshot = new NetworkStats(now, totalSent, totalReceived, uploadKbps, downloadKbps, tcpConnections, pingMs);
            _subject.OnNext(snapshot);
            CentralUnitService.Current.Variables.Publish(CentralVariables.NetworkStats, snapshot);
            CentralCircuitBreaker.Current.RecordSuccess(Subsystem);
            CentralUnitWatchdog.Current.Beat(Subsystem);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkMonitorService.RefreshAsync");
            CentralCircuitBreaker.Current.RecordFailure(Subsystem, ex);
            CentralErrorTriage.Current.Record(ex, "NetworkMonitorService.RefreshAsync");
        }
    }

    public void Dispose()
    {
        CentralUnitWatchdog.Current.Unregister(Subsystem);
        CentralUnitService.Current.Activity.LevelChanged -= _onActivityChanged;
        CentralUnitService.Current.Flags.CancelSchedule(CentralFlags.NetworkStatsDue);
        _cts.Cancel();
        try
        {
            _ = _loop.Wait(2000);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkMonitorService.Dispose:Loop");
        }
        _cts.Dispose();
        try
        {
            _subject.OnCompleted();
            _subject.Dispose();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkMonitorService.Dispose:Subject");
        }
        GC.SuppressFinalize(this);
    }
}
