#nullable enable
using System;
using System.Collections.ObjectModel;
using Coclico.Models;
using Xunit;

namespace Coclico.Tests
{
    public class LaunchModeTests
    {
        [Fact]
        public void LaunchMode_Normal_Defined()
        {
            Assert.True(Enum.IsDefined(typeof(LaunchMode), LaunchMode.Normal));
        }

        [Fact]
        public void LaunchMode_Minimized_Defined()
        {
            Assert.True(Enum.IsDefined(typeof(LaunchMode), LaunchMode.Minimized));
        }

        [Fact]
        public void LaunchMode_Tray_Defined()
        {
            Assert.True(Enum.IsDefined(typeof(LaunchMode), LaunchMode.Tray));
        }

        [Fact]
        public void LaunchMode_HasThreeValues()
        {
            var values = Enum.GetValues(typeof(LaunchMode));
            Assert.Equal(3, values.Length);
        }

        [Theory]
        [InlineData("Normal")]
        [InlineData("Minimized")]
        [InlineData("Tray")]
        public void LaunchMode_ParseFromString_Succeeds(string name)
        {
            Assert.True(Enum.TryParse<LaunchMode>(name, out _));
        }
    }

    public class NodeTypeTests
    {
        [Theory]
        [InlineData("Start")]
        [InlineData("End")]
        [InlineData("OpenApp")]
        [InlineData("CloseApp")]
        [InlineData("RunCommand")]
        [InlineData("KillProcess")]
        [InlineData("Delay")]
        [InlineData("Condition")]
        [InlineData("Loop")]
        [InlineData("Notification")]
        [InlineData("HttpRequest")]
        [InlineData("FileOperation")]
        [InlineData("SystemCheck")]
        [InlineData("RunPowerShell")]
        [InlineData("OpenUrl")]
        [InlineData("SetVolume")]
        [InlineData("MuteAudio")]
        [InlineData("CleanTemp")]
        [InlineData("RamClean")]
        [InlineData("ClipboardSet")]
        [InlineData("CompressFile")]
        [InlineData("EmptyRecycleBin")]
        [InlineData("KillByMemory")]
        public void NodeType_AllExpectedValues_AreDefined(string name)
        {
            Assert.True(Enum.TryParse<NodeType>(name, out _));
        }

        [Fact]
        public void NodeType_HasAtLeast20Values()
        {
            Assert.True(Enum.GetValues(typeof(NodeType)).Length >= 20);
        }
    }

    public class ConditionOperatorTests
    {
        [Theory]
        [InlineData("ProcessRunning")]
        [InlineData("ProcessNotRunning")]
        [InlineData("FileExists")]
        [InlineData("FileNotExists")]
        [InlineData("TimeAfter")]
        [InlineData("TimeBefore")]
        [InlineData("CpuBelow")]
        [InlineData("CpuAbove")]
        [InlineData("RamBelow")]
        [InlineData("RamAbove")]
        public void ConditionOperator_AllExpectedValues_AreDefined(string name)
        {
            Assert.True(Enum.TryParse<ConditionOperator>(name, out _));
        }
    }

    public class WorkflowPipelineModelAdditionalTests
    {
        [Fact]
        public void WorkflowPipeline_DefaultId_IsNotEmpty()
        {
            var chain = new WorkflowPipeline();
            Assert.False(string.IsNullOrEmpty(chain.Id));
        }

        [Fact]
        public void WorkflowPipeline_DefaultName_IsNotNull()
        {
            var chain = new WorkflowPipeline();
            Assert.NotNull(chain.Name);
        }

        [Fact]
        public void WorkflowPipeline_Items_IsNotNull()
        {
            var chain = new WorkflowPipeline();
            Assert.NotNull(chain.Items);
        }

        [Fact]
        public void WorkflowPipeline_Connections_IsNotNull()
        {
            var chain = new WorkflowPipeline();
            Assert.NotNull(chain.Connections);
        }

