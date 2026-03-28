#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class NetworkMonitorServiceTests
    {
        [Fact]
        public void CanBeInstantiated()
        {
            Assert.NotNull(new NetworkMonitorService());
        }

        [Fact]
        public void InitialStats_AreValid()
        {
            var svc = new NetworkMonitorService();
            var stats = svc.CurrentStats;
            Assert.NotEqual(default, stats.Timestamp);
            Assert.True(stats.TotalBytesSent >= 0);
            Assert.True(stats.TotalBytesReceived >= 0);
        }

        [Fact]
        public async Task StatsStream_ProducesAtLeastOneValueWithinTimeout()
        {
            using var svc = new NetworkMonitorService();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            bool gotValue = false;

            using var subscription = svc.StatsStream
                .Subscribe(value => gotValue = true);

            while (!gotValue && !cts.Token.IsCancellationRequested)
                await Task.Delay(100, cts.Token);

            Assert.True(gotValue, "NetworkMonitorService did not emit a stats update in expected time.");
        }
    }
}
