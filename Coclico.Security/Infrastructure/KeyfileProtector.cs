using System.Security.Cryptography;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class KeyfileProtector
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public static (RSA PrivateKey, string KeyId) GenerateKey(string keyfilePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var rsa = RSA.Create(4096);
        string publicBase64 = ExportPublicKeyBase64(rsa);
        string keyId = ComputeKeyId(publicBase64);

        byte[] privateBytes = rsa.ExportPkcs8PrivateKey();
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(password, salt);
        byte[] sealedBytes = AesGcmEncrypt(key, nonce, privateBytes);

        using var stream = new FileStream(keyfilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(SecurityLimits.KeyFileMagic));
        stream.Write([(byte)3]);
        stream.Write(BitConverter.GetBytes(SecurityLimits.KeyFileIterations));
        stream.Write(salt);
        stream.Write(nonce);
        stream.Write(sealedBytes);

        return (RSA.Create(rsa.ExportParameters(true)), keyId);
    }

    public static RSA LoadPrivateKey(string keyfilePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] file = File.ReadAllBytes(keyfilePath);
        if (file.Length < 6 + 1 + 4 + SaltSize + NonceSize + TagSize ||
            !System.Text.Encoding.UTF8.GetString(file, 0, 6).Equals(SecurityLimits.KeyFileMagic, StringComparison.Ordinal) ||
            file[6] != 3)
        {
            throw new CryptographicException("key file format is not recognized");
        }

        int iterations = BitConverter.ToInt32(file, 7);
        if (iterations < 100000 || iterations > 10_000_000)
        {
            throw new CryptographicException("key file iteration count is out of range");
        }

        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        Array.Copy(file, 11, salt, 0, SaltSize);
        Array.Copy(file, 11 + SaltSize, nonce, 0, NonceSize);
        int cipherOffset = 11 + SaltSize + NonceSize;
        int cipherLength = file.Length - cipherOffset;
        if (cipherLength <= TagSize)
        {
            throw new CryptographicException("key file payload is truncated");
        }

        byte[] key = DeriveKey(password, salt);
        byte[] privateBytes = AesGcmDecrypt(key, nonce, file.AsSpan(cipherOffset, cipherLength).ToArray());
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateBytes, out _);
        return rsa;
    }

    public static string ExportPublicKeyBase64(RSA rsa)
    {
        return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }

    public static string ComputeKeyId(string publicKeyBase64)
    {
        byte[] hash = SHA256.HashData(Convert.FromBase64String(publicKeyBase64));
        return "coclico-release-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        byte[] pseudorandomKey = HKdfExtract(salt, passwordBytes);
        return HkdfExpand(pseudorandomKey, System.Text.Encoding.UTF8.GetBytes("coclico-signing-v3-aes-gcm"), KeySize);
    }

    private static byte[] HKdfExtract(byte[] salt, byte[] inputKeyMaterial)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(inputKeyMaterial);
    }

    private static byte[] HkdfExpand(byte[] pseudorandomKey, byte[] info, int outputLength)
    {
        byte[] result = new byte[outputLength];
        byte[] block = [];
        int offset = 0;
        byte counter = 1;
        while (offset < outputLength)
        {
            using var hmac = new HMACSHA256(pseudorandomKey);
            byte[] input = new byte[block.Length + info.Length + 1];
            Array.Copy(block, 0, input, 0, block.Length);
            Array.Copy(info, 0, input, block.Length, info.Length);
            input[^1] = counter;
            block = hmac.ComputeHash(input);
            int copy = Math.Min(block.Length, outputLength - offset);
            Array.Copy(block, 0, result, offset, copy);
            offset += copy;
            counter++;
        }

        return result;
    }

    private static byte[] AesGcmEncrypt(byte[] key, byte[] nonce, byte[] plaintext)
    {
        byte[] ciphertext = new byte[plaintext.Length + TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length));
        return ciphertext;
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] sealedBytes)
    {
        int cipherLength = sealedBytes.Length - TagSize;
        byte[] plaintext = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, sealedBytes.AsSpan(0, cipherLength), sealedBytes.AsSpan(cipherLength), plaintext);
        return plaintext;
    }
}
