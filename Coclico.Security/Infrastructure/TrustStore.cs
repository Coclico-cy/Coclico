using System.Text.Json;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public sealed class TrustPolicyDocument
{
    public List<TrustedKey> Keys { get; set; } = [];

    public UpdatePolicy UpdatePolicy { get; set; } = new();
}

public static class TrustStore
{
    public static TrustPolicyDocument Load(Stream stream)
    {
        TrustPolicyDocument? document = JsonSerializer.Deserialize(stream, SecurityJsonContext.Default.TrustPolicyDocument);
        if (document == null)
        {
            throw new InvalidOperationException("trust policy document is empty");
        }

        return document;
    }

    public static TrustPolicyDocument LoadFromFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static TrustPolicyDocument Empty()
    {
        return new TrustPolicyDocument();
    }
}
