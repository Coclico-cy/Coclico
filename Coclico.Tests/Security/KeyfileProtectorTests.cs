using System.Security.Cryptography;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class KeyfileProtectorTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "coclico-keytests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GeneratedKeyFileRoundTrips()
    {
        _ = Directory.CreateDirectory(_dir);
        string keyfile = Path.Join(_dir, "release.keyfile");
        string pubkey = Path.Join(_dir, "release.pub");

        (RSA created, string keyId) = KeyfileProtector.GenerateKey(keyfile, "correct horse battery staple");
        File.WriteAllText(pubkey, KeyfileProtector.ExportPublicKeyBase64(created));

        using RSA loaded = KeyfileProtector.LoadPrivateKey(keyfile, "correct horse battery staple");
        string publicKey = KeyfileProtector.ExportPublicKeyBase64(loaded);

        Assert.Equal(File.ReadAllText(pubkey).Trim(), publicKey);
        Assert.StartsWith("coclico-release-", keyId);
        byte[] message = [1, 2, 3, 4];
        Assert.True(loaded.VerifyData(message, created.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    [Fact]
    public void WrongPasswordFails()
    {
        _ = Directory.CreateDirectory(_dir);
        string keyfile = Path.Join(_dir, "release.keyfile");
        _ = KeyfileProtector.GenerateKey(keyfile, "right-password");

        Assert.ThrowsAny<CryptographicException>(() => KeyfileProtector.LoadPrivateKey(keyfile, "wrong-password"));
    }

    [Fact]
    public void KeyFileStartsWithMagicAndUsesStrongIterations()
    {
        _ = Directory.CreateDirectory(_dir);
        string keyfile = Path.Join(_dir, "release.keyfile");
        _ = KeyfileProtector.GenerateKey(keyfile, "some-password");

        byte[] raw = File.ReadAllBytes(keyfile);
        string magic = System.Text.Encoding.UTF8.GetString(raw, 0, 6);
        int iterations = BitConverter.ToInt32(raw, 7);

        Assert.Equal(SecurityLimits.KeyFileMagic, magic);
        Assert.Equal(SecurityLimits.KeyFileIterations, iterations);
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
