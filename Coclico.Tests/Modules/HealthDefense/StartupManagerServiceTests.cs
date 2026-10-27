using Coclico.Services;
using Microsoft.Win32;
using Xunit;

namespace Coclico.Tests.Modules.HealthDefense;

public sealed class StartupManagerServiceTests
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string TestEntryName = "CoclicoStartupManagerServiceTests";

    [Fact]
    public void ListStartupEntries_IncludesHkcuRunEntry()
    {
        try
        {
            CreateTestRunEntry();

            List<StartupEntry> entries = StartupManagerService.ListStartupEntries();

            StartupEntry? entry = entries.FirstOrDefault(e => e.Name == TestEntryName);
            Assert.NotNull(entry);
            Assert.Equal("HKCU", entry!.Source);
            Assert.True(entry.CanToggle);
            Assert.True(entry.IsEnabled);
        }
        finally
        {
            CleanupTestEntry();
        }
    }

    [Fact]
    public void SetEntryEnabled_DisablesAndReEnablesEntry()
    {
        try
        {
            CreateTestRunEntry();

            StartupEntry? entry = StartupManagerService.ListStartupEntries()
                .FirstOrDefault(e => e.Name == TestEntryName);
            Assert.NotNull(entry);

            bool disabled = StartupManagerService.SetEntryEnabled(entry!, enabled: false);
            Assert.True(disabled);
            StartupEntry? afterDisable = StartupManagerService.ListStartupEntries()
                .FirstOrDefault(e => e.Name == TestEntryName);
            Assert.NotNull(afterDisable);
            Assert.False(afterDisable!.IsEnabled);

            bool reEnabled = StartupManagerService.SetEntryEnabled(afterDisable, enabled: true);
            Assert.True(reEnabled);
            StartupEntry? afterEnable = StartupManagerService.ListStartupEntries()
                .FirstOrDefault(e => e.Name == TestEntryName);
            Assert.NotNull(afterEnable);
            Assert.True(afterEnable!.IsEnabled);
        }
        finally
        {
            CleanupTestEntry();
        }
    }

    [Fact]
    public void SetEntryEnabled_DoesNotDeleteOriginalRunValue()
    {
        try
        {
            CreateTestRunEntry();

            StartupEntry? entry = StartupManagerService.ListStartupEntries()
                .FirstOrDefault(e => e.Name == TestEntryName);
            Assert.NotNull(entry);
            Assert.True(StartupManagerService.SetEntryEnabled(entry!, enabled: false));

            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            Assert.NotNull(runKey);
            Assert.NotNull(runKey!.GetValue(TestEntryName));
        }
        finally
        {
            CleanupTestEntry();
        }
    }

    [Fact]
    public void SetEntryEnabled_LockedEntry_ReturnsFalse()
    {
        var locked = new StartupEntry("NimporteQuelService", "cmd", "HKLM", IsEnabled: true, CanToggle: false);

        bool result = StartupManagerService.SetEntryEnabled(locked, enabled: false);

        Assert.False(result);
    }

    [Fact]
    public void SetEntryEnabled_NullEntry_ReturnsFalse()
    {
        bool result = StartupManagerService.SetEntryEnabled(null!, enabled: false);

        Assert.False(result);
    }

    private static void CreateTestRunEntry()
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(TestEntryName, "\"C:\\CoclicoTests\\fictif.exe\"");
    }

    private static void CleanupTestEntry()
    {
        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(TestEntryName, throwOnMissingValue: false);
            using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            approvedKey?.DeleteValue(TestEntryName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Security.SecurityException)
        {
            return;
        }
    }
}
