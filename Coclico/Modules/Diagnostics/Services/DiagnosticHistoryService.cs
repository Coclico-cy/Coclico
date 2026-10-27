namespace Coclico.Modules.Diagnostics.Services;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Services;

public sealed class DiagnosticHistoryService : IDiagnosticHistoryService
{
    private const string Subsystem = "DiagnosticHistoryService";
    private const int MaxRunFilesScanned = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _baseDirectory;
    private readonly int _retentionCount;
    private readonly int _retentionDays;

    public DiagnosticHistoryService(int retentionCount = 30, int retentionDays = 30)
        : this(
            Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "diagnostics"),
            retentionCount,
            retentionDays)
    {
    }

    public DiagnosticHistoryService(string baseDirectory, int retentionCount = 30, int retentionDays = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _retentionCount = Math.Max(1, retentionCount);
        _retentionDays = Math.Max(1, retentionDays);
    }

    public async Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        Directory.CreateDirectory(_baseDirectory);

        string filePath = Path.Join(_baseDirectory, FileNameFor(run.Id));

        string tempPath = filePath + ".tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, run, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(tempPath, filePath, overwrite: true);

        try
        {
            File.SetLastWriteTimeUtc(filePath, run.StartedAtUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Timestamp");
        }

        ApplyRetentionPolicy();
    }

    public Task<IReadOnlyList<DiagnosticRunSummary>> GetRecentAsync(
        int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        List<DiagnosticRunSummary> summaries = [];
        foreach ((DiagnosticRun run, _) in EnumerateRuns(limit))
        {
            summaries.Add(ToSummary(run));
        }

        summaries.Sort(static (a, b) => b.StartedAtUtc.CompareTo(a.StartedAtUtc));
        return Task.FromResult<IReadOnlyList<DiagnosticRunSummary>>(summaries);
    }

    public Task<DiagnosticRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        string filePath = Path.Join(_baseDirectory, FileNameFor(id));
        if (!File.Exists(filePath))
        {
            return Task.FromResult<DiagnosticRun?>(null);
        }

        try
        {
            DiagnosticRun? run = JsonSerializer.Deserialize<DiagnosticRun>(
                File.ReadAllText(filePath), SerializerOptions);
            return Task.FromResult(run);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.GetById");
            return Task.FromResult<DiagnosticRun?>(null);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        string filePath = Path.Join(_baseDirectory, FileNameFor(id));
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private static string FileNameFor(Guid id) => $"{id:N}.json";

    private static DiagnosticRunSummary ToSummary(DiagnosticRun run)
        => new(
            run.Id,
            run.StartedAtUtc,
            run.Mode,
            run.Status,
            run.Findings.Count(f => f.Severity == DiagnosticSeverity.Warning),
            run.Findings.Count(f => f.Severity == DiagnosticSeverity.Error),
            run.Findings.Count(f => f.Severity == DiagnosticSeverity.Critical));

    private IEnumerable<(DiagnosticRun Run, string Path)> EnumerateRuns(int maxRuns)
    {
        if (!Directory.Exists(_baseDirectory))
        {
            yield break;
        }

        int scanned = 0;
        int yielded = 0;
        foreach (string filePath in Directory.EnumerateFiles(_baseDirectory, "*.json"))
        {
            if (yielded >= maxRuns || scanned >= MaxRunFilesScanned)
            {
                yield break;
            }

            scanned++;
            DiagnosticRun? run = TryReadRun(filePath);
            if (run is not null)
            {
                yielded++;
                yield return (run, filePath);
            }
        }
    }

    private static DiagnosticRun? TryReadRun(string filePath)
    {
        try
        {
            return JsonSerializer.Deserialize<DiagnosticRun>(
                File.ReadAllText(filePath), SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Read");
            return null;
        }
    }

    private void ApplyRetentionPolicy()
    {
        try
        {
            List<(DateTime StartedAtUtc, string Path)> files = [];
            foreach (string filePath in Directory.EnumerateFiles(_baseDirectory, "*.json"))
            {
                DateTime written = TryGetTimestampUtc(filePath);
                files.Add((written, filePath));
            }

            DateTime oldestKept = DateTime.UtcNow.AddDays(-_retentionDays);
            int kept = 0;
            foreach ((DateTime startedAt, string filePath) in files.OrderByDescending(f => f.StartedAtUtc))
            {
                if (startedAt < oldestKept)
                {
                    TryDelete(filePath);
                }
                else
                {
                    kept++;
                    if (kept > _retentionCount)
                    {
                        TryDelete(filePath);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Retention");
        }
    }

    private static DateTime TryGetTimestampUtc(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoggingService.LogException(ex, $"{Subsystem}.Delete");
        }
    }
}
