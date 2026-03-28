#nullable enable
using System;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class CacheServiceExtendedTests
    {
        [Fact]
        public async Task Has_AfterSet_ReturnsTrue()
        {
            var key = "has_test_" + Guid.NewGuid().ToString("N");
            try
            {
                await new CacheService().SetAsync(key, 42, TimeSpan.FromMinutes(5));
                Assert.True(await new CacheService().HasAsync(key));
            }
            finally
            {
                await new CacheService().InvalidateAsync(key);
            }
        }

        [Fact]
        public async Task Has_AfterInvalidate_ReturnsFalse()
        {
            var key = "has_inv_" + Guid.NewGuid().ToString("N");
            await new CacheService().SetAsync(key, 42, TimeSpan.FromMinutes(5));
            await new CacheService().InvalidateAsync(key);

            Assert.False(await new CacheService().HasAsync(key));
        }

        [Fact]
        public async Task Get_NonExistentKey_ReturnsDefault()
        {
            var key = "missing_" + Guid.NewGuid().ToString("N");
            var result = await new CacheService().GetAsync<string>(key);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetCacheSizeBytes_ReturnsNonNegative()
        {
            var size = new CacheService().GetCacheSizeBytes();
            Assert.True(size >= 0);
        }

        [Fact]
        public async Task Set_OverwritesExistingValue()
        {
            var key = "overwrite_" + Guid.NewGuid().ToString("N");
            try
            {
                await new CacheService().SetAsync(key, "first", TimeSpan.FromMinutes(5));
                await new CacheService().SetAsync(key, "second", TimeSpan.FromMinutes(5));

                var result = await new CacheService().GetAsync<string>(key);
                Assert.Equal("second", result);
            }
            finally
            {
                await new CacheService().InvalidateAsync(key);
            }
        }

        [Fact]
        public async Task Set_WithSubdir_CanBeRetrieved()
        {
            var subdir = "test_subdir";
            var key = "subkey_" + Guid.NewGuid().ToString("N");
            try
            {
                await Task.Run(() => new CacheService().Set(subdir, key, "value123", TimeSpan.FromMinutes(5)));
                var result = new CacheService().Get<string>(subdir, key);
                Assert.Equal("value123", result);
            }
            finally
            {
                try { new CacheService().ClearSubdir(subdir); } catch { }
            }
        }

        [Fact]
        public void GetCacheSizeBytes_DoesNotThrow()
        {
            var ex = Record.Exception(() => new CacheService().GetCacheSizeBytes());
            Assert.Null(ex);
        }
    }
}
