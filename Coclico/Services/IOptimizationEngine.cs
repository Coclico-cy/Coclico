#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed record OptimizationReport(
    DateTimeOffset CycleStart,
    DateTimeOffset CycleEnd,
    int AnomaliesDetected,
    int ActionsApplied,
    int RollbacksCreated,
    IReadOnlyList<string> ActionLog,
    string? RcaSummary = null);

public interface IOptimizationEngine
{
    void Start(CancellationToken ct = default);

    void Stop();

    OptimizationReport? LastReport { get; }
}
