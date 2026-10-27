using System.Security.Cryptography;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;

namespace Coclico.SigningTool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: coclico-signing keygen --out <keyfile> --pubout <pubfile> [--password-env VAR]");
            Console.WriteLine("       (keygen password: exactly 128 characters)");
            Console.WriteLine("       coclico-signing sign --dir <payloadDir> --keyfile <keyfile> --version <x.y.z> --generation <n> --out <integrity.json> [--exclude <path>]... [--password-env VAR]");
            Console.WriteLine("       coclico-signing verify --dir <payloadDir> --manifest <integrity.json> --trust <trust-policy.json>");
            Console.WriteLine("       coclico-signing check --keyfile <keyfile> [--password-env VAR]");
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "keygen" => RunKeygen(args),
                "sign" => RunSign(args),
                "verify" => RunVerify(args),
                "check" => RunCheck(args),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'");
        return 1;
    }

    private static int RunKeygen(string[] args)
    {
        string keyfilePath = RequireOption(args, "--out");
        string publicKeyPath = RequireOption(args, "--pubout");
        string password = ResolvePassword(args);

        if (password.Length < SecurityLimits.MinSigningPasswordLength || password.Length > SecurityLimits.MaxSigningPasswordLength)
        {
            Console.Error.WriteLine($"password must be exactly {SecurityLimits.MinSigningPasswordLength} characters");
            return 2;
        }

        (RSA key, string keyId) = KeyfileProtector.GenerateKey(keyfilePath, password);
        string publicBase64 = KeyfileProtector.ExportPublicKeyBase64(key);
        File.WriteAllText(publicKeyPath, publicBase64 + Environment.NewLine);
        Console.WriteLine($"keyId: {keyId}");
        Console.WriteLine($"public key written to {publicKeyPath}");
        Console.WriteLine($"encrypted key file written to {keyfilePath}");
        return 0;
    }

    private static int RunSign(string[] args)
    {
        string payloadDir = RequireOption(args, "--dir");
        string keyfilePath = RequireOption(args, "--keyfile");
        string version = RequireOption(args, "--version");
        int generation = int.Parse(RequireOption(args, "--generation"));
        string outputPath = RequireOption(args, "--out");
        string password = ResolvePassword(args);
        List<string> exclusions = CollectOptions(args, "--exclude");

        if (!Directory.Exists(payloadDir))
        {
            Console.Error.WriteLine($"payload directory not found: {payloadDir}");
            return 2;
        }

        using RSA key = KeyfileProtector.LoadPrivateKey(keyfilePath, password);

        IntegrityManifest manifest = new()
        {
            FormatVersion = SecurityLimits.SupportedFormatVersion,
            Product = "Coclico",
            ReleaseVersion = version,
            SecurityGeneration = generation,
            KeyId = "derived-from-public-key",
            SignedUtc = DateTime.UtcNow
        };

        List<ManifestFileEntry> entries = [];
        foreach (string file in Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(payloadDir, file).Replace('\\', '/');
            if (relative.Equals("integrity.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!PathValidator.IsSafeRelativePath(relative))
            {
                Console.Error.WriteLine($"rejected path in payload: {relative}");
                return 2;
            }

            if (exclusions.Exists(exclusion => IsExcluded(relative, exclusion)))
            {
                continue;
            }

            entries.Add(new ManifestFileEntry
            {
                Path = relative,
                Bytes = new FileInfo(file).Length,
                Sha256 = FileIntegrityScanner.Sha256File(file)
            });
        }

        manifest.Files = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        manifest.KeyId = KeyfileProtector.ComputeKeyId(KeyfileProtector.ExportPublicKeyBase64(key));

        SignedIntegrityManifest signed = ManifestSigner.CreateSigned(manifest, key, manifest.KeyId);
        _ = ManifestLoader.Save(signed, outputPath);

        Console.WriteLine($"signed {entries.Count} files for Coclico {version} generation {generation}");
        Console.WriteLine($"keyId: {manifest.KeyId}");
        Console.WriteLine($"manifest written to {outputPath}");
        return 0;
    }

    private static int RunVerify(string[] args)
    {
        string payloadDir = RequireOption(args, "--dir");
        string manifestPath = RequireOption(args, "--manifest");
        string trustPath = RequireOption(args, "--trust");

        TrustPolicyDocument document = TrustStore.LoadFromFile(trustPath);
        VerificationResult result = IntegrityVerifier.VerifyDirectory(payloadDir, manifestPath, ToTrustPolicy(document));
        Console.WriteLine(result.Describe());
        return result.Status == IntegrityStatus.Valid ? 0 : 3;
    }

    private static int RunCheck(string[] args)
    {
        string keyfilePath = RequireOption(args, "--keyfile");
        string password = ResolvePassword(args);

        if (!File.Exists(keyfilePath))
        {
            Console.Error.WriteLine($"key file not found: {keyfilePath}");
            return 2;
        }

        using RSA key = KeyfileProtector.LoadPrivateKey(keyfilePath, password);
        string keyId = KeyfileProtector.ComputeKeyId(KeyfileProtector.ExportPublicKeyBase64(key));
        Console.WriteLine($"keyId: {keyId}");
        Console.WriteLine("key file is valid");
        return 0;
    }

    private static TrustPolicy ToTrustPolicy(TrustPolicyDocument document)
    {
        return new TrustPolicy { Keys = document.Keys };
    }

    private static string RequireOption(string[] args, string name)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        throw new InvalidOperationException($"missing required option {name}");
    }

    private static List<string> CollectOptions(string[] args, string name)
    {
        List<string> values = [];
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                values.Add(args[i + 1]);
            }
        }

        return values;
    }

    private static bool IsExcluded(string relative, string rawExclusion)
    {
        string exclusion = rawExclusion.Replace('\\', '/').Trim();
        if (exclusion.Length == 0)
        {
            return false;
        }

        if (exclusion.EndsWith('/'))
        {
            string dir = exclusion.TrimEnd('/');
            return relative.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase);
        }

        if (exclusion.StartsWith("*."))
        {
            return relative.EndsWith(exclusion[2..], StringComparison.OrdinalIgnoreCase);
        }

        return relative.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
            (!exclusion.Contains('/') && relative.EndsWith("/" + exclusion, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePassword(string[] args)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--password-env", StringComparison.Ordinal))
            {
                string? value = Environment.GetEnvironmentVariable(args[i + 1]);
                if (string.IsNullOrEmpty(value))
                {
                    throw new InvalidOperationException($"environment variable {args[i + 1]} is empty");
                }

                return value;
            }
        }

        Console.Write("signing password: ");
        string? password = Console.ReadLine();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("password is required");
        }

        return password;
    }
}
