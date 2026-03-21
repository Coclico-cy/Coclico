using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class CacheAndSettingsTests
    {
        [Fact]
        public async Task Cache_SetGet_Works()
        {
            var key = "unittest_cache_key" + Guid.NewGuid().ToString("N");
            var data = new List<int> { 1, 2, 3 };
            await CacheService.Instance.SetAsync(key, data, TimeSpan.FromMinutes(5));

            var read = await CacheService.Instance.GetAsync<List<int>>(key);
            Assert.NotNull(read);
            Assert.Equal(3, read.Count);

            try { await CacheService.Instance.InvalidateAsync(key); } catch { }
        }

        [Fact]
        public async Task Settings_SaveLoad_Persists()
        {
            var svc = SettingsService.Instance;

            // Backup current value
            var original = svc.Settings.LaunchAtStartup;
            try
            {
                svc.Settings.LaunchAtStartup = !original;
                await svc.SaveAsync();

                await svc.LoadAsync();

                Assert.Equal(!original, svc.Settings.LaunchAtStartup);
            }
            finally
            {
                svc.Settings.LaunchAtStartup = original;
                await svc.SaveAsync();
            }
        }
    }
}
