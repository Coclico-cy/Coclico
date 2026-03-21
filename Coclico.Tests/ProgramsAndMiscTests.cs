using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class InstalledProgramsServiceTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(InstalledProgramsService.Instance);
        }

        [Fact]
        public void GetCategories_ReturnsNonNull()
        {
            var cats = InstalledProgramsService.Instance.GetCategories();
            Assert.NotNull(cats);
        }

        [Fact]
        public void GetFilterGroups_ReturnsNonNull()
        {
            var groups = InstalledProgramsService.Instance.GetFilterGroups();
            Assert.NotNull(groups);
        }

        [Fact]
        public void GetMemoryCacheIconPaths_ReturnsNonNull()
        {
            var paths = InstalledProgramsService.Instance.GetMemoryCacheIconPaths();
            Assert.NotNull(paths);
        }

        [Fact]
        public void ProgramInfo_DefaultValues()
        {
            var info = new InstalledProgramsService.ProgramInfo();
            Assert.Equal(string.Empty, info.Name);
            Assert.Equal(string.Empty, info.InstallPath);
            Assert.Equal(string.Empty, info.ExePath);
            Assert.Equal("Windows", info.Source);
            Assert.Equal(string.Empty, info.Publisher);
            Assert.Equal(string.Empty, info.Version);
            Assert.Equal(0L, info.SizeBytes);
        }

        [Fact]
        public void DetectGames_WithEmptyList_ReturnsEmpty()
        {
            var games = InstalledProgramsService.DetectGames(new System.Collections.Generic.List<InstalledProgramsService.ProgramInfo>());
            Assert.NotNull(games);
            Assert.Empty(games);
        }

        [Fact]
        public void FilterGroup_DefaultIsStaticFalse()
        {
            var fg = new InstalledProgramsService.FilterGroup();
            Assert.False(fg.IsStatic);
        }

        [Fact]
        public async Task GetAllInstalledProgramsAsync_ReturnsNonNull()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var programs = await InstalledProgramsService.Instance.GetAllInstalledProgramsAsync(
                forceRefresh: false, cancellationToken: cts.Token);
            Assert.NotNull(programs);
        }
    }

    public class InstallerServiceTests
    {
        [Fact]
        public void GetAvailableSoftware_ReturnsNonEmpty()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.NotNull(items);
            Assert.NotEmpty(items);
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveName()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveWingetId()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.WingetId)));
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveCategory()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.Category)));
        }

        [Fact]
        public void IsRunAsAdmin_ReturnsBoolWithoutThrowing()
        {
            var svc = new InstallerService();
            var ex = Record.Exception(() => svc.IsRunAsAdmin());
            Assert.Null(ex);
        }

        [Fact]
        public void SoftwareItem_DefaultStatusIsNonEmpty()
        {
            var item = new InstallerService.SoftwareItem { Name = "Test", WingetId = "test.id" };
            Assert.False(string.IsNullOrWhiteSpace(item.Status));
            Assert.Equal(0, item.ProgressValue);
        }
    }

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

    public class ProcessWatcherServiceTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(ProcessWatcherService.Instance);
        }

        [Fact]
        public void KillProcess_NonExistentProcess_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
                ProcessWatcherService.Instance.KillProcess("this_process_does_not_exist_xyz"));
            Assert.Null(ex);
        }
    }

    public class CacheServiceExtendedTests
    {
        [Fact]
        public async Task Has_AfterSet_ReturnsTrue()
        {
            var key = "has_test_" + Guid.NewGuid().ToString("N");
            try
            {
                await CacheService.Instance.SetAsync(key, 42, TimeSpan.FromMinutes(5));
                Assert.True(await CacheService.Instance.HasAsync(key));
            }
            finally
            {
                await CacheService.Instance.InvalidateAsync(key);
            }
        }

        [Fact]
        public async Task Has_AfterInvalidate_ReturnsFalse()
        {
            var key = "has_inv_" + Guid.NewGuid().ToString("N");
            await CacheService.Instance.SetAsync(key, 42, TimeSpan.FromMinutes(5));
            await CacheService.Instance.InvalidateAsync(key);

            Assert.False(await CacheService.Instance.HasAsync(key));
        }

        [Fact]
        public async Task Get_NonExistentKey_ReturnsDefault()
        {
            var key = "missing_" + Guid.NewGuid().ToString("N");
            var result = await CacheService.Instance.GetAsync<string>(key);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetCacheSizeBytes_ReturnsNonNegative()
        {
            var size = CacheService.Instance.GetCacheSizeBytes();
            Assert.True(size >= 0);
        }

        [Fact]
        public async Task Set_OverwritesExistingValue()
        {
            var key = "overwrite_" + Guid.NewGuid().ToString("N");
            try
            {
                await CacheService.Instance.SetAsync(key, "first", TimeSpan.FromMinutes(5));
                await CacheService.Instance.SetAsync(key, "second", TimeSpan.FromMinutes(5));

                var result = await CacheService.Instance.GetAsync<string>(key);
                Assert.Equal("second", result);
            }
            finally
            {
                await CacheService.Instance.InvalidateAsync(key);
            }
        }

        [Fact]
        public async Task Set_WithSubdir_CanBeRetrieved()
        {
            var subdir = "test_subdir";
            var key = "subkey_" + Guid.NewGuid().ToString("N");
            try
            {
                await Task.Run(() => CacheService.Instance.Set(subdir, key, "value123", TimeSpan.FromMinutes(5)));
                var result = CacheService.Instance.Get<string>(subdir, key);
                Assert.Equal("value123", result);
            }
            finally
            {
                try { CacheService.Instance.ClearSubdir(subdir); } catch { }
            }
        }

        [Fact]
        public void GetCacheSizeBytes_DoesNotThrow()
        {
            var ex = Record.Exception(() => CacheService.Instance.GetCacheSizeBytes());
            Assert.Null(ex);
        }
    }

    public class NetworkMonitorServiceTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(NetworkMonitorService.Instance);
        }

        [Fact]
        public void InitialStats_AreValid()
        {
            var stats = NetworkMonitorService.Instance.CurrentStats;
            Assert.NotEqual(default, stats.Timestamp);
            Assert.True(stats.TotalBytesSent >= 0);
            Assert.True(stats.TotalBytesReceived >= 0);
        }

        [Fact]
        public async Task StatsStream_ProducesAtLeastOneValueWithinTimeout()
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            bool gotValue = false;

            using var subscription = NetworkMonitorService.Instance.StatsStream
                .Subscribe(value => gotValue = true);

            while (!gotValue && !cts.Token.IsCancellationRequested)
                await Task.Delay(100, cts.Token);

            Assert.True(gotValue, "NetworkMonitorService did not emit a stats update in expected time.");
        }
    }
}
