#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Models;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class WorkflowServiceCrudTests
    {
        [Fact]
        public void GetWorkflowPipelines_ReturnsNonNull()
        {
            var svc = new WorkflowService();
            var chains = svc.GetWorkflowPipelines();
            Assert.NotNull(chains);
        }

        [Fact]
        public void GetWorkflowPipelines_ReturnsObservableCollection()
        {
            var svc = new WorkflowService();
            var chains = svc.GetWorkflowPipelines();
            Assert.IsAssignableFrom<ObservableCollection<WorkflowPipeline>>(chains);
        }

        [Fact]
        public void SaveAndGetWorkflowPipelines_RoundTrip()
        {
            var svc = new WorkflowService();
            var original = svc.GetWorkflowPipelines();
            var testName = "UnitTestChain_" + Guid.NewGuid().ToString("N")[..6];

            var testChain = new WorkflowPipeline { Name = testName };
            original.Add(testChain);

            svc.SaveWorkflowPipelines(original);

            var reloaded = svc.GetWorkflowPipelines();
            Assert.Contains(reloaded, c => c.Name == testName);

            reloaded.Remove(reloaded[reloaded.Count - 1]);
            svc.SaveWorkflowPipelines(reloaded);
        }

        [Fact]
        public void SaveWorkflowPipelines_Empty_DoesNotThrow()
        {
            var svc = new WorkflowService();
            var ex = Record.Exception(() =>
                svc.SaveWorkflowPipelines(new ObservableCollection<WorkflowPipeline>()));
            Assert.Null(ex);
        }
    }

    public class WorkflowServiceTests
    {
        private static WorkflowPipeline MakeChain(params NodeType[] types)
        {
            var chain = new WorkflowPipeline { Name = "Test" };
            foreach (var t in types)
                chain.Items.Add(new PipelineStep { NodeType = t, Name = t.ToString(), IsEnabled = true });
            return chain;
        }

        [Fact]
        public async Task NullChain_ReturnsSafeResult()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(null!);
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        }

        [Fact]
        public async Task EmptyChain_SucceedsWithZeroNodes()
        {
            var svc = new WorkflowService();
            var chain = new WorkflowPipeline { Name = "Empty" };
            var result = await svc.ExecuteChainAsync(chain);

            Assert.True(result.Success);
            Assert.Equal(0, result.NodesExecuted);
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task StartEndChain_ExecutesBothNodes()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);

            var result = await svc.ExecuteChainAsync(chain);

            Assert.True(result.Success);
            Assert.Equal(2, result.NodesExecuted);
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task ChainWithZeroDelayNode_ExecutesSuccessfully()
        {
            var svc = new WorkflowService();
            var chain = new WorkflowPipeline { Name = "DelayTest" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Start, Name = "Start", IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "Wait", IsEnabled = true, DelaySeconds = 0 });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.End, Name = "End", IsEnabled = true });

            var result = await svc.ExecuteChainAsync(chain);

            Assert.True(result.Success);
            Assert.Equal(3, result.NodesExecuted);
        }

        [Fact]
        public async Task DisabledNodes_AreSkipped()
        {
            var svc = new WorkflowService();
            var chain = new WorkflowPipeline { Name = "DisabledTest" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Start, Name = "Start", IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "Disabled", IsEnabled = false });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.End, Name = "End", IsEnabled = true });

            var result = await svc.ExecuteChainAsync(chain);

            Assert.Equal(2, result.NodesExecuted);
        }

        [Fact]
        public async Task CancelledBeforeExecution_ReturnsWithZeroExecuted()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.Delay, NodeType.End);
            chain.Items[1].DelaySeconds = 60;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await svc.ExecuteChainAsync(chain, null, cts.Token);

            Assert.Equal(0, result.NodesExecuted);
        }

        [Fact]
        public async Task ProgressCallback_IsInvoked()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);
            var reports = new List<(string, int)>();
            var progress = new Progress<(string, int)>(r => reports.Add(r));

            await svc.ExecuteChainAsync(chain, progress);

            await Task.Delay(50);
            Assert.NotEmpty(reports);
        }

        [Fact]
        public async Task NodeExecutingEvent_IsRaised()
        {
            var svc = new WorkflowService();
            var chain = new WorkflowPipeline { Name = "EventTest" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "Delay", IsEnabled = true, DelaySeconds = 0 });

            var fired = false;
            svc.NodeExecuting += (_, _) => fired = true;

            await svc.ExecuteChainAsync(chain);

            Assert.True(fired);
        }

        [Fact]
        public async Task NodeCompletedEvent_IsRaised()
        {
            var svc = new WorkflowService();
            var chain = new WorkflowPipeline { Name = "EventTest" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "Delay", IsEnabled = true, DelaySeconds = 0 });

            bool? completedSuccess = null;
            svc.NodeCompleted += (_, success, _) => completedSuccess = success;

            await svc.ExecuteChainAsync(chain);

            Assert.NotNull(completedSuccess);
            Assert.True(completedSuccess);
        }

        [Fact]
        public async Task ExecutionResult_ElapsedTimeIsPositive()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);

            var result = await svc.ExecuteChainAsync(chain);

            Assert.True(result.ElapsedTime.TotalMilliseconds >= 0);
        }

        [Fact]
        public async Task ExecutionResult_SummaryIsNotEmpty()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);

            var result = await svc.ExecuteChainAsync(chain);

            Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        }

        [Fact]
        public async Task Chain_IsRunning_SetToFalseAfterExecution()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);

            await svc.ExecuteChainAsync(chain);

            Assert.False(chain.IsRunning);
        }

        [Fact]
        public async Task Chain_LastRunTime_SetAfterExecution()
        {
            var svc = new WorkflowService();
            var chain = MakeChain(NodeType.Start, NodeType.End);
            var before = DateTime.UtcNow;

            await svc.ExecuteChainAsync(chain);

            Assert.True(chain.LastRunTime >= before);
        }
    }

    public class ExecutionResultTests
    {
        [Fact]
        public void ExecutionResult_DefaultValues()
        {
            var result = new WorkflowService.ExecutionResult();
            Assert.False(result.Success);
            Assert.Equal(0, result.NodesExecuted);
            Assert.Equal(0, result.NodesFailed);
            Assert.Equal(0, result.NodesSkipped);
            Assert.Equal(string.Empty, result.Summary);
        }

        [Fact]
        public void ExecutionResult_IsImmutable()
        {
            var result = new WorkflowService.ExecutionResult
            {
                Success = true,
                NodesExecuted = 5,
                NodesFailed = 1,
                NodesSkipped = 0,
                Summary = "Done"
            };

            Assert.True(result.Success);
            Assert.Equal(5, result.NodesExecuted);
        }
    }
}
