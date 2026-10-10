using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace Coclico.Modules.DiskAnalyzer.Services;

public sealed class DiskItemNode
{
    public string Name { get; }
    public string FullName { get; }
    public bool IsDirectory { get; }
    public long SizeBytes { get; internal set; }
    public long FileCount { get; internal set; }
    public List<DiskItemNode> Children { get; } = [];

    public DiskItemNode(string name, string fullName, bool isDirectory)
    {
        Name = name;
        FullName = fullName;
        IsDirectory = isDirectory;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var culture = CultureInfo.GetCultureInfo("fr-FR");
        return unit <= 1
            ? string.Format(culture, "{0:0} {1}", value, units[unit])
            : string.Format(culture, "{0:0.#} {1}", value, units[unit]);
    }
}

public sealed record DiskScanProgress(long Bytes, int Files, int Folders, string CurrentPath);

public sealed class DiskZoneSnapshot
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public long SizeBytes { get; init; }
    public long FileCount { get; init; }
    public required List<DiskZoneSnapshot> Children { get; init; } = [];
}

public static class DiskAnalyzerService
{
    public static List<DriveInfo> GetReadyDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static async Task<DiskItemNode> ScanAsync(string rootPath, IProgress<DiskScanProgress>? progress, Action<DiskItemNode>? onWaveCompleted = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var rootInfo = new DirectoryInfo(rootPath);
            var rootName = string.IsNullOrEmpty(rootInfo.Name) ? rootPath : rootInfo.Name;
            var root = new DiskItemNode(rootName, rootPath, true);

            var nodes = new ConcurrentDictionary<string, DiskItemNode>(StringComparer.OrdinalIgnoreCase);
            _ = nodes.TryAdd(rootPath, root);

            long totalBytes = 0;
            int totalFiles = 0;
            int totalFolders = 0;
            var pending = new ConcurrentQueue<DirectoryInfo>();
            pending.Enqueue(rootInfo);

            int maxDop = Math.Max(2, Environment.ProcessorCount);
            int reportCounter = 0;

            while (!pending.IsEmpty && !ct.IsCancellationRequested)
            {
                var wave = new List<DirectoryInfo>();
                while (pending.TryDequeue(out DirectoryInfo? dir))
                {
                    wave.Add(dir);
                }

                _ = Parallel.ForEach(wave, new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct }, dirInfo =>
                {
                    if (!nodes.TryGetValue(dirInfo.FullName, out DiskItemNode? node))
                    {
                        return;
                    }

                    try
                    {
                        foreach (FileInfo file in dirInfo.EnumerateFiles())
                        {
                            try
                            {
                                node.SizeBytes += file.Length;
                                node.FileCount++;
                                _ = Interlocked.Add(ref totalBytes, file.Length);
                                _ = Interlocked.Increment(ref totalFiles);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        foreach (DirectoryInfo sub in dirInfo.EnumerateDirectories())
                        {
                            if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }

                            var child = new DiskItemNode(
                                string.IsNullOrEmpty(sub.Name) ? sub.FullName : sub.Name,
                                sub.FullName,
                                true);
                            if (nodes.TryAdd(sub.FullName, child))
                            {
                                node.Children.Add(child);
                                pending.Enqueue(sub);
                                _ = Interlocked.Increment(ref totalFolders);
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (Interlocked.Increment(ref reportCounter) % 512 == 0)
                    {
                        progress?.Report(new DiskScanProgress(
                            Interlocked.Read(ref totalBytes),
                            totalFiles,
                            totalFolders,
                            dirInfo.FullName));
                    }
                });

                if (!ct.IsCancellationRequested)
                {
                    progress?.Report(new DiskScanProgress(
                        Interlocked.Read(ref totalBytes),
                        totalFiles,
                        totalFolders,
                        rootPath));
                    onWaveCompleted?.Invoke(root);
                }
            }

            ct.ThrowIfCancellationRequested();

            _ = SumSizes(root);
            progress?.Report(new DiskScanProgress(root.SizeBytes, (int)root.FileCount, totalFolders, rootPath));
            return root;
        }, ct).ConfigureAwait(false);
    }

    public static DiskZoneSnapshot? BuildSnapshot(DiskItemNode root, int maxDepth = 3, int maxChildren = 30)
    {
        var sums = new Dictionary<DiskItemNode, long>();
        var files = new Dictionary<DiskItemNode, long>();
        var stack = new Stack<(DiskItemNode Node, bool Expanded)>();
        stack.Push((root, false));

        while (stack.Count > 0)
        {
            (DiskItemNode node, bool expanded) = stack.Pop();
            if (expanded)
            {
                long sum = node.SizeBytes;
                long count = node.FileCount;
                foreach (DiskItemNode child in node.Children)
                {
                    if (sums.TryGetValue(child, out long childSum))
                    {
                        sum += childSum;
                    }

                    if (files.TryGetValue(child, out long childFiles))
                    {
                        count += childFiles;
                    }
                }

                sums[node] = sum;
                files[node] = count;
            }
            else
            {
                stack.Push((node, true));
                foreach (DiskItemNode child in node.Children)
                {
                    stack.Push((child, false));
                }
            }
        }

        return Convert(root, 0);

        DiskZoneSnapshot? Convert(DiskItemNode node, int depth)
        {
            if (!sums.TryGetValue(node, out long size) || size <= 0)
            {
                return null;
            }

            var children = new List<DiskZoneSnapshot>();
            if (depth < maxDepth)
            {
                var sorted = node.Children
                    .Select(c => (Node: c, Size: sums.GetValueOrDefault(c), Files: files.GetValueOrDefault(c)))
                    .Where(c => c.Size > 0)
                    .OrderByDescending(c => c.Size)
                    .ToList();

                foreach (var (child, _, _) in sorted.Take(maxChildren))
                {
                    DiskZoneSnapshot? converted = Convert(child, depth + 1);
                    if (converted != null)
                    {
                        children.Add(converted);
                    }
                }

                long restSize = sorted.Skip(maxChildren).Sum(c => c.Size);
                if (restSize > 0)
                {
                    children.Add(new DiskZoneSnapshot
                    {
                        Name = $"Autres ({sorted.Count - Math.Min(maxChildren, sorted.Count)} éléments)",
                        FullName = node.FullName,
                        SizeBytes = restSize,
                        FileCount = sorted.Skip(maxChildren).Sum(c => c.Files),
                        Children = []
                    });
                }
            }

            return new DiskZoneSnapshot
            {
                Name = string.IsNullOrEmpty(node.Name) ? node.FullName : node.Name,
                FullName = node.FullName,
                SizeBytes = size,
                FileCount = files.GetValueOrDefault(node),
                Children = children
            };
        }
    }

    private static long SumSizes(DiskItemNode node)
    {
        foreach (DiskItemNode child in node.Children)
        {
            node.SizeBytes += SumSizes(child);
            node.FileCount += child.FileCount;
        }

        node.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return node.SizeBytes;
    }
}
