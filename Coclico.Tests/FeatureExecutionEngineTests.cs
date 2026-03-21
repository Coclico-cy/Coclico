using System;
using System.Linq;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class FeatureExecutionEngineTests
    {
        [Fact]
        public async Task RunFeatureAsync_SetsSuccessState()
        {
            var name = "TestFeature_RunFeatureAsync_Success_" + Guid.NewGuid().ToString("N");
            var engine = FeatureExecutionEngine.Instance;

            var result = await engine.RunFeatureAsync(name, async ctx =>
            {
                ctx.Report("Step 1");
                await Task.Delay(1);
                ctx.Report("Step 2");
                return true;
            });

            Assert.True(result);

            var state = engine.GetState(name);
            Assert.NotNull(state);
            Assert.Equal(FeatureExecutionStatus.Success, state!.Status);
            Assert.Equal("Success", state.Message);
            Assert.True(state.Duration >= TimeSpan.Zero);
            Assert.Contains(state.LogHistory, l => l.Contains("Step 1"));
        }

        [Fact]
        public async Task RunFeatureAsync_WithException_SetsErrorState()
        {
            var name = "TestFeature_RunFeatureAsync_Exception_" + Guid.NewGuid().ToString("N");
            var engine = FeatureExecutionEngine.Instance;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await engine.RunFeatureAsync<bool>(name, async ctx => throw new InvalidOperationException("Boom")));

            var state = engine.GetState(name);
            Assert.NotNull(state);
            Assert.Equal(FeatureExecutionStatus.Error, state!.Status);
            Assert.Contains("Boom", state.Message);
        }

        [Fact]
        public async Task FlowChainExecutionService_IntegratesFeatureEngine()
        {
            var svc = new FlowChainExecutionService();
            var chain = new Models.FlowChain { Name = "FeatureEngineChainTest" };
            chain.Items.Add(new Models.FlowItem { NodeType = Models.NodeType.Start, Name = "Start", IsEnabled = true });
            chain.Items.Add(new Models.FlowItem { NodeType = Models.NodeType.Delay, Name = "Delay", IsEnabled = true, DelaySeconds = 0 });
            chain.Items.Add(new Models.FlowItem { NodeType = Models.NodeType.End, Name = "End", IsEnabled = true });

            var result = await svc.ExecuteChainAsync(chain);

            Assert.True(result.Success);
            var state = FeatureExecutionEngine.Instance.GetState($"FlowChain:{chain.Name}");
            Assert.NotNull(state);
            Assert.Equal(FeatureExecutionStatus.Success, state!.Status);
            Assert.NotEmpty(state.LogHistory);
        }
    }
}
