#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class CleaningServiceTests
    {
        [Fact]
        public void GetAvailableCategories_ReturnsNonEmptyList()
        {
            var service = new CleaningService();
            var categories = service.GetAvailableCategories();

            Assert.NotNull(categories);
            Assert.NotEmpty(categories);
            Assert.All(categories, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
                Assert.False(string.IsNullOrWhiteSpace(c.Icon));
            });
        }

        [Fact]
        public async Task EstimateCleanableBytesAsync_ReturnsNonNegative()
        {
            var service = new CleaningService();
            var bytes = await service.EstimateCleanableBytesAsync(CancellationToken.None);
            Assert.True(bytes >= 0, "Estimated cleanable bytes should be non-negative.");
        }

        [Fact]
        public async Task ExecuteDeepCleanAsync_WithEmptyCategories_ReturnsResult()
        {
            var service = new CleaningService();
            var emptyCategories = new List<CleaningService.CleaningCategory>();

            var result = await service.ExecuteDeepCleanAsync(emptyCategories, null, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(0, result.FilesDeleted);
        }

        [Fact]
        public async Task ExecuteDeepCleanAsync_CanBeCancelled()
        {
            var service = new CleaningService();
            var categories = service.GetAvailableCategories().Take(1).ToList();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.ExecuteDeepCleanAsync(categories, null, cts.Token));
        }

        [Fact]
        public void GetAvailableCategories_AllSelectedByDefault()
        {
            var service = new CleaningService();
            var categories = service.GetAvailableCategories();
            Assert.All(categories, c => Assert.True(c.IsSelected));
        }
    }
}
