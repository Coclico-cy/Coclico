using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Coclico.Security.Infrastructure;

namespace Coclico.Installer.Services;

public sealed record PayloadVerification
{
    public bool HasManifest { get; init; }

    public bool IsVerified { get; init; }

    public bool IsSignedByTrustedKey { get; init; }

    public int CheckedFiles { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public static class PayloadGuard
{
    private const string ManifestName = "integrity.json";
    private const string TrustPolicyResourceName = "Coclico.Installer.trust-policy.json";

    public static bool IsSafeArchiveEntry(string entryName)
    {
        return ArchiveGuard.IsSafeArchiveEntry(entryName);
    }

    public static void ExtractSafely(Stream zipStream, string stagingDir, Action<int, int>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDir);
        string stagingRoot = Path.GetFullPath(stagingDir);
        string normalizedRoot = stagingRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _ = Directory.CreateDirectory(stagingRoot);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        if (archive.Entries.Count > ArchiveGuard.MaxEntries)
        {
            throw new InvalidOperationException($"archive has {archive.Entries.Count} entries, limit is {ArchiveGuard.MaxEntries}");
        }

        int total = archive.Entries.Count;
        int extracted = 0;
        long totalBytes = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length > ArchiveGuard.MaxEntryBytes)
            {
                throw new InvalidOperationException($"archive entry '{entry.FullName}' is too large");
            }

            if (totalBytes + entry.Length > ArchiveGuard.MaxTotalBytes)
            {
                throw new InvalidOperationException("archive total size exceeds the limit");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                if (entry.FullName.Contains("..", StringComparison.Ordinal) || !IsSafeArchiveEntry(entry.FullName))
                {
                    throw new InvalidOperationException($"rejected archive entry '{entry.FullName}'");
                }

                string dirDest = Path.GetFullPath(Path.Join(stagingRoot, entry.FullName));
                if (!dirDest.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"archive entry escapes the staging directory: '{dirDest}'");
                }
                _ = Directory.CreateDirectory(dirDest);
                extracted++;
                progress?.Invoke(extracted, total);
                continue;
            }

            if (entry.FullName.Contains("..", StringComparison.Ordinal) || !IsSafeArchiveEntry(entry.FullName))
            {
                throw new InvalidOperationException($"rejected archive entry '{entry.FullName}'");
            }

