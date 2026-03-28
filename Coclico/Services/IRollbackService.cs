#nullable enable
using System;
using System.Collections.Generic;

namespace Coclico.Services;

public sealed record SnapshotMetadata(
    string SnapshotId,
    string ContextName,
    DateTimeOffset CreatedAt,
    string StateTypeName,
    long SizeBytes);

public interface IRollbackService
{
    string CreateSnapshot<T>(string contextName, T state);

    T? Rollback<T>(string snapshotId);

    IReadOnlyList<SnapshotMetadata> GetHistory(int maxCount = 20);

    void Delete(string snapshotId);

    void Prune(TimeSpan olderThan);

    int GetSnapshotCount();
}
