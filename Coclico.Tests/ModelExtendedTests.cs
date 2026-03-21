using System;
using System.Collections.ObjectModel;
using Coclico.Models;
using Xunit;

namespace Coclico.Tests
{
    public class FlowItemPropertyTests
    {
        [Fact]
        public void DefaultFlowItem_HasExpectedDefaults()
        {
            var item = new FlowItem();
            Assert.Equal(NodeType.OpenApp, item.NodeType);
            Assert.True(item.IsEnabled);
            Assert.Equal(0, item.RetryCount);
            Assert.Equal(0, item.TimeoutSeconds);
            Assert.Equal(1, item.LoopCount);
            Assert.Equal(50, item.VolumeLevel);
        }

        [Fact]
        public void StartNode_CannotDeleteOrEdit()
        {
            var item = new FlowItem { NodeType = NodeType.Start };
            Assert.False(item.CanDelete);
            Assert.False(item.CanEdit);
        }

        [Fact]
        public void EndNode_CannotDeleteOrEdit()
        {
            var item = new FlowItem { NodeType = NodeType.End };
            Assert.False(item.CanDelete);
            Assert.False(item.CanEdit);
        }

        [Fact]
        public void OpenAppNode_CanDeleteAndEdit()
        {
            var item = new FlowItem { NodeType = NodeType.OpenApp };
            Assert.True(item.CanDelete);
            Assert.True(item.CanEdit);
        }

        [Fact]
        public void NodeTypeLabel_IsNonEmpty_ForAllTypes()
        {
            foreach (NodeType type in Enum.GetValues<NodeType>())
            {
                var item = new FlowItem { NodeType = type };
                Assert.False(string.IsNullOrWhiteSpace(item.NodeTypeLabel),
                    $"NodeTypeLabel should not be empty for {type}");
            }
        }

        [Fact]
        public void NodeTypeBrush_IsValidHex_ForAllTypes()
        {
            foreach (NodeType type in Enum.GetValues<NodeType>())
            {
                var item = new FlowItem { NodeType = type };
                Assert.False(string.IsNullOrWhiteSpace(item.NodeTypeBrush),
                    $"NodeTypeBrush should not be empty for {type}");
                Assert.StartsWith("#", item.NodeTypeBrush);
            }
        }

        [Fact]
        public void NodeTypeIcon_IsNonEmpty_ForAllTypes()
        {
            foreach (NodeType type in Enum.GetValues<NodeType>())
            {
                var item = new FlowItem { NodeType = type };
                Assert.False(string.IsNullOrWhiteSpace(item.NodeTypeIcon),
                    $"NodeTypeIcon should not be empty for {type}");
            }
        }

        [Fact]
        public void SubTitle_DoesNotThrow_ForAnyNodeType()
        {
            foreach (NodeType type in Enum.GetValues<NodeType>())
            {
                var item = new FlowItem
                {
                    NodeType = type,
                    Name = "Test",
                    CommandLine = "cmd",
                    ProcessName = "notepad",
                    NotificationMessage = "Hello",
                    HttpUrl = "https://example.com",
                    FileOperationSource = "C:\\source",
                    PowerShellScript = "Get-Process",
                    UrlToOpen = "https://example.com",
                    ClipboardText = "clipboard",
                    SendKeysText = "text",
                    CompressSource = "C:\\folder",
                    TriggerShortcutKeys = "Ctrl+F1"
                };
                var ex = Record.Exception(() => _ = item.SubTitle);
                Assert.Null(ex);
            }
        }

        [Fact]
        public void DelaySeconds_ClampedAtZero()
        {
            var item = new FlowItem { DelaySeconds = -5 };
            Assert.Equal(0, item.DelaySeconds);
        }

        [Fact]
        public void LoopCount_ClampedBetween1And1000()
        {
            var item = new FlowItem();
            item.LoopCount = 0;
            Assert.Equal(1, item.LoopCount);

            item.LoopCount = 9999;
            Assert.Equal(1000, item.LoopCount);

            item.LoopCount = 500;
            Assert.Equal(500, item.LoopCount);
        }

        [Fact]
        public void VolumeLevel_ClampedBetween0And100()
        {
            var item = new FlowItem();
            item.VolumeLevel = -10;
            Assert.Equal(0, item.VolumeLevel);

            item.VolumeLevel = 150;
            Assert.Equal(100, item.VolumeLevel);

            item.VolumeLevel = 75;
            Assert.Equal(75, item.VolumeLevel);
        }

