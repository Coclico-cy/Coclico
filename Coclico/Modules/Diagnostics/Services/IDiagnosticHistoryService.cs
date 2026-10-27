namespace Coclico.Modules.Diagnostics.Services;

using Coclico.Modules.Diagnostics.Models;

public interface IDiagnosticHistoryService
{
    Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiagnosticRunSummary>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task<DiagnosticRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
