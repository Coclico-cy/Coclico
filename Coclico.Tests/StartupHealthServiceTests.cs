#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class StartupHealthServiceTests
    {
        [Fact]
        public async Task CheckAndRepairAsync_ReturnsHealthReport()
        {
            var svc = new StartupHealthService();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            StartupHealthService.HealthReport? report = null;
            var ex = await Record.ExceptionAsync(async () =>
                report = await svc.CheckAndRepairAsync());

            Assert.Null(ex);
            Assert.NotNull(report);
        }

        [Fact]
        public async Task CheckAndRepairAsync_ReportHasMessage()
        {
            var svc = new StartupHealthService();
            var report = await svc.CheckAndRepairAsync();

            Assert.NotNull(report.Message);
        }
    }
}