        [Fact]
        public void CenterX_ComputedCorrectly()
        {
            var item = new FlowItem { X = 100, Width = 200 };
            Assert.Equal(200, item.CenterX);
        }

        [Fact]
        public void PropertyChanged_FiresOnNodeTypeSet()
        {
            var item = new FlowItem();
            var changed = new System.Collections.Generic.List<string>();
            item.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

            item.NodeType = NodeType.Delay;

            Assert.Contains(nameof(FlowItem.NodeType), changed);
            Assert.Contains(nameof(FlowItem.NodeTypeLabel), changed);
            Assert.Contains(nameof(FlowItem.NodeTypeBrush), changed);
            Assert.Contains(nameof(FlowItem.NodeTypeIcon), changed);
        }
    }

    public class FlowChainPropertyTests
    {
        [Fact]
        public void DefaultFlowChain_HasId()
        {
            var chain = new FlowChain();
            Assert.False(string.IsNullOrWhiteSpace(chain.Id));
        }

        [Fact]
        public void DefaultFlowChain_HasEmptyCollections()
        {
            var chain = new FlowChain();
            Assert.NotNull(chain.Items);
            Assert.NotNull(chain.Connections);
        }

        [Fact]
        public void AutoTriggerIntervalSec_MinimumIs10()
        {
            var chain = new FlowChain();
            chain.AutoTriggerIntervalSec = 1;
            Assert.Equal(10, chain.AutoTriggerIntervalSec);

            chain.AutoTriggerIntervalSec = 60;
            Assert.Equal(60, chain.AutoTriggerIntervalSec);
        }

        [Fact]
        public void TriggerSummary_WhenNoTrigger_ReturnsEmptyOrDefault()
        {
            var chain = new FlowChain { AutoTriggerEnabled = false, TriggerHotkey = null };
            var summary = chain.TriggerSummary;
            Assert.NotNull(summary);
        }

        [Fact]
        public void TriggerSummary_WithHotkey_IncludesHotkey()
        {
            var chain = new FlowChain { TriggerHotkey = "Ctrl+F5" };
            Assert.Contains("Ctrl+F5", chain.TriggerSummary);
        }

        [Fact]
        public void TriggerSummary_WithAutoTrigger_IncludesInterval()
        {
            var chain = new FlowChain { AutoTriggerEnabled = true, AutoTriggerIntervalSec = 30 };
            Assert.Contains("30", chain.TriggerSummary);
        }

        [Fact]
        public void PropertyChanged_FiresOnIsRunningSet()
        {
            var chain = new FlowChain();
            var changed = false;
            chain.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlowChain.IsRunning)) changed = true; };
            chain.IsRunning = true;
            Assert.True(changed);
        }
    }

    public class FlowConnectionTests
    {
        [Fact]
        public void FlowConnection_DefaultProperties()
        {
            var conn = new FlowConnection();
            Assert.Null(conn.StartItem);
            Assert.Null(conn.EndItem);
            Assert.Null(conn.Label);
        }

        [Fact]
        public void FlowConnection_SetProperties()
        {
            var start = new FlowItem { Name = "Start" };
            var end = new FlowItem { Name = "End" };
            var conn = new FlowConnection { StartItem = start, EndItem = end, Label = "Yes" };

            Assert.Equal(start, conn.StartItem);
            Assert.Equal(end, conn.EndItem);
            Assert.Equal("Yes", conn.Label);
        }

        [Fact]
        public void FlowConnection_PropertyChanged_Fires()
        {
            var conn = new FlowConnection();
            var changed = false;
            conn.PropertyChanged += (_, _) => changed = true;
            conn.Label = "Changed";
            Assert.True(changed);
        }
    }

    public class NodeTypeEnumTests
    {
        [Fact]
        public void NodeType_Has27OrMoreValues()
        {
            var values = Enum.GetValues<NodeType>();
            Assert.True(values.Length >= 27, $"Expected at least 27 NodeType values, got {values.Length}");
        }

        [Fact]
        public void ConditionOperator_HasExpectedValues()
        {
            var values = Enum.GetValues<ConditionOperator>();
            Assert.Contains(ConditionOperator.ProcessRunning, values);
            Assert.Contains(ConditionOperator.ProcessNotRunning, values);
            Assert.Contains(ConditionOperator.FileExists, values);
            Assert.Contains(ConditionOperator.FileNotExists, values);
            Assert.Contains(ConditionOperator.CpuBelow, values);
            Assert.Contains(ConditionOperator.CpuAbove, values);
            Assert.Contains(ConditionOperator.RamBelow, values);
            Assert.Contains(ConditionOperator.RamAbove, values);
        }
    }
}
