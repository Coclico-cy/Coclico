using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Models;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class DeepCleaningServiceTests
    {
        [Fact]
        public void GetAvailableCategories_ReturnsNonEmptyList()
        {
            var service = new DeepCleaningService();
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
            var service = new DeepCleaningService();
            var bytes = await service.EstimateCleanableBytesAsync(CancellationToken.None);
            Assert.True(bytes >= 0, "Estimated cleanable bytes should be non-negative.");
        }

        [Fact]
        public async Task ExecuteDeepCleanAsync_WithEmptyCategories_ReturnsResult()
        {
            var service = new DeepCleaningService();
            var emptyCategories = new List<DeepCleaningService.CleaningCategory>();

            var result = await service.ExecuteDeepCleanAsync(emptyCategories, null, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(0, result.FilesDeleted);
        }

        [Fact]
        public async Task ExecuteDeepCleanAsync_CanBeCancelled()
        {
            var service = new DeepCleaningService();
            var categories = service.GetAvailableCategories().Take(1).ToList();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.ExecuteDeepCleanAsync(categories, null, cts.Token));
        }

        [Fact]
        public void GetAvailableCategories_AllSelectedByDefault()
        {
            var service = new DeepCleaningService();
            var categories = service.GetAvailableCategories();
            Assert.All(categories, c => Assert.True(c.IsSelected));
        }
    }

    public class FlowChainModelTests
    {
        [Fact]
        public void FlowChain_DefaultValues_AreCorrect()
        {
            var chain = new FlowChain { Name = "Test Chain" };
            Assert.Equal("Test Chain", chain.Name);
            Assert.NotNull(chain.Items);
        }

        [Fact]
        public void FlowItem_NodeTypes_AreExtended()
        {
            var values = Enum.GetValues<NodeType>();
            Assert.Contains(NodeType.Condition, values);
            Assert.Contains(NodeType.Loop, values);
            Assert.Contains(NodeType.Parallel, values);
            Assert.Contains(NodeType.HttpRequest, values);
            Assert.Contains(NodeType.FileOperation, values);
            Assert.Contains(NodeType.Notification, values);
            Assert.Contains(NodeType.SystemCheck, values);
        }

        [Fact]
        public void FlowChain_Id_IsGeneratedAutomatically()
        {
            var chain1 = new FlowChain();
            var chain2 = new FlowChain();
            Assert.False(string.IsNullOrWhiteSpace(chain1.Id));
            Assert.NotEqual(chain1.Id, chain2.Id);
        }

        [Fact]
        public void FlowItem_IsEnabledByDefault()
        {
            var item = new FlowItem();
            Assert.True(item.IsEnabled);
        }
    }
}
