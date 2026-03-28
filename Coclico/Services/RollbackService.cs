#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Coclico.Services;

public sealed class RollbackService : IRollbackService, IDisposable
{
    private const int MemoryCapacity = 50;

    private readonly string _snapshotsDir;
    private readonly ConcurrentDictionary<string, (SnapshotMetadata Meta, string Json)> _memory = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public RollbackService()
    {
        _snapshotsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Coclico", "snapshots");
        Directory.CreateDirectory(_snapshotsDir);
        LoggingService.LogInfo($"[Rollback] Initialisé → {_snapshotsDir}");
    }

    public string CreateSnapshot<T>(string contextName, T state)
    {
        LoggingService.LogInfo($"[RollbackService.CreateSnapshot] Entry — contextName={contextName}");
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(state, _json);
            var meta = new SnapshotMetadata(
                SnapshotId: id,
                ContextName: contextName,
                CreatedAt: DateTimeOffset.UtcNow,
                StateTypeName: typeof(T).FullName ?? typeof(T).Name,
                SizeBytes: Encoding.UTF8.GetByteCount(json));

            EnqueueMemory(id, meta, json);
            PersistToDisk(id, meta, json);

            LoggingService.LogInfo($"[Rollback] Snapshot créé : {id} ({contextName}, {meta.SizeBytes} B)");
            LoggingService.LogInfo($"[RollbackService.CreateSnapshot] Exit — result=snapshotId={id}");
            return id;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"RollbackService.CreateSnapshot({contextName})");
            return string.Empty;
        }
    }

    public T? Rollback<T>(string snapshotId)
    {
        LoggingService.LogInfo($"[RollbackService.Rollback] Entry — snapshotId={snapshotId}");
        if (string.IsNullOrEmpty(snapshotId)) return default;

        try
        {
            if (_memory.TryGetValue(snapshotId, out var entry))
            {
                LoggingService.LogInfo($"[RollbackService.Rollback] Exit — result=RestoredFromMemory({snapshotId})");
                return JsonSerializer.Deserialize<T>(entry.Json, _json);
            }

            var filePath = SnapshotPath(snapshotId);
            if (!File.Exists(filePath)) return default;

            var encryptedBytes = File.ReadAllBytes(filePath);
            SnapshotEnvelope? envelope;
            try
            {
                var decrypted = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(
                    Encoding.UTF8.GetString(decrypted), _json);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "RollbackService.Rollback.Unprotect");
                envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(
                    Encoding.UTF8.GetString(encryptedBytes), _json);
            }

            if (envelope?.StateJson is null) return default;

            LoggingService.LogInfo($"[Rollback] Restauré depuis disque : {snapshotId}");
            LoggingService.LogInfo($"[RollbackService.Rollback] Exit — result=RestoredFromDisk({snapshotId})");
            return JsonSerializer.Deserialize<T>(envelope.StateJson, _json);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"RollbackService.Rollback({snapshotId})");
            return default;
        }
    }

    public int GetSnapshotCount() => _memory.Count;

    public IReadOnlyList<SnapshotMetadata> GetHistory(int maxCount = 20)
    {
        return _memory.Values
            .Select(e => e.Meta)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxCount)
            .ToList();
    }

    public void Delete(string snapshotId)
    {
        _memory.TryRemove(snapshotId, out _);
        try
        {
            var path = SnapshotPath(snapshotId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public void Prune(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;

        foreach (var kv in _memory.ToArray())
            if (kv.Value.Meta.CreatedAt < cutoff)
                Delete(kv.Key);

        try
        {
            foreach (var file in Directory.GetFiles(_snapshotsDir, "*.json"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff.UtcDateTime)
                    File.Delete(file);
            }
        }
        catch { }
    }

    private void EnqueueMemory(string id, SnapshotMetadata meta, string json)
    {
        _memory[id] = (meta, json);
        _insertionOrder.Enqueue(id);

        while (_memory.Count > MemoryCapacity && _insertionOrder.TryDequeue(out var old))
            _memory.TryRemove(old, out _);
    }

    private void PersistToDisk(string id, SnapshotMetadata meta, string stateJson)
    {
        try
        {
            var envelope = new SnapshotEnvelope
            {
                SnapshotId = meta.SnapshotId,
                ContextName = meta.ContextName,
                CreatedAt = meta.CreatedAt,
                StateTypeName = meta.StateTypeName,
                StateJson = stateJson,
            };
            var plainText = JsonSerializer.Serialize(envelope, _json);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SnapshotPath(id), encrypted);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"RollbackService.PersistToDisk({id})");
        }
    }

    private string SnapshotPath(string id) =>
        Path.Combine(_snapshotsDir, $"{id}.json");

    public void Dispose()
    {
        Prune(TimeSpan.FromDays(30));
    }

    private sealed class SnapshotEnvelope
    {
        public string SnapshotId { get; set; } = string.Empty;
        public string ContextName { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string StateTypeName { get; set; } = string.Empty;
        public string? StateJson { get; set; }
    }
}
