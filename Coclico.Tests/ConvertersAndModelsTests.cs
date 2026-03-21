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

    public class FlowChainModelAdditionalTests
    {
        [Fact]
        public void FlowChain_DefaultId_IsNotEmpty()
        {
            var chain = new FlowChain();
            Assert.False(string.IsNullOrEmpty(chain.Id));
        }

        [Fact]
        public void FlowChain_DefaultName_IsNotNull()
        {
            var chain = new FlowChain();
            Assert.NotNull(chain.Name);
        }

        [Fact]
        public void FlowChain_Items_IsNotNull()
        {
            var chain = new FlowChain();
            Assert.NotNull(chain.Items);
        }

        [Fact]
        public void FlowChain_Connections_IsNotNull()
        {
            var chain = new FlowChain();
            Assert.NotNull(chain.Connections);
        }

        [Fact]
        public void FlowChain_Items_CanAddItems()
        {
            var chain = new FlowChain();
            chain.Items.Add(new FlowItem { NodeType = NodeType.Delay, DelaySeconds = 100 });
            Assert.Single(chain.Items);
        }

        [Fact]
        public void FlowChain_DefaultIsRunning_IsFalse()
        {
            var chain = new FlowChain();
            Assert.False(chain.IsRunning);
        }

        [Fact]
        public void FlowChain_DefaultAutoTriggerEnabled_IsFalse()
        {
            var chain = new FlowChain();
            Assert.False(chain.AutoTriggerEnabled);
        }

        [Fact]
        public void FlowChain_SetName_PropertyChangedFires()
        {
            var chain = new FlowChain();
            bool fired = false;
            chain.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowChain.Name)) fired = true; };
            chain.Name = "TestChain";
            Assert.True(fired);
        }

        [Fact]
        public void FlowChain_SetIsRunning_PropertyChangedFires()
        {
            var chain = new FlowChain();
            bool fired = false;
            chain.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowChain.IsRunning)) fired = true; };
            chain.IsRunning = true;
            Assert.True(fired);
        }

        [Fact]
        public void FlowChain_TwoChains_HaveDifferentIds()
        {
            var a = new FlowChain();
            var b = new FlowChain();
            Assert.NotEqual(a.Id, b.Id);
        }
    }

    public class FlowItemModelTests
    {
        [Fact]
        public void FlowItem_DefaultNodeType_IsDefined()
        {
            var item = new FlowItem();
            Assert.True(Enum.IsDefined(typeof(NodeType), item.NodeType));
        }

        [Fact]
        public void FlowItem_DefaultEnabled_IsTrue()
        {
            var item = new FlowItem();
            Assert.True(item.IsEnabled);
        }

        [Fact]
        public void FlowItem_SetDelaySeconds_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.DelaySeconds)) fired = true; };
            item.DelaySeconds = 500;
            Assert.True(fired);
        }

        [Fact]
        public void FlowItem_SetNodeType_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.NodeType)) fired = true; };
            item.NodeType = NodeType.RunCommand;
            Assert.True(fired);
        }

        [Fact]
        public void FlowItem_SetCommandLine_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.CommandLine)) fired = true; };
            item.CommandLine = "echo hello";
            Assert.True(fired);
        }

        [Fact]
        public void FlowItem_DefaultRetryCount_IsZero()
        {
            var item = new FlowItem();
            Assert.Equal(0, item.RetryCount);
        }

        [Fact]
        public void FlowItem_DefaultTimeoutSeconds_IsZero()
        {
            var item = new FlowItem();
            Assert.Equal(0, item.TimeoutSeconds);
        }

        [Fact]
        public void FlowItem_SetLoopCount_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.LoopCount)) fired = true; };
            item.LoopCount = 3;
            Assert.True(fired);
        }

        [Fact]
        public void FlowItem_SetConditionOperator_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.ConditionOperator)) fired = true; };
            item.ConditionOperator = ConditionOperator.FileExists;
            Assert.True(fired);
        }

        [Fact]
        public void FlowItem_SetIsEnabled_PropertyChangedFires()
        {
            var item = new FlowItem();
            bool fired = false;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowItem.IsEnabled)) fired = true; };
            item.IsEnabled = false;
            Assert.True(fired);
        }
    }

    public class FlowConnectionModelTests
    {
        [Fact]
        public void FlowConnection_DefaultLabel_AccessDoesNotThrow()
        {
            var conn = new FlowConnection();
            var ex = Record.Exception(() => { _ = conn.Label; });
            Assert.Null(ex);
        }

        [Fact]
        public void FlowConnection_SetLabel_PropertyChangedFires()
        {
            var conn = new FlowConnection();
            bool fired = false;
            conn.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowConnection.Label)) fired = true; };
            conn.Label = "Yes";
            Assert.True(fired);
        }

        [Fact]
        public void FlowConnection_SetStartItem_PropertyChangedFires()
        {
            var conn = new FlowConnection();
            bool fired = false;
            conn.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowConnection.StartItem)) fired = true; };
            conn.StartItem = new FlowItem();
            Assert.True(fired);
        }
    }

}