        [Fact]
        public void WorkflowPipeline_Items_CanAddItems()
        {
            var chain = new WorkflowPipeline();
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, DelaySeconds = 100 });
            Assert.Single(chain.Items);
        }

        [Fact]
        public void WorkflowPipeline_DefaultIsRunning_IsFalse()
        {
            var chain = new WorkflowPipeline();
            Assert.False(chain.IsRunning);
        }

        [Fact]
        public void WorkflowPipeline_DefaultAutoTriggerEnabled_IsFalse()
        {
            var chain = new WorkflowPipeline();
            Assert.False(chain.AutoTriggerEnabled);
        }

        [Fact]
        public void WorkflowPipeline_SetName_PropertyChangedFires()
        {
            var chain = new WorkflowPipeline();
            bool fired = false;
            chain.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkflowPipeline.Name)) fired = true; };
            chain.Name = "TestChain";
            Assert.True(fired);
        }

        [Fact]
        public void WorkflowPipeline_SetIsRunning_PropertyChangedFires()
        {
            var chain = new WorkflowPipeline();
            bool fired = false;
            chain.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkflowPipeline.IsRunning)) fired = true; };
            chain.IsRunning = true;
            Assert.True(fired);
        }

        [Fact]
        public void WorkflowPipeline_TwoChains_HaveDifferentIds()
        {
            var a = new WorkflowPipeline();
            var b = new WorkflowPipeline();
            Assert.NotEqual(a.Id, b.Id);
        }
    }

    public class PipelineStepModelTests
    {
        [Fact]
        public void PipelineStep_DefaultNodeType_IsDefined()
        {
            var item = new PipelineStep();
            Assert.True(Enum.IsDefined(typeof(NodeType), item.NodeType));
        }

        [Fact]
        public void PipelineStep_DefaultEnabled_IsTrue()
        {
            var item = new PipelineStep();
            Assert.True(item.IsEnabled);
        }

        [Fact]
        public void PipelineStep_SetDelaySeconds_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.DelaySeconds)) fired = true; };
            item.DelaySeconds = 500;
            Assert.True(fired);
        }

        [Fact]
        public void PipelineStep_SetNodeType_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.NodeType)) fired = true; };
            item.NodeType = NodeType.RunCommand;
            Assert.True(fired);
        }

        [Fact]
        public void PipelineStep_SetCommandLine_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.CommandLine)) fired = true; };
            item.CommandLine = "echo hello";
            Assert.True(fired);
        }

        [Fact]
        public void PipelineStep_DefaultRetryCount_IsZero()
        {
            var item = new PipelineStep();
            Assert.Equal(0, item.RetryCount);
        }

        [Fact]
        public void PipelineStep_DefaultTimeoutSeconds_IsZero()
        {
            var item = new PipelineStep();
            Assert.Equal(0, item.TimeoutSeconds);
        }

        [Fact]
        public void PipelineStep_SetLoopCount_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.LoopCount)) fired = true; };
            item.LoopCount = 3;
            Assert.True(fired);
        }

        [Fact]
        public void PipelineStep_SetConditionOperator_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.ConditionOperator)) fired = true; };
            item.ConditionOperator = ConditionOperator.FileExists;
            Assert.True(fired);
        }

        [Fact]
        public void PipelineStep_SetIsEnabled_PropertyChangedFires()
        {
            var item = new PipelineStep();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineStep.IsEnabled)) fired = true; };
            item.IsEnabled = false;
            Assert.True(fired);
        }
    }

    public class PipelineConnectionModelTests
    {
        [Fact]
        public void PipelineConnection_DefaultLabel_AccessDoesNotThrow()
        {
            var conn = new PipelineConnection();
            var ex = Record.Exception(() => { _ = conn.Label; });
            Assert.Null(ex);
        }

        [Fact]
        public void PipelineConnection_SetLabel_PropertyChangedFires()
        {
            var conn = new PipelineConnection();
            bool fired = false;
            conn.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineConnection.Label)) fired = true; };
            conn.Label = "Yes";
            Assert.True(fired);
        }

        [Fact]
        public void PipelineConnection_SetStartItem_PropertyChangedFires()
        {
            var conn = new PipelineConnection();
            bool fired = false;
            conn.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PipelineConnection.StartItem)) fired = true; };
            conn.StartItem = new PipelineStep();
            Assert.True(fired);
        }
    }

}
