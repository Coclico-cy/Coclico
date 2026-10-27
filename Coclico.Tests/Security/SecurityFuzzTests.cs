using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Xunit;

namespace Coclico.Tests;

public sealed class SecurityFuzzTests : IDisposable
{
    private readonly TestWorld _world = new();

    [Fact]
    public void DangerousPathCorpusIsRejectedByBothValidators()
    {
        string[] dangerous =
        [
            "", " ", "\t", "..", "../x", "a/../b", "a/..", "a//b",
            "C:/x", "C:\\x", "\\\\srv\\share\\x", "\\\\?\\C:\\x", "/abs/x", "/x", "\\x",
            "CON", "con.txt", "PRN.dll", "COM1.bin", "aux", "nul.cfg", "lpt1.data",
            "a<b", "a>b", "a\"b", "a|b", "a?b", "a*b", "a\tb", "a\nb",
            "x.", "x ", "a./b", "a /b",
            new string('a', 256), new string('a', 500)
        ];

        foreach (string candidate in dangerous)
        {
            Assert.False(PathValidator.IsSafeRelativePath(candidate), $"PathValidator should reject '{candidate}'");
            Assert.False(ArchiveGuard.IsSafeArchiveEntry(candidate), $"ArchiveGuard should reject '{candidate}'");
        }
    }

    [Fact]
    public void SafePathCorpusIsAcceptedByBothValidators()
    {
        string[] safe =
        [
            "Coclico.exe", "libs/core.dll", "runtimes/win-x64/native/cuda12/cudart64_12.dll",
            "Logs_2026.txt", "ünïcode.txt", "file-name.bin", "UPPER.DLL", "a/b/c/d/e/f.txt",
            "1.2.3-version.json"
        ];

        foreach (string candidate in safe)
        {
            Assert.True(PathValidator.IsSafeRelativePath(candidate), $"PathValidator should accept '{candidate}'");
            Assert.True(ArchiveGuard.IsSafeArchiveEntry(candidate), $"ArchiveGuard should accept '{candidate}'");
        }
    }

    [Fact]
    public void PathFuzzNeverCrashesAndHonorsUniversalInvariants()
    {
        var rng = new Random(20261027);
        string[] fragments =
        [
            "..", ".", "//", "\\\\", "/", ":", "C:", "CON", "com1", "<", ">", "\"", "|", "?", "*",
            "\t", "\n", "\0", " ", "x", "dll", "exe", "a", "sub", "win-x64", "native", "ünïcode", "日本語", "‮"
        ];

        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            int length = rng.Next(0, 12);
            builder.Clear();
            for (int f = 0; f < length; f++)
            {
                if (rng.Next(4) == 0)
                {
                    int code = rng.Next(1, 0xD7FF);
                    _ = builder.Append(char.ConvertFromUtf32(code));
                }
                else
                {
                    _ = builder.Append(fragments[rng.Next(fragments.Length)]);
                }
            }

            string candidate = builder.ToString();
            bool validatorResult = PathValidator.IsSafeRelativePath(candidate);
            bool archiveResult = ArchiveGuard.IsSafeArchiveEntry(candidate);

            if (candidate.Length == 0 || candidate.Contains(':'))
            {
                Assert.False(validatorResult);
                Assert.False(archiveResult);
            }

            if (candidate.StartsWith('/') || candidate.StartsWith('\\'))
            {
                Assert.False(validatorResult);
                Assert.False(archiveResult);
            }

            if (candidate.IndexOf('\n') >= 0 || candidate.IndexOf('\t') >= 0 || candidate.IndexOf('\0') >= 0)
            {
                Assert.False(validatorResult);
                Assert.False(archiveResult);
            }

            if (candidate.Length > 255)
            {
                Assert.False(validatorResult);
                Assert.False(archiveResult);
            }
        }
    }

    [Fact]
    public void ArchiveLimitGuardsMatchContract()
    {
        Assert.False(ArchiveGuard.IsEntryCountAllowed(0));
        Assert.True(ArchiveGuard.IsEntryCountAllowed(1));
        Assert.True(ArchiveGuard.IsEntryCountAllowed(ArchiveGuard.MaxEntries));
        Assert.False(ArchiveGuard.IsEntryCountAllowed(ArchiveGuard.MaxEntries + 1));

        Assert.True(ArchiveGuard.IsEntrySizeAllowed(0, 0));
        Assert.True(ArchiveGuard.IsEntrySizeAllowed(ArchiveGuard.MaxEntryBytes, 0));
        Assert.False(ArchiveGuard.IsEntrySizeAllowed(ArchiveGuard.MaxEntryBytes + 1, 0));
        Assert.False(ArchiveGuard.IsEntrySizeAllowed(1, ArchiveGuard.MaxTotalBytes));
        Assert.False(ArchiveGuard.IsEntrySizeAllowed(-1, 0));
        Assert.False(ArchiveGuard.IsEntrySizeAllowed(1, -1));
    }

    [Fact]
    public void ManifestByteFuzzNeverCrashesAndAlwaysReturnsDefinedStatus()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.WriteFile("libs/core.dll", "library-content");
        _ = _world.SaveManifest(_world.BuildManifest());
        byte[] validManifest = File.ReadAllBytes(_world.ManifestPath);

        var rng = new Random(31337);
        for (int i = 0; i < 250; i++)
        {
            byte[] mutated = (byte[])validManifest.Clone();
            if (rng.Next(6) == 0)
            {
                int cut = rng.Next(0, mutated.Length);
                Array.Resize(ref mutated, cut);
            }
            else
            {
                int mutations = rng.Next(1, 9);
                for (int m = 0; m < mutations; m++)
                {
                    int index = rng.Next(mutated.Length);
                    mutated[index] = (byte)rng.Next(256);
                }
            }

            File.WriteAllBytes(_world.ManifestPath, mutated);

            VerificationResult result = _world.Verify();
            Assert.True(Enum.IsDefined(result.Status), $"iteration {i} produced an undefined status");
            Assert.True(result.CheckedFiles >= 0, $"iteration {i} produced a negative checked count");
        }
    }

    [Fact]
    public void ManifestEmptyAndOversizedInputsAreRejected()
    {
        _ = _world.WriteFile("Coclico.exe", "binary-content");
        _ = _world.SaveManifest(_world.BuildManifest());

        File.WriteAllText(_world.ManifestPath, string.Empty);
        Assert.Equal(IntegrityStatus.Unknown, _world.Verify().Status);

        File.WriteAllText(_world.ManifestPath, "{ not json at all");
        Assert.Equal(IntegrityStatus.Unknown, _world.Verify().Status);

        byte[] oversized = new byte[SecurityLimits.MaxManifestBytes + 1024];
        File.WriteAllBytes(_world.ManifestPath, oversized);
        Assert.Equal(IntegrityStatus.Unknown, _world.Verify().Status);
    }

    public void Dispose()
    {
        _world.Dispose();
    }
}
