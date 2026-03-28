#nullable enable
using System;
using Coclico.Models;
using Xunit;

namespace Coclico.Tests
{
    public class WorkflowPipelineModelTests
    {
        [Fact]
        public void WorkflowPipeline_DefaultValues_AreCorrect()
        {
            var chain = new WorkflowPipeline { Name = "Test Chain" };
            Assert.Equal("Test Chain", chain.Name);
            Assert.NotNull(chain.Items);
        }

        [Fact]
        public void PipelineStep_NodeTypes_AreExtended()
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
        public void WorkflowPipeline_Id_IsGeneratedAutomatically()
        {
            var chain1 = new WorkflowPipeline();
            var chain2 = new WorkflowPipeline();
            Assert.False(string.IsNullOrWhiteSpace(chain1.Id));
            Assert.NotEqual(chain1.Id, chain2.Id);
        }

        [Fact]
        public void PipelineStep_IsEnabledByDefault()
        {
            var item = new PipelineStep();
            Assert.True(item.IsEnabled);
        }
    }
}
