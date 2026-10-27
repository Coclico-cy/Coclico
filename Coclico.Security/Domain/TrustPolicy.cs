namespace Coclico.Security.Domain;

public sealed class TrustedKey
{
    public string KeyId { get; set; } = string.Empty;

    public string PublicKeyBase64 { get; set; } = string.Empty;

    public DateTime ActiveFromUtc { get; set; } = DateTime.UnixEpoch;

    public DateTime? RevokedUtc { get; set; }

    public bool IsActiveAt(DateTime atUtc)
    {
        return string.IsNullOrWhiteSpace(KeyId) is false &&
            string.IsNullOrWhiteSpace(PublicKeyBase64) is false &&
            atUtc >= ActiveFromUtc &&
            RevokedUtc is null;
    }
}

public sealed class TrustPolicy
{
    public List<TrustedKey> Keys { get; set; } = [];

    public TrustedKey? FindActiveKey(string keyId, DateTime atUtc)
    {
        return Keys.FirstOrDefault(k =>
            string.Equals(k.KeyId, keyId, StringComparison.Ordinal) && k.IsActiveAt(atUtc));
    }
}
