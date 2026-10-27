using System.Text.Json;
using System.Text.Json.Serialization;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = false, ReadCommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8, WriteIndented = true)]
[JsonSerializable(typeof(SignedIntegrityManifest))]
[JsonSerializable(typeof(TrustPolicyDocument))]
public sealed partial class SecurityJsonContext : JsonSerializerContext
{
}
