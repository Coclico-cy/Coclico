#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Coclico.Services;

public sealed record SecretFinding(string FilePath, int LineNumber, string Pattern, string LineText);

public static class SecretScanService
{
    private static readonly Regex[] SecretPatterns =
    {
        new(@"(?i)\b(?:api[_-]?key|client[_-]?secret|access[_-]?token|secret|password)\s*[:=]\s*['""]?[A-Za-z0-9\-_=]{16,}['""]?", RegexOptions.Compiled),
        new(@"(?i)\b(?:ssh-rsa|ssh-ed25519|-----BEGIN PRIVATE KEY-----|-----BEGIN RSA PRIVATE KEY-----)", RegexOptions.Compiled),
        new(@"(?i)\bBearer\s+[A-Za-z0-9\-_\.]{20,}\b", RegexOptions.Compiled),
        new(@"(?i)\bEAACEdEose0cBA[A-Za-z0-9]+\b", RegexOptions.Compiled)
    };

    public static IReadOnlyList<SecretFinding> ScanText(string content, string filePath = "<inline>")
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var findings = new List<SecretFinding>();

        using var reader = new StringReader(content);
        int lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            foreach (var regex in SecretPatterns)
            {
                if (regex.IsMatch(line))
                    findings.Add(new SecretFinding(filePath, lineNumber, regex.ToString(), line.Trim()));
            }
        }

        return findings;
    }

    public static IReadOnlyList<SecretFinding> ScanFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var findings = new List<SecretFinding>();
        int lineNumber = 0;
        foreach (var line in File.ReadLines(filePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            foreach (var regex in SecretPatterns)
            {
                if (regex.IsMatch(line))
                    findings.Add(new SecretFinding(filePath, lineNumber, regex.ToString(), line.Trim()));
            }
        }
        return findings;
    }

    public static IReadOnlyList<SecretFinding> ScanDirectory(string directoryPath, bool recurse = true, string[]? skipPatterns = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentNullException(nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException(directoryPath);

        var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<SecretFinding>();

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", searchOption))
        {
            if (!ShouldScanFile(file, skipPatterns)) continue;
            try { results.AddRange(ScanFile(file)); }
            catch { }
        }

        return results;
    }

    private static bool ShouldScanFile(string path, string[]? skipPatterns)
    {
        if (path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase))
            return false;

        if (skipPatterns == null || skipPatterns.Length == 0)
            return true;

        foreach (var pattern in skipPatterns)
        {
            if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
