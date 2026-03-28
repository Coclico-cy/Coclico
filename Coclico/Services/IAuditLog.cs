#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Coclico.Services;

public sealed record AiDecisionContext(
    string Prompt,
    string RawResponse,
    string DecisionMode);

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string Target,
    bool Success,
    string? Details = null,
    AiDecisionContext? AiDecision = null);

public interface IAuditLog
{
    Task LogAsync(AuditEntry entry);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxCount = 100);

    void Prune(TimeSpan olderThan);
}
