#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Coclico.Services.Algorithms;

namespace Coclico.Services;

public sealed class OptimizationEngineService(
    IDynamicTracer tracer,
    IRollbackService rollback,
    IResourceAllocator allocator,
    IAiService ai,
    ISourceAnalyzer? sourceAnalyzer = null,
    IAuditLog? audit = null) : IOptimizationEngine, IDisposable
{
    private static readonly TimeSpan MemCleanLatencyThreshold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QosLatencyThreshold = TimeSpan.FromMilliseconds(500);
    private const int CycleIntervalMs = 30_000;

    private readonly IsolationForestDetector _anomalyDetector = new(
        numTrees: 100, subsampleSize: 64, anomalyThreshold: 0.65);
    private readonly AdaptiveScorer _scorer = new();
    private const double ActionThreshold = 0.6;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private readonly Channel<SystemMetrics> _metricsChannel = Channel.CreateBounded<SystemMetrics>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly Channel<string> _decisionChannel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });

    public OptimizationReport? LastReport { get; private set; }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private async Task<OptimizationAction?> RunCognitiveDecisionAsync(
        SystemMetrics metrics,
        List<string> log,
        CancellationToken ct)
    {
        LoggingService.LogInfo("[OptimizationEngineService.RunCognitiveDecisionAsync] Entry");
        try
        {
            var prompt = BuildPrompt(metrics);
            var sb = new StringBuilder();

            await foreach (var token in ai.SendSystemPromptAsync(prompt, ct).ConfigureAwait(false))
                sb.Append(token);

            var rawResponse = sb.ToString().Trim();
            log.Add($"[LLama] Réponse brute ({rawResponse.Length} chars)");

            return ExtractActionFromResponse(rawResponse, log);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "OptEngine.CognitiveDecision");
            log.Add($"[LLama] Erreur — bascule sur règles déterministes : {ex.Message}");
            return RunDeterministicDecision(metrics, log);
        }
    }

    private string BuildPrompt(SystemMetrics m)
    {
        var metricsJson = JsonSerializer.Serialize(m, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var hotspotContext = BuildHotspotContext(m);

        bool hasAnomalies = m.EwmaAnomalies is { Count: > 0 };
        string rcaInstruction = hasAnomalies
            ? "\"RcaSummary\":\"[explication courte de la cause racine et recommandation]\","
            : "\"RcaSummary\":null,";

        return
            "Tu es un administrateur système autonome Windows. " +
            "Analyse ces métriques et réponds UNIQUEMENT en JSON valide avec ce format exact, " +
            "ou réponds avec {\"NoAction\":true,\"RcaSummary\":null} si tout est normal.\n" +
            "Format attendu : {\"ProcessId\":1234,\"ProcessName\":\"chrome\"," +
            $"\"TargetPriority\":\"BelowNormal\",\"Reason\":\"...\",{rcaInstruction}\"NoAction\":false}}\n" +
            "Priorités valides : Idle, BelowNormal, Normal, AboveNormal, High\n" +
            "RcaSummary : analyse causale courte (max 2 phrases) si anomalie détectée, null sinon.\n" +
            "N'écris RIEN d'autre que le JSON.\n\n" +
            $"Métriques système : {metricsJson}" +
            hotspotContext;
    }

    private string BuildHotspotContext(SystemMetrics? metrics = null)
    {
        var sb = new StringBuilder();

        if (metrics?.EwmaAnomalies is { Count: > 0 } anomalies)
        {
            sb.AppendLine("\n\nAnomalies statistiques EWMA (Z-Score) détectées :");
            foreach (var a in anomalies)
                sb.AppendLine($"  ⚠ {a}");
        }

        var report = sourceAnalyzer?.LastReport;
        if (report is { Hotspots.Count: > 0 })
        {
            sb.AppendLine("\nHotspots de complexité détectés dans le code (top 3) :");
            int count = 0;
            foreach (var h in report.Hotspots)
            {
                if (count++ >= 3) break;
                sb.AppendLine($"  - {h.ClassName}.{h.MethodName} : CC={h.CyclomaticComplexity}, " +
                              $"CogCC={h.CognitiveComplexity}, {h.LineCount} lignes, Sévérité={h.Severity}");
            }
        }

        if (sb.Length == 0) return string.Empty;
        sb.Append("Ces informations sont indicatives. Ne change pas le format JSON de ta réponse.");
        return sb.ToString();
    }

    private static OptimizationAction? ExtractActionFromResponse(string response, List<string> log)
    {
        int searchFrom = 0;

        while (searchFrom < response.Length)
        {
            int start = response.IndexOf('{', searchFrom);
            if (start < 0) break;

            int depth = 0;
            int end = -1;
            for (int i = start; i < response.Length; i++)
            {
                if (response[i] == '{') depth++;
                else if (response[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }

            if (end < 0) break;

            var candidate = response[start..(end + 1)];
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var action = JsonSerializer.Deserialize<OptimizationAction>(candidate, _jsonOpts);
                if (action is not null)
                {
                    log.Add($"[LLama] Action désérialisée : {action.TargetPriority} sur PID {action.ProcessId} ({action.ProcessName})");
                    return action;
                }
            }
            catch (JsonException) { }

            searchFrom = end + 1;
        }

        log.Add("[LLama] Aucun JSON valide trouvé dans la réponse — no-op.");
        return null;
    }

    private OptimizationAction? RunDeterministicDecision(SystemMetrics metrics, List<string> log)
    {
        var memStats = tracer.GetAggregate("MemoryCleaner.FullClean");
        var qosStats = tracer.GetAggregate("QoS.ApplyProfile");

        if (memStats is not null && memStats.P95 > MemCleanLatencyThreshold)
            log.Add($"[Fallback] MemCleaner P95={memStats.P95.TotalSeconds:F1}s > seuil");

        if (qosStats is not null && qosStats.P95 > QosLatencyThreshold)
            log.Add($"[Fallback] QoS P95={qosStats.P95.TotalMilliseconds:F0}ms > seuil");

        double score = _scorer.ComputeScore(metrics.CpuPercent, metrics.RamUsedPercent,
            metrics.MemCleanP95Ms ?? 0, metrics.TopProcesses.Count);
        if (score < ActionThreshold) return null;

        foreach (var proc in metrics.TopProcesses)
        {
            if (IsCriticalProcess(proc.Name)) continue;
            return new OptimizationAction(
                ProcessId: proc.Pid,
                ProcessName: proc.Name,
                TargetPriority: "BelowNormal",
                Reason: $"[Fallback] CPU {metrics.CpuPercent:F1}% — throttle processus lourd ({proc.WorkingSetMb} MB)",
                NoAction: false);
        }

        return null;
    }

    private SystemMetrics CollectMetrics()
    {
        var ram = MemoryCleanerService.GetRamInfo();
        var cpu = MemoryCleanerService.GetSystemCpuPercent();
        var top = MemoryCleanerService.GetTopProcessesByMemory(5);
        var memAgg = tracer.GetAggregate("MemoryCleaner.FullClean");

        double diskIoProxy = memAgg?.P95.TotalMilliseconds ?? 0.0;
        var (score, isAnomaly, details) = _anomalyDetector.Observe(cpu, ram.PhysUsedPercent, diskIoProxy);
        var anomalies = isAnomaly ? details.ToList() : new List<string>();

        if (anomalies.Count > 0)
            LoggingService.LogInfo($"[OptEngine] Anomalies IsolationForest détectées (score={score:F3}) : {string.Join(", ", anomalies)}");

        return new SystemMetrics(
            CpuPercent: cpu,
            RamUsedMb: ram.UsedPhysBytes / (1024 * 1024),
            RamTotalMb: ram.TotalPhysBytes / (1024 * 1024),
            RamUsedPercent: ram.PhysUsedPercent,
            MemCleanP95Ms: memAgg?.P95.TotalMilliseconds,
            TopProcesses: top,
            EwmaAnomalies: anomalies);
    }

    public void Start(CancellationToken ct = default)
    {
        LoggingService.LogInfo("[OptimizationEngineService.Start] Entry");
        if (_loopTask is { IsCompleted: false }) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _loopTask = Task.WhenAll(
            Stage1CollectAsync(_cts.Token),
            Stage2DecideAsync(_cts.Token),
            Stage3ExecuteAsync(_cts.Token));
        LoggingService.LogInfo($"[OptEngine] Pipeline 3-étages démarré (intervalle {CycleIntervalMs / 1000}s)");
    }

    public void Stop()
    {
        LoggingService.LogInfo("[OptimizationEngineService.Stop] Entry");
        _cts?.Cancel();
        _metricsChannel.Writer.TryComplete();
        _decisionChannel.Writer.TryComplete();
        LoggingService.LogInfo("[OptEngine] Pipeline arrêté.");
    }

    private async Task Stage1CollectAsync(CancellationToken ct)
    {
        LoggingService.LogInfo("[OptimizationEngineService.Stage1CollectAsync] Entry");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var metrics = CollectMetrics();
                    _metricsChannel.Writer.TryWrite(metrics);
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "OptEngine.Stage1.Collect");
                }
                await Task.Delay(CycleIntervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { _metricsChannel.Writer.TryComplete(); }
    }

    private async Task Stage2DecideAsync(CancellationToken ct)
    {
        LoggingService.LogInfo("[OptimizationEngineService.Stage2DecideAsync] Entry");
        try
        {
            await foreach (var metrics in _metricsChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    string decision;
                    string decisionMode;
                    string promptUsed = string.Empty;
                    if (ai.IsInitialized)
                    {
                        decisionMode = "Cognitive";
                        promptUsed = BuildPrompt(metrics);
                        var sb = new StringBuilder();
                        await foreach (var token in ai.SendSystemPromptAsync(promptUsed, ct).ConfigureAwait(false))
                            sb.Append(token);
                        decision = sb.ToString().Trim();
                    }
                    else
                    {
                        decisionMode = "Fallback";
                        var fallbackAction = RunDeterministicDecision(metrics, new List<string>());
                        decision = fallbackAction is null
                            ? "{\"NoAction\":true}"
                            : JsonSerializer.Serialize(fallbackAction, _jsonOpts);
                    }

                    if (audit is not null)
                    {
                        _ = audit.LogAsync(new AuditEntry(
                            Timestamp: DateTimeOffset.UtcNow,
                            Actor: "OptimizationEngine",
                            Action: "AiDecision",
                            Target: "SystemMetrics",
                            Success: true,
                            Details: $"Mode={decisionMode} | Response={decision.Length} chars",
                            AiDecision: new AiDecisionContext(
                                Prompt: promptUsed,
                                RawResponse: decision,
                                DecisionMode: decisionMode)));
                    }

                    _decisionChannel.Writer.TryWrite(decision);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "OptEngine.Stage2.Decide");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { _decisionChannel.Writer.TryComplete(); }
    }

    private async Task Stage3ExecuteAsync(CancellationToken ct)
    {
        LoggingService.LogInfo("[OptimizationEngineService.Stage3ExecuteAsync] Entry");
        try
        {
            await foreach (var rawResponse in _decisionChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var log = new List<string>();
                    var action = ExtractActionFromResponse(rawResponse, log);

                    if (action is { NoAction: false, ProcessId: not null })
                    {
                        var currentPriority = allocator.GetProcessPriority(action.ProcessId.Value);
                        var snapshotId = rollback.CreateSnapshot(
                            $"OptEngine.Before.{action.ProcessName}",
                            new { action.ProcessId, action.ProcessName, PriorityBefore = currentPriority?.ToString() });

                        var level = ParsePriorityLevel(action.TargetPriority);
                        var result = allocator.SetProcessPriority(action.ProcessId.Value, level);

                        log.Add(result.Success
                            ? $"✓ PID {action.ProcessId} ({action.ProcessName}) → {level}"
                            : $"✗ Échec QoS PID {action.ProcessId} : {result.Message}");

                        if (!string.IsNullOrWhiteSpace(action.RcaSummary))
                            log.Add($"[RCA] {action.RcaSummary}");

                        LastReport = new OptimizationReport(
                            CycleStart: DateTimeOffset.UtcNow,
                            CycleEnd: DateTimeOffset.UtcNow,
                            AnomaliesDetected: 1,
                            ActionsApplied: result.Success ? 1 : 0,
                            RollbacksCreated: string.IsNullOrEmpty(snapshotId) ? 0 : 1,
                            ActionLog: log,
                            RcaSummary: action.RcaSummary);
                    }
                    else
                    {
                        log.Add("Cycle OK — aucune action requise.");
                    }

                    LoggingService.LogInfo($"[OptEngine] Stage3 — {string.Join(" | ", log)}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "OptEngine.Stage3.Execute");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static PriorityLevel ParsePriorityLevel(string? value) => value?.ToLowerInvariant() switch
    {
        "idle" => PriorityLevel.Idle,
        "belownormal" => PriorityLevel.BelowNormal,
        "abovenormal" => PriorityLevel.AboveNormal,
        "high" => PriorityLevel.High,
        "realtime" => PriorityLevel.RealTime,
        _ => PriorityLevel.Normal,
    };

    private static bool IsCriticalProcess(string name)
    {
        ReadOnlySpan<string> critical =
        [
            "system", "smss", "csrss", "wininit", "winlogon", "lsass",
            "services", "svchost", "explorer", "coclico",
        ];
        foreach (var c in critical)
            if (name.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

internal sealed record SystemMetrics(
    double CpuPercent,
    long RamUsedMb,
    long RamTotalMb,
    double RamUsedPercent,
    double? MemCleanP95Ms,
    IReadOnlyList<MemoryCleanerService.ProcessMemInfo> TopProcesses,
    IReadOnlyList<string>? EwmaAnomalies = null);

internal sealed class AdaptiveScorer
{
    private double _wCpu = 0.4;
    private double _wRam = 0.3;
    private double _wDisk = 0.2;
    private double _wHistory = 0.1;

    public double ComputeScore(double cpuPct, double ramPct, double diskIoMs, int recentActions)
    {
        double cpuScore = Math.Max(0, (cpuPct - 50) / 50);
        double ramScore = Math.Max(0, (ramPct - 60) / 40);
        double diskScore = Math.Min(1, diskIoMs / 5000);
        double histScore = Math.Min(1, recentActions / 5.0);

        return _wCpu * cpuScore + _wRam * ramScore + _wDisk * diskScore + _wHistory * histScore;
    }

    public void RecordOutcome(bool wasEffective)
    {
        if (!wasEffective)
        {
            _wCpu = Math.Min(0.6, _wCpu + 0.02);
            _wRam = Math.Max(0.15, _wRam - 0.01);
            _wDisk = Math.Max(0.1, _wDisk - 0.005);
            _wHistory = Math.Max(0.05, _wHistory - 0.005);
            Normalize();
        }
    }

    private void Normalize()
    {
        double sum = _wCpu + _wRam + _wDisk + _wHistory;
        _wCpu /= sum; _wRam /= sum; _wDisk /= sum; _wHistory /= sum;
    }
}
