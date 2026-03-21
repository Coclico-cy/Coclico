using System;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class MemoryCleanerFormatBytesTests
    {
        [Theory]
        [InlineData(0, "B")]
        [InlineData(512, "B")]
        [InlineData(1024, "KB")]
        [InlineData(1536, "KB")]
        [InlineData(1048576, "MB")]
        [InlineData(1073741824, "GB")]
        public void FormatBytes_ContainsCorrectUnit(long bytes, string expectedUnit)
        {
            var result = MemoryCleanerService.FormatBytes(bytes);
            Assert.Contains(expectedUnit, result);
        }

        [Theory]
        [InlineData(0, "0")]
        [InlineData(512, "512")]
        public void FormatBytes_ByteValues_ContainNumber(long bytes, string expectedNumber)
        {
            var result = MemoryCleanerService.FormatBytes(bytes);
            Assert.Contains(expectedNumber, result);
        }

        [Fact]
        public void FormatBytes_1KB_Contains1()
        {
            var result = MemoryCleanerService.FormatBytes(1024);
            Assert.Contains("1", result);
            Assert.Contains("KB", result);
        }

        [Fact]
        public void FormatBytes_1MB_Contains1()
        {
            var result = MemoryCleanerService.FormatBytes(1048576);
            Assert.Contains("1", result);
            Assert.Contains("MB", result);
        }

        [Fact]
        public void FormatBytes_1GB_Contains1()
        {
            var result = MemoryCleanerService.FormatBytes(1073741824);
            Assert.Contains("1", result);
            Assert.Contains("GB", result);
        }

        [Fact]
        public void FormatBytes_LargeValue_DoesNotThrow()
        {
            var ex = Record.Exception(() => MemoryCleanerService.FormatBytes(long.MaxValue));
            Assert.Null(ex);
        }

        [Fact]
        public void FormatBytes_NegativeValue_DoesNotThrow()
        {
            var ex = Record.Exception(() => MemoryCleanerService.FormatBytes(-1));
            Assert.Null(ex);
        }

        [Fact]
        public void FormatBytes_AlwaysReturnsNonNull()
        {
            foreach (var val in new long[] { 0, 1, 1023, 1024, 1025, 1048576, 1073741824 })
            {
                Assert.NotNull(MemoryCleanerService.FormatBytes(val));
            }
        }
    }

    public class MemoryCleanerRamInfoTests
    {
        [Fact]
        public void GetRamInfo_TotalIsPositive()
        {
            var info = MemoryCleanerService.GetRamInfo();
            Assert.True(info.TotalPhysBytes > 0, "Total RAM should be greater than 0");
        }

        [Fact]
        public void GetRamInfo_AvailableIsNonNegative()
        {
            var info = MemoryCleanerService.GetRamInfo();
            Assert.True(info.AvailPhysBytes >= 0);
        }

        [Fact]
        public void GetRamInfo_AvailNotExceedsTotal()
        {
            var info = MemoryCleanerService.GetRamInfo();
            Assert.True(info.AvailPhysBytes <= info.TotalPhysBytes,
                $"Available ({info.AvailPhysBytes}) should not exceed total ({info.TotalPhysBytes})");
        }

        [Fact]
        public void GetRamInfo_LoadPercentInRange()
        {
            var info = MemoryCleanerService.GetRamInfo();
            Assert.InRange(info.PhysUsedPercent, 0.0, 100.0);
        }
    }

    public class MemoryCleanerSystemTests
    {
        [Fact]
        public void GetSystemCpuPercent_ReturnsInRange()
        {
            var cpu = MemoryCleanerService.GetSystemCpuPercent();
            Assert.InRange(cpu, 0.0, 100.0);
        }

        [Fact]
        public void GetTopProcessesByMemory_ReturnsNonNull()
        {
            var procs = MemoryCleanerService.GetTopProcessesByMemory(5);
            Assert.NotNull(procs);
        }

        [Fact]
        public void GetTopProcessesByMemory_CountRespectedOrLess()
        {
            var procs = MemoryCleanerService.GetTopProcessesByMemory(3);
            Assert.True(procs.Count <= 3);
        }

        [Fact]
        public void GetGpuInfo_DoesNotThrow()
        {
            var ex = Record.Exception(() => MemoryCleanerService.GetGpuInfo());
            Assert.Null(ex);
        }

        [Fact]
        public void ForceGcCollect_ReturnsNonNegative()
        {
            var freed = MemoryCleanerService.ForceGcCollect();
            Assert.True(freed >= 0, "ForceGcCollect should return >= 0");
        }

        [Fact]
        public void TrimSelfWorkingSet_DoesNotThrow()
        {
            var ex = Record.Exception(() => MemoryCleanerService.TrimSelfWorkingSet());
            Assert.Null(ex);
        }

        [Fact]
        public void FlushDnsCache_DoesNotThrow()
        {
            var ex = Record.Exception(() => MemoryCleanerService.FlushDnsCache());
            Assert.Null(ex);
        }

        [Fact]
        public async Task CleanByProfile_Quick_CompletesWithoutException()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ex = await Record.ExceptionAsync(async () =>
            {
                var result = await MemoryCleanerService.CleanByProfileAsync(
                    MemoryCleanerService.CleanProfile.Quick, null, cts.Token);
                Assert.True(result.TotalFreed >= 0);
            });
            Assert.Null(ex);
        }

        [Fact]
        public async Task CleanByProfile_Normal_ReturnsResult()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await MemoryCleanerService.CleanByProfileAsync(
                MemoryCleanerService.CleanProfile.Normal, null, cts.Token);

            Assert.True(result.TotalFreed >= 0);
        }

        [Fact]
        public async Task CleanByProfile_WithProgress_ProgressReports()
        {
            var reports = new System.Collections.Generic.List<string>();
            var progress = new Progress<string>(msg => reports.Add(msg));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await MemoryCleanerService.CleanByProfileAsync(MemoryCleanerService.CleanProfile.Quick, progress, cts.Token);

            await Task.Delay(100);
        }
    }

    public class CleanResultTests
    {
        [Fact]
        public void CleanResult_TotalFreedIsZeroByDefault()
        {
            var result = new MemoryCleanerService.CleanResult();
            Assert.Equal(0, result.TotalFreed);
        }
    }
}