            string destination = Path.GetFullPath(Path.Join(stagingRoot, entry.FullName));
            if (!destination.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"archive entry escapes the staging directory: '{destination}'");
            }

            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                _ = Directory.CreateDirectory(parent);
            }

            using Stream source = entry.Open();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[128 * 1024];
            long written = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > ArchiveGuard.MaxEntryBytes)
                {
                    throw new InvalidOperationException($"archive entry '{entry.FullName}' expands beyond the limit");
                }

                target.Write(buffer, 0, read);
            }

            totalBytes += written;
            extracted++;
            progress?.Invoke(extracted, total);
        }
    }

    public static PayloadVerification VerifyStaging(string stagingDir)
    {
        string manifestPath = Path.Join(stagingDir, ManifestName);
        if (!File.Exists(manifestPath))
        {
            return new PayloadVerification
            {
                HasManifest = false,
                IsVerified = false,
                IsSignedByTrustedKey = false,
                Reason = "integrity.json absent du payload"
            };
        }

        var info = new FileInfo(manifestPath);
        if (info.Length > 2 * 1024 * 1024)
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "manifeste trop volumineux" };
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "manifeste illisible : " + ex.Message };
        }

        if (root is not JsonObject rootObject ||
            rootObject["Manifest"] is not JsonObject manifest ||
            manifest["Files"] is not JsonArray files ||
            files.Count > 20000)
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "structure du manifeste invalide" };
        }

        if (manifest["FormatVersion"]?.GetValue<int>() != 1)
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "version de format non supportée" };
        }

        if (!string.Equals(manifest["Product"]?.GetValue<string>(), "Coclico", StringComparison.Ordinal))
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "produit du manifeste invalide" };
        }

        string signedKeyId = rootObject["KeyId"]?.GetValue<string>() ?? manifest["KeyId"]?.GetValue<string>() ?? string.Empty;
        string algorithm = rootObject["Algorithm"]?.GetValue<string>() ?? string.Empty;
        string signature = rootObject["Signature"]?.GetValue<string>() ?? string.Empty;
        string? signedUtcRaw = manifest["SignedUtc"]?.GetValue<string>();

        if (!string.Equals(algorithm, "RSA-PSS-SHA256", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(signature) ||
            string.IsNullOrEmpty(signedUtcRaw))
        {
            return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "en-tête de signature invalide" };
        }

        List<TrustedKeyEntry> trustedKeys = LoadTrustedKeys();
        bool signedByTrustedKey = false;
        if (trustedKeys.Count > 0)
        {
            if (!TryVerifySignature(manifest, trustedKeys, signedKeyId, signedUtcRaw, signature, out string reason))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, IsSignedByTrustedKey = false, Reason = "signature du manifeste rejetée : " + reason };
            }

            signedByTrustedKey = true;
        }

        var expected = new Dictionary<string, (long Bytes, string Sha256)>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? fileNode in files)
        {
            if (fileNode is not JsonObject file ||
                file["Path"]?.GetValue<string>() is not { } relativePath ||
                file["Sha256"]?.GetValue<string>() is not { } sha256 ||
                file["Bytes"]?.GetValue<long>() is not { } bytes)
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = "entrée de manifeste invalide" };
            }

            if (!IsSafeArchiveEntry(relativePath))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = $"chemin de manifeste dangereux '{relativePath}'" };
            }

            if (bytes < 0 || sha256.Length != 64 || !IsHex(sha256))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = $"données d'entrée invalides pour '{relativePath}'" };
            }

            if (expected.ContainsKey(relativePath))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, Reason = $"entrée dupliquée '{relativePath}'" };
            }

            expected[relativePath] = (bytes, sha256.ToLowerInvariant());
        }

        var checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(stagingDir, file).Replace('\\', '/');
            if (relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expected.TryGetValue(relative, out (long Bytes, string Sha256) entry))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, IsSignedByTrustedKey = signedByTrustedKey, Reason = $"fichier inattendu '{relative}'" };
            }

            var fileInfo = new FileInfo(file);
            if (fileInfo.Length != entry.Bytes)
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, IsSignedByTrustedKey = signedByTrustedKey, Reason = $"taille invalide pour '{relative}'" };
            }

            if (!string.Equals(Sha256File(file), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, IsSignedByTrustedKey = signedByTrustedKey, Reason = $"empreinte invalide pour '{relative}'" };
            }

            _ = checkedFiles.Add(relative);
        }

        foreach (string expectedPath in expected.Keys)
        {
            if (!checkedFiles.Contains(expectedPath))
            {
                return new PayloadVerification { HasManifest = true, IsVerified = false, IsSignedByTrustedKey = signedByTrustedKey, Reason = $"fichier manquant '{expectedPath}'" };
            }
        }

        return new PayloadVerification
        {
            HasManifest = true,
            IsVerified = true,
            IsSignedByTrustedKey = signedByTrustedKey,
            CheckedFiles = checkedFiles.Count,
            Reason = signedByTrustedKey ? "manifeste signé et fichiers vérifiés" : "fichiers vérifiés (manifeste non authentifié, aucune clé de confiance embarquée)"
        };
    }

    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool TryHardenInstallAcl(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            string? userSid = WindowsIdentity.GetCurrent().User?.Value;
            bool elevated = false;
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Debug.WriteLine("[PayloadGuard] Rôle Windows indéterminé : " + ex.Message);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "icacls",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(directory);
            psi.ArgumentList.Add("/inheritance:r");
            psi.ArgumentList.Add("/grant:r");
            psi.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)F");
            psi.ArgumentList.Add("/grant:r");
            psi.ArgumentList.Add("*S-1-5-18:(OI)(CI)F");
            psi.ArgumentList.Add("/grant:r");
            psi.ArgumentList.Add("*S-1-1-0:(OI)(CI)RX");
            if (!elevated && !string.IsNullOrEmpty(userSid))
            {
                psi.ArgumentList.Add("/grant:r");
                psi.ArgumentList.Add("*" + userSid + ":(OI)(CI)F");
            }

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(30000))
            {
                try { process.Kill(); } catch (Exception ex) when (ex is not OutOfMemoryException) { Debug.WriteLine("[PayloadGuard] icacls non terminé : " + ex.Message); }
                return false;
            }

            bool ok = process.ExitCode == 0;
            if (!ok)
            {
                Debug.WriteLine("[PayloadGuard] icacls a échoué, code " + process.ExitCode);
            }

            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Debug.WriteLine("[PayloadGuard] Durcissement ACL impossible : " + ex.Message);
            return false;
        }
    }

    private static bool TryVerifySignature(
        JsonObject manifest,
        List<TrustedKeyEntry> trustedKeys,
        string signedKeyId,
        string signedUtcRaw,
        string signature,
        out string reason)
    {
        reason = string.Empty;
        if (!DateTime.TryParse(signedUtcRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime signedUtc))
        {
            reason = "horodatage de signature invalide";
            return false;
        }

        TrustedKeyEntry? key = trustedKeys.Find(k => string.Equals(k.KeyId, signedKeyId, StringComparison.Ordinal));
        if (key == null)
        {
            reason = $"clé '{signedKeyId}' non reconnue";
            return false;
        }

        if (signedUtc < key.ActiveFromUtc || (key.RevokedUtc is not null && signedUtc >= key.RevokedUtc))
        {
            reason = $"clé '{signedKeyId}' invalide à la date de signature";
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            reason = "signature illisible en base64";
            return false;
        }

        byte[] payload = CanonicalManifestBytes(manifest);
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyBase64), out _);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            reason = "clé publique de confiance illisible";
            return false;
        }

        if (!rsa.VerifyData(payload, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        {
            reason = "signature non conforme au manifeste signé";
            return false;
        }

        return true;
    }

    private static byte[] CanonicalManifestBytes(JsonObject manifest)
    {
        using var stream = new MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", manifest["FormatVersion"]!.GetValue<int>());
        writer.WriteString("product", manifest["Product"]?.GetValue<string>() ?? string.Empty);
        writer.WriteString("releaseVersion", manifest["ReleaseVersion"]?.GetValue<string>() ?? string.Empty);
        writer.WriteNumber("securityGeneration", manifest["SecurityGeneration"]?.GetValue<int>() ?? 0);
        writer.WriteString("keyId", manifest["KeyId"]?.GetValue<string>() ?? string.Empty);
        string signedUtc = manifest["SignedUtc"]?.GetValue<string>() ?? string.Empty;
        if (DateTime.TryParse(signedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            writer.WriteString("signedUtc", DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToString("O"));
        }
        else
        {
            writer.WriteString("signedUtc", signedUtc);
        }

        writer.WriteStartArray("files");
        var entries = new SortedDictionary<string, (long Bytes, string Sha256)>(StringComparer.Ordinal);
        if (manifest["Files"] is JsonArray files)
        {
            foreach (JsonObject file in files.OfType<JsonObject>()
                .Where(file => file["Path"]?.GetValue<string>() is not null))
            {
                string path = file["Path"]!.GetValue<string>();
                entries[path] = (file["Bytes"]?.GetValue<long>() ?? 0, (file["Sha256"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
            }
        }

        foreach (KeyValuePair<string, (long Bytes, string Sha256)> entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.Key);
            writer.WriteNumber("bytes", entry.Value.Bytes);
            writer.WriteString("sha256", entry.Value.Sha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private static List<TrustedKeyEntry> LoadTrustedKeys()
    {
        try
        {
            Assembly assembly = typeof(PayloadGuard).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(TrustPolicyResourceName);
            if (stream == null)
            {
                return [];
            }

            string policyJson;
            using (var reader = new StreamReader(stream))
            {
                policyJson = reader.ReadToEnd();
            }

            JsonNode? root = JsonNode.Parse(policyJson);
            if (root is not JsonObject rootObject || rootObject["keys"] is not JsonArray keys)
            {
                return [];
            }

            var result = new List<TrustedKeyEntry>();
            foreach (JsonObject key in keys.OfType<JsonObject>()
                .Where(key => key["keyId"]?.GetValue<string>() is not null &&
                              key["publicKeyBase64"]?.GetValue<string>() is not null))
            {
                result.Add(new TrustedKeyEntry(
                    key["keyId"]!.GetValue<string>(),
                    key["publicKeyBase64"]!.GetValue<string>(),
                    DateTime.TryParse(key["activeFromUtc"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime active) ? active : DateTime.UnixEpoch,
                    DateTime.TryParse(key["revokedUtc"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime revoked) ? revoked : null));
            }

            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine("[PayloadGuard] Politique de confiance illisible : " + ex.Message);
            return [];
        }
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record TrustedKeyEntry(string KeyId, string PublicKeyBase64, DateTime ActiveFromUtc, DateTime? RevokedUtc);
}
