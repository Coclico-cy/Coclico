using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Coclico.Services
{
    public sealed record SecretFinding(string FilePath, int LineNumber, string Pattern, string LineText);

    public static class SecretScanService
    {
        private static readonly Regex[] SecretPatterns =
        {
            new Regex(@"(?i)\b(?:api[_-]?key|client[_-]?secret|access[_-]?token|secret|password)\s*[:=]\s*['""]?[A-Za-z0-9\-_=]{16,}['""]?", RegexOptions.Compiled),
            new Regex(@"(?i)\b(?:ssh-rsa|ssh-ed25519|-----BEGIN PRIVATE KEY-----|-----BEGIN RSA PRIVATE KEY-----)", RegexOptions.Compiled),
            new Regex(@"(?i)\bBearer\s+[A-Za-z0-9\-_\.]{20,}\b", RegexOptions.Compiled),
            new Regex(@"(?i)\bEAACEdEose0cBA[A-Za-z0-9]+\b", RegexOptions.Compiled)
        };

        public static IReadOnlyList<SecretFinding> ScanText(string content, string filePath = "<inline>")
        {
            if (content is null)
                throw new ArgumentNullException(nameof(content));

            var findings = new List<SecretFinding>();
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                foreach (var regex in SecretPatterns)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        findings.Add(new SecretFinding(filePath, i + 1, regex.ToString(), line.Trim()));
                    }
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

            var content = File.ReadAllText(filePath);
            return ScanText(content, filePath);
        }

        public static IReadOnlyList<SecretFinding> ScanDirectory(string directoryPath, bool recurse = true, string[]? skipPatterns = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException(directoryPath);

            var files = Directory.EnumerateFiles(directoryPath, "*.*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(path => !IsHiddenOrSkipped(path, skipPatterns))
                .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) && !path.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = new List<SecretFinding>();
            foreach (var file in files)
            {
                try
                {
                    results.AddRange(ScanFile(file));
                }
                catch
                {
                }
            }

            return results;
        }

        private static bool IsHiddenOrSkipped(string path, string[]? skipPatterns)
        {
            if (skipPatterns == null || skipPatterns.Length == 0)
                return false;

            return skipPatterns.Any(pattern => path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }
}
