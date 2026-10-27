using System.Security.Cryptography;
using System.Text;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public sealed class SecurityJournal
{
    private const string HeaderLine = "CCLJ1";

    private readonly byte[] _key;
    private readonly string _journalPath;

    public SecurityJournal(string journalPath, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("journal key must be 32 bytes");
        }

        _journalPath = journalPath;
        _key = (byte[])key.Clone();
        EnsureHeader();
    }

    public string JournalPath => _journalPath;

    public void Append(DateTime utcTimestamp, string appVersion, string eventName, string component, string result, string code, string detail)
    {
        string sanitized = BuildLine(utcTimestamp, appVersion, eventName, component, result, code, detail);
        byte[] lineBytes = Encoding.UTF8.GetBytes(sanitized);
        if (lineBytes.Length > SecurityLimits.MaxJournalEntryBytes)
        {
            throw new InvalidOperationException("journal entry exceeds the maximum size");
        }

        lock (_key)
        {
            RotateIfNeeded();
            string? previousMac = ReadLastMac();
            string mac = ComputeMac(previousMac, sanitized);
            string entry = sanitized + "|" + mac;
            using var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(entry);
        }
    }

    public JournalVerification Verify()
    {
        if (!File.Exists(_journalPath))
        {
            return new JournalVerification { IsIntact = false, FirstBrokenLine = 0, Reason = "journal file is missing" };
        }

        string[] lines = File.ReadAllLines(_journalPath);
        if (lines.Length == 0 || !lines[0].StartsWith(HeaderLine + "|", StringComparison.Ordinal))
        {
            return new JournalVerification { IsIntact = false, FirstBrokenLine = 0, Reason = "journal header is missing" };
        }

        string previousMac = ExtractMac(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            int lastPipe = lines[i].LastIndexOf('|');
            if (lastPipe <= 0)
            {
                return new JournalVerification { IsIntact = false, FirstBrokenLine = i + 1, Reason = "journal line has no mac" };
            }

            string body = lines[i][..lastPipe];
            string mac = lines[i][(lastPipe + 1)..];
            string expected = ComputeMac(previousMac, body);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(mac),
                    Convert.FromHexString(expected)))
            {
                return new JournalVerification { IsIntact = false, FirstBrokenLine = i + 1, Reason = "journal chain is broken" };
            }

            previousMac = mac;
        }

        return new JournalVerification { IsIntact = true, FirstBrokenLine = 0, Reason = string.Empty };
    }

    public JournalExportResult ExportSigned(string exportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPath);

        JournalVerification verification = Verify();
        if (!File.Exists(_journalPath))
        {
            return new JournalExportResult { IsExported = false, IsIntact = false, ExportPath = exportPath, SignaturePath = string.Empty, Reason = "journal file is missing" };
        }

        byte[] content = File.ReadAllBytes(_journalPath);
        string mac = ComputeMac(null, Convert.ToHexString(content).ToLowerInvariant());

        string? directory = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string signaturePath = exportPath + ".sig";
        File.WriteAllBytes(exportPath, content);
        string signature = string.Join("|", "CCLS1", verification.IsIntact ? "1" : "0", verification.FirstBrokenLine.ToString(), mac);
        File.WriteAllText(signaturePath, signature + Environment.NewLine, new UTF8Encoding(false));

        return new JournalExportResult
        {
            IsExported = true,
            IsIntact = verification.IsIntact,
            ExportPath = exportPath,
            SignaturePath = signaturePath,
            Reason = verification.Reason
        };
    }

    public static bool VerifyExport(string exportPath, byte[] key, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPath);
        ArgumentNullException.ThrowIfNull(key);
        reason = string.Empty;

        if (!File.Exists(exportPath) || !File.Exists(exportPath + ".sig"))
        {
            reason = "export or signature file is missing";
            return false;
        }

        string[] signatureLines = File.ReadAllLines(exportPath + ".sig");
        if (signatureLines.Length == 0)
        {
            reason = "signature file is empty";
            return false;
        }

        string[] parts = signatureLines[0].Split('|');
        if (parts.Length != 4 || !parts[0].Equals("CCLS1", StringComparison.Ordinal))
        {
            reason = "signature format is not supported";
            return false;
        }

        byte[] content = File.ReadAllBytes(exportPath);
        var journal = new SecurityJournal(exportPath, key);
        string expected = journal.ComputeMac(null, Convert.ToHexString(content).ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[3]), Convert.FromHexString(expected)))
        {
            reason = "export signature mismatch: the exported journal has been modified since the export";
            return false;
        }

        if (parts[1] == "0")
        {
            reason = "export signature is valid but the journal chain was already broken when it was exported";
            return false;
        }

        return true;
    }

    public static string BuildLine(DateTime utcTimestamp, string appVersion, string eventName, string component, string result, string code, string detail)
    {
        DateTime utc = DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc);
        string timestamp = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return string.Join("|",
            timestamp,
            Sanitize(appVersion),
            Sanitize(eventName),
            Sanitize(component),
            Sanitize(result),
            Sanitize(code),
            Sanitize(detail));
    }

    private void EnsureHeader()
    {
        if (File.Exists(_journalPath))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_journalPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string mac = ComputeMac(null, HeaderLine);
        File.WriteAllText(_journalPath, HeaderLine + "|" + mac + Environment.NewLine, new UTF8Encoding(false));
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_journalPath);
        if (info.Length < SecurityLimits.MaxJournalFileBytes)
        {
            return;
        }

        string rotated = _journalPath + ".old";
        if (File.Exists(rotated))
        {
            File.Delete(rotated);
        }

        File.Move(_journalPath, rotated);
        EnsureHeader();
    }

    private string? ReadLastMac()
    {
        string[] lines = File.ReadAllLines(_journalPath);
        if (lines.Length == 0)
        {
            return null;
        }

        string last = lines[^1];
        int lastPipe = last.LastIndexOf('|');
        return lastPipe > 0 ? last[(lastPipe + 1)..] : null;
    }

    private string ComputeMac(string? previousMac, string body)
    {
        byte[] previous = string.IsNullOrEmpty(previousMac)
            ? Convert.FromHexString("00")
            : Convert.FromHexString(previousMac);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] input = new byte[previous.Length + bodyBytes.Length];
        Array.Copy(previous, 0, input, 0, previous.Length);
        Array.Copy(bodyBytes, 0, input, previous.Length, bodyBytes.Length);
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant();
    }

    private static string ExtractMac(string line)
    {
        int lastPipe = line.LastIndexOf('|');
        return lastPipe > 0 ? line[(lastPipe + 1)..] : string.Empty;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        string sanitized = value
            .Replace("|", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");

        if (sanitized.Length > 512)
        {
            sanitized = sanitized[..512];
        }

        return sanitized;
    }
}

public sealed class JournalVerification
{
    public bool IsIntact { get; init; }

    public int FirstBrokenLine { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed class JournalExportResult
{
    public bool IsExported { get; init; }

    public bool IsIntact { get; init; }

    public string ExportPath { get; init; } = string.Empty;

    public string SignaturePath { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}
