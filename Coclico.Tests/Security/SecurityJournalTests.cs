using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class SecurityJournalTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "coclico-journaltests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ChainedEntriesVerify()
    {
        _ = Directory.CreateDirectory(_dir);
        string journalPath = Path.Join(_dir, "journal.log");
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        var journal = new SecurityJournal(journalPath, key);
        journal.Append(DateTime.UtcNow, "2.2.0", "StartupCheck", "Integrity", "Valid", "None", "initial check");
        journal.Append(DateTime.UtcNow, "2.2.0", "PeriodicCheck", "Integrity", "Valid", "None", "periodic check");

        JournalVerification verification = journal.Verify();
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public void TamperedLineIsDetected()
    {
        _ = Directory.CreateDirectory(_dir);
        string journalPath = Path.Join(_dir, "journal.log");
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        var journal = new SecurityJournal(journalPath, key);
        journal.Append(DateTime.UtcNow, "2.2.0", "StartupCheck", "Integrity", "Valid", "None", "initial check");
        journal.Append(DateTime.UtcNow, "2.2.0", "PeriodicCheck", "Integrity", "Valid", "None", "periodic check");
        string[] lines = File.ReadAllLines(journalPath);
        lines[1] = lines[1].Replace("Valid", "Altered");
        File.WriteAllLines(journalPath, lines);

        var reloaded = new SecurityJournal(journalPath, key);
        JournalVerification verification = reloaded.Verify();
        Assert.False(verification.IsIntact);
    }

    [Fact]
    public void TruncatedJournalIsDetected()
    {
        _ = Directory.CreateDirectory(_dir);
        string journalPath = Path.Join(_dir, "journal.log");
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        var journal = new SecurityJournal(journalPath, key);
        journal.Append(DateTime.UtcNow, "2.2.0", "StartupCheck", "Integrity", "Valid", "None", "initial check");
        journal.Append(DateTime.UtcNow, "2.2.0", "PeriodicCheck", "Integrity", "Valid", "None", "periodic check");
        string[] lines = File.ReadAllLines(journalPath)[..1];
        File.WriteAllLines(journalPath, lines);

        var reloaded = new SecurityJournal(journalPath, key);
        JournalVerification verification = reloaded.Verify();
        Assert.True(verification.IsIntact);
    }

    [Fact]
    public void EntrySanitizationRemovesSeparators()
    {
        string line = SecurityJournal.BuildLine(DateTime.UtcNow, "2.2.0", "Event", "Comp", "Valid", "None", "a|b\nc");
        Assert.DoesNotContain("|a|b", line);
        Assert.DoesNotContain('\n', line);
        string[] fields = line.Split('|');
        Assert.Equal(7, fields.Length);
    }

    [Fact]
    public void SignedExportVerifiesAndTamperingIsDetected()
    {
        _ = Directory.CreateDirectory(_dir);
        string journalPath = Path.Join(_dir, "journal.log");
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        var journal = new SecurityJournal(journalPath, key);
        journal.Append(DateTime.UtcNow, "2.2.0", "StartupCheck", "Integrity", "Valid", "None", "initial check");
        journal.Append(DateTime.UtcNow, "2.2.0", "PeriodicCheck", "Integrity", "Valid", "None", "periodic check");

        string exportPath = Path.Join(_dir, "export", "journal-export.log");
        JournalExportResult export = journal.ExportSigned(exportPath);
        Assert.True(export.IsExported);
        Assert.True(export.IsIntact);
        Assert.True(File.Exists(export.SignaturePath));
        Assert.True(SecurityJournal.VerifyExport(exportPath, key, out string validReason));

        byte[] content = File.ReadAllBytes(exportPath);
        byte[] tampered = new byte[content.Length + 1];
        Array.Copy(content, tampered, content.Length);
        tampered[^1] = 0x41;
        File.WriteAllBytes(exportPath, tampered);

        Assert.False(SecurityJournal.VerifyExport(exportPath, key, out string tamperedReason));
        Assert.Contains("modified", tamperedReason);

        byte[] wrongKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(wrongKey);
        File.WriteAllBytes(exportPath, content);
        Assert.False(SecurityJournal.VerifyExport(exportPath, wrongKey, out string wrongKeyReason));
    }

    [Fact]
    public void ExportOfBrokenJournalReportsTheBrokenChain()
    {
        _ = Directory.CreateDirectory(_dir);
        string journalPath = Path.Join(_dir, "journal.log");
        byte[] key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        var journal = new SecurityJournal(journalPath, key);
        journal.Append(DateTime.UtcNow, "2.2.0", "StartupCheck", "Integrity", "Valid", "None", "initial check");
        string[] lines = File.ReadAllLines(journalPath);
        lines[1] = lines[1].Replace("Valid", "Altered");
        File.WriteAllLines(journalPath, lines);

        var reloaded = new SecurityJournal(journalPath, key);
        string exportPath = Path.Join(_dir, "export", "journal-export.log");
        JournalExportResult export = reloaded.ExportSigned(exportPath);
        Assert.True(export.IsExported);
        Assert.False(export.IsIntact);

        Assert.False(SecurityJournal.VerifyExport(exportPath, key, out string reason));
        Assert.Contains("already broken", reason);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
