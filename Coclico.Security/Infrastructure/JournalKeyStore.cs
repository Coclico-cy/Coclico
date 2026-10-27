using System.Security.Cryptography;

namespace Coclico.Security.Infrastructure;

public sealed class DpapiJournalKeyStore
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("Coclico.SecurityJournal.Key.v1");

    public byte[]? LoadOrCreateKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (File.Exists(keyPath))
            {
                byte[] sealedKey = File.ReadAllBytes(keyPath);
                try
                {
                    return ProtectedData.Unprotect(sealedKey, Entropy, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    byte[] legacyKey = ProtectedData.Unprotect(sealedKey, null, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(keyPath, ProtectedData.Protect(legacyKey, Entropy, DataProtectionScope.CurrentUser));
                    return legacyKey;
                }
            }

            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
            string? directory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(keyPath, protectedKey);
            return key;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
