using Coclico.Modules.DiskAnalyzer.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DuplicateFinderTests
{
    private static string CreateTempDir()
    {
        return Path.Combine(Path.GetTempPath(), "coclico-dup-test-" + Guid.NewGuid().ToString("N")[..8]);
    }

    private static void CleanupDir(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch { }
    }

    [Fact]
    public async Task FindDuplicatesAsync_GroupsOnlyRealDuplicates()
    {
        string dir = CreateTempDir();
        string sub = Path.Combine(dir, "Sub");
        _ = Directory.CreateDirectory(sub);

        byte[] contentA = new byte[2048];
        contentA.AsSpan(0, 256).Fill(7);
        byte[] contentB = new byte[2048];
        contentB.AsSpan(0, 256).Fill(9);

        string a1 = Path.Combine(dir, "a1.bin");
        string a2 = Path.Combine(sub, "a2-copy.bin");
        string b1 = Path.Combine(dir, "b1.bin");

        await File.WriteAllBytesAsync(a1, contentA);
        await File.WriteAllBytesAsync(a2, contentA);
        await File.WriteAllBytesAsync(b1, contentB);

        try
        {
            IReadOnlyList<DuplicateGroup> groups = await DuplicateFinderService.FindDuplicatesAsync(
                new DuplicateScanOptions(dir, MinSizeBytes: 1024));

            Assert.Single(groups);
            DuplicateGroup group = groups[0];
            Assert.Equal(2, group.FileCount);
            Assert.Equal(2048 * 2 - 2048, group.WastedBytes);
            Assert.Contains(a1, group.Paths);
            Assert.Contains(a2, group.Paths);
            Assert.Equal("a1.bin", group.FileName);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task FindDuplicatesAsync_IgnoresFilesBelowMinSize()
    {
        string dir = CreateTempDir();
        _ = Directory.CreateDirectory(dir);

        string tiny1 = Path.Combine(dir, "tiny1.bin");
        string tiny2 = Path.Combine(dir, "tiny2.bin");

        await File.WriteAllBytesAsync(tiny1, new byte[100]);
        await File.WriteAllBytesAsync(tiny2, new byte[100]);

        try
        {
            IReadOnlyList<DuplicateGroup> groups = await DuplicateFinderService.FindDuplicatesAsync(
                new DuplicateScanOptions(dir, MinSizeBytes: 1024));

            Assert.Empty(groups);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task FindLargestFilesAsync_ReturnsTopFilesSortedBySize()
    {
        string dir = CreateTempDir();
        _ = Directory.CreateDirectory(dir);

        long[] sizes = [4096, 1024, 16384, 2048, 8192];
        for (int i = 0; i < sizes.Length; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, $"f{i}.bin"), new byte[sizes[i]]);
        }

        try
        {
            IReadOnlyList<LargeFileEntry> top = await DuplicateFinderService.FindLargestFilesAsync(dir, 3);

            Assert.Equal(3, top.Count);
            Assert.Equal("f2.bin", top[0].FileName);
            Assert.Equal(16384, top[0].SizeBytes);
            Assert.Equal("f4.bin", top[1].FileName);
            Assert.Equal("f0.bin", top[2].FileName);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void IndexBySize_And_CandidateGroups_KeepOnlySharedSizes()
    {
        var files = new List<FileMeta>
        {
            new("a", 100),
            new("b", 100),
            new("c", 300),
            new("d", 300),
            new("e", 500)
        };

        Dictionary<long, List<FileMeta>> index = DuplicateFinderService.IndexBySize(files);
        Assert.Equal(3, index.Count);

        List<List<FileMeta>> candidates = DuplicateFinderService.CandidateGroups(index);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, g => Assert.Equal(2, g.Count));
        Assert.DoesNotContain(candidates, g => g.Any(f => f.Path == "e"));
    }

    [Fact]
    public void ToDuplicateGroups_SortsByWastedBytesAndCapsResults()
    {
        List<List<FileMeta>> groups =
        [
            [new FileMeta("big1", 1000), new FileMeta("big2", 1000)],
            [new FileMeta("small1", 10), new FileMeta("small2", 10), new FileMeta("small3", 10)],
            [new FileMeta("huge1", 5000), new FileMeta("huge2", 5000)]
        ];

        IReadOnlyList<DuplicateGroup> result = DuplicateFinderService.ToDuplicateGroups(groups, maxGroups: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("huge1", result[0].FileName);
        Assert.Equal(5000, result[0].WastedBytes);
        Assert.Equal("big1", result[1].FileName);
    }
}
