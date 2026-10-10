using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace Coclico.Modules.DiskAnalyzer.Services;

public sealed record FileMeta(string Path, long Size);

public sealed record DuplicateScanOptions(
    string Root,
    long MinSizeBytes = 1024 * 1024,
    int MaxGroups = 500);

public sealed record DuplicateScanProgress(int Phase, int FilesExamined, long BytesExamined, string CurrentPath);

public sealed class DuplicateGroup
{
    public required string FileName { get; init; }
    public required string Hash { get; init; }
    public required long SizeBytes { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public int FileCount => Paths.Count;
    public long WastedBytes => SizeBytes * (Paths.Count - 1);
}

public sealed class LargeFileEntry
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
}

public static class DuplicateFinderService
{
    private const int QuickHashBytes = 64 * 1024;

    public static async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() => FindDuplicates(options, progress, ct), ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<LargeFileEntry>> FindLargestFilesAsync(
        string root,
        int count,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() => FindLargestFiles(root, count, progress, ct), ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<DuplicateGroup> FindDuplicates(
        DuplicateScanOptions options,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        ConcurrentBag<FileMeta> bySize = [];
        long bytesExamined = 0;
        int filesExamined = 0;
        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.System
        };

        foreach (string path in Directory.EnumerateFiles(options.Root, "*", enumeration))
        {
            ct.ThrowIfCancellationRequested();
            filesExamined++;

            FileMeta meta = GetMeta(path);
            if (meta.Size < options.MinSizeBytes)
            {
                continue;
            }

            bySize.Add(meta);
            _ = Interlocked.Add(ref bytesExamined, meta.Size);

            if (filesExamined % 250 == 0)
            {
                progress?.Report(new DuplicateScanProgress(1, filesExamined, Volatile.Read(ref bytesExamined), path));
            }
        }

        List<List<FileMeta>> sizeGroups = CandidateGroups(IndexBySize(bySize));

        progress?.Report(new DuplicateScanProgress(2, filesExamined, Volatile.Read(ref bytesExamined), string.Empty));
        List<List<FileMeta>> quickGroups = RefineByHash(sizeGroups, QuickHashBytes, ct);

        progress?.Report(new DuplicateScanProgress(3, filesExamined, Volatile.Read(ref bytesExamined), string.Empty));
        List<List<FileMeta>> fullGroups = RefineByHash(quickGroups, hashPrefixBytes: 0, ct);

        IReadOnlyList<DuplicateGroup> result = ToDuplicateGroups(fullGroups, options.MaxGroups);
        progress?.Report(new DuplicateScanProgress(4, filesExamined, Volatile.Read(ref bytesExamined), string.Empty));
        return result;
    }

    internal static Dictionary<long, List<FileMeta>> IndexBySize(IEnumerable<FileMeta> files)
    {
        Dictionary<long, List<FileMeta>> index = [];
        foreach (FileMeta file in files)
        {
            if (!index.TryGetValue(file.Size, out List<FileMeta>? list))
            {
                list = [];
                index[file.Size] = list;
            }

            list.Add(file);
        }

        return index;
    }

    internal static List<List<FileMeta>> CandidateGroups(Dictionary<long, List<FileMeta>> index)
    {
        List<List<FileMeta>> groups = [];
        foreach (List<FileMeta> group in index.Values)
        {
            if (group.Count > 1)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    internal static List<List<FileMeta>> RefineByHash(List<List<FileMeta>> groups, int hashPrefixBytes, CancellationToken ct)
    {
        ConcurrentDictionary<string, ConcurrentBag<FileMeta>> byHash = new();
        int prefix = hashPrefixBytes;

        Parallel.ForEach(groups, new ParallelOptions { CancellationToken = ct }, group =>
        {
            foreach (FileMeta file in group)
            {
                ct.ThrowIfCancellationRequested();
                string? hash = ComputeHash(file.Path, prefix);
                if (hash != null)
                {
                    ConcurrentBag<FileMeta> bucket = byHash.GetOrAdd(hash, _ => []);
                    bucket.Add(file);
                }
            }
        });

        List<List<FileMeta>> result = [];
        foreach (ConcurrentBag<FileMeta> bag in byHash.Values)
        {
            if (bag.Count > 1)
            {
                result.Add([.. bag]);
            }
        }

        return result;
    }

    internal static IReadOnlyList<DuplicateGroup> ToDuplicateGroups(List<List<FileMeta>> groups, int maxGroups)
    {
        List<DuplicateGroup> result = groups
            .Select(group =>
            {
                List<string> paths = group.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase).ToList();
                return new DuplicateGroup
                {
                    FileName = Path.GetFileName(paths[0]),
                    Hash = group[0].Size.ToString(CultureInfo.InvariantCulture) + "-" + group.Count,
                    SizeBytes = group[0].Size,
                    Paths = paths
                };
            })
            .OrderByDescending(g => g.WastedBytes)
            .ThenBy(g => g.FileName, StringComparer.OrdinalIgnoreCase)
            .Take(maxGroups)
            .ToList();

        return result;
    }

    internal static IReadOnlyList<LargeFileEntry> FindLargestFiles(
        string root,
        int count,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        var top = new SortedSet<(long Size, string Path)>(SizePathComparer.Instance);
        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.System
        };

        long bytesSeen = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*", enumeration))
        {
            ct.ThrowIfCancellationRequested();
            FileMeta meta = GetMeta(path);
            bytesSeen += meta.Size;

            if (top.Count < count)
            {
                _ = top.Add((meta.Size, meta.Path));
            }
            else if (top.Count > 0)
            {
                var min = top.Min;
                if (min.Size < meta.Size)
                {
                    _ = top.Remove(min);
                    _ = top.Add((meta.Size, meta.Path));
                }
            }

            if (progress != null && bytesSeen % (64L * 1024 * 1024) < 1024L * 1024)
            {
                progress.Report(bytesSeen);
            }
        }

        progress?.Report(bytesSeen);
        return top
            .Reverse()
            .Select(entry => new LargeFileEntry { Path = entry.Path, FileName = Path.GetFileName(entry.Path), SizeBytes = entry.Size })
            .ToList();
    }

    private static FileMeta GetMeta(string path)
    {
        long size = 0;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new FileMeta(path, size);
    }

    private static string? ComputeHash(string path, int prefixBytes)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            if (prefixBytes <= 0)
            {
                return Convert.ToHexString(SHA256.HashData(stream));
            }

            byte[] buffer = new byte[Math.Min(prefixBytes, 16 * 1024 * 1024)];
            using var sha = SHA256.Create();
            long remaining = prefixBytes;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0)
                {
                    break;
                }

                _ = sha.TransformBlock(buffer, 0, read, buffer, 0);
                remaining -= read;
            }

            _ = sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash ?? []);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class SizePathComparer : IComparer<(long Size, string Path)>
    {
        public static readonly SizePathComparer Instance = new();

        public int Compare((long Size, string Path) x, (long Size, string Path) y)
        {
            int sizeCompare = x.Size.CompareTo(y.Size);
            return sizeCompare != 0 ? sizeCompare : string.CompareOrdinal(x.Path, y.Path);
        }
    }
}