using System.Diagnostics;

namespace Coclico.Services;

public sealed class ResourceAllocatorService : IResourceAllocator
{
    private const long BytesPerMb = 1024 * 1024;

    private static readonly Dictionary<PriorityLevel, ProcessPriorityClass> _priorityMap = new()
    {
        [PriorityLevel.Idle] = ProcessPriorityClass.Idle,
        [PriorityLevel.BelowNormal] = ProcessPriorityClass.BelowNormal,
        [PriorityLevel.Normal] = ProcessPriorityClass.Normal,
        [PriorityLevel.AboveNormal] = ProcessPriorityClass.AboveNormal,
        [PriorityLevel.High] = ProcessPriorityClass.High,
        [PriorityLevel.RealTime] = ProcessPriorityClass.RealTime,
    };

    private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "smss", "lsass", "services",
        "wininit", "dwm", "winlogon", "svchost", "explorer"
    };

    public QosResult SetProcessPriority(int processId, PriorityLevel level)
    {
        LoggingService.LogDebug($"[ResourceAllocatorService.SetProcessPriority] Entry — processId={processId}, priority={level}");
        try
        {
            if (level == PriorityLevel.RealTime)
            {
                LoggingService.LogWarning($"[ResourceAllocatorService] RealTime priority refused for PID={processId} for system stability. Clamping to High.");
                level = PriorityLevel.High;
            }

            using var proc = Process.GetProcessById(processId);
            string name = proc.ProcessName;

            if (ProtectedSystemProcesses.Contains(name))
            {
                return Fail(processId, $"Cannot modify priority of critical system process '{name}'");
            }

            proc.PriorityClass = _priorityMap[level];
            LoggingService.LogDebug($"[QoS] PID {processId} ({name}) → {level}");
            LoggingService.LogDebug($"[ResourceAllocatorService.SetProcessPriority] Exit — result=Success({name})");
            return new QosResult(true, $"Priority set to {level}", processId, name);
        }
        catch (ArgumentException)
        {
            return Fail(processId, "Process not found");
        }
        catch (InvalidOperationException)
        {
            return Fail(processId, "Process has exited");
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80004005))
        {
            return Fail(processId, "Access denied (system process)");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"QoS.SetPriority PID={processId}");
            return Fail(processId, ex.Message);
        }
    }

    public QosResult SetProcessorAffinity(int processId, long affinityMask)
    {
        LoggingService.LogDebug($"[ResourceAllocatorService.SetProcessorAffinity] Entry — processId={processId}, mask=0x{affinityMask:X}");
        try
        {
            using var proc = Process.GetProcessById(processId);
            string name = proc.ProcessName;
            proc.ProcessorAffinity = new IntPtr(affinityMask);
            LoggingService.LogDebug($"[QoS] PID {processId} ({name}) affinity → 0x{affinityMask:X}");
            LoggingService.LogDebug($"[ResourceAllocatorService.SetProcessorAffinity] Exit — result=Success({name})");
            return new QosResult(true, $"Affinity set to 0x{affinityMask:X}", processId, name);
        }
        catch (ArgumentException)
        {
            return Fail(processId, "Process not found");
        }
        catch (InvalidOperationException)
        {
            return Fail(processId, "Process has exited");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"QoS.SetAffinity PID={processId}");
            return Fail(processId, ex.Message);
        }
    }

    public QosResult TrimProcessWorkingSet(int processId)
    {
        LoggingService.LogDebug($"[ResourceAllocatorService.TrimProcessWorkingSet] Entry — processId={processId}");
        try
        {
            using var proc = Process.GetProcessById(processId);
            string name = proc.ProcessName;
            long before = proc.WorkingSet64 / BytesPerMb;

            MemoryCleanerService.TrimProcessWorkingSetById(processId);
            proc.Refresh();
            long after = proc.WorkingSet64 / BytesPerMb;
            LoggingService.LogDebug($"[ResourceAllocatorService.TrimProcessWorkingSet] Exit — result=Trimmed({name}, {before}MB → {after}MB)");
            return new QosResult(true, $"WS trimmed (was {before} MB, now {after} MB)", processId, name);
        }
        catch (ArgumentException)
        {
            return Fail(processId, "Process not found");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"QoS.TrimWS PID={processId}");
            return Fail(processId, ex.Message);
        }
    }

    public QosResult ApplyProfile(int processId, PriorityLevel level, long? affinityMask = null)
    {
        LoggingService.LogDebug($"[ResourceAllocatorService.ApplyProfile] Entry — processId={processId}, level={level}");

        QosResult priorityResult = SetProcessPriority(processId, level);
        if (!priorityResult.Success)
        {
            return priorityResult;
        }

        if (affinityMask.HasValue)
        {
            QosResult affinityResult = SetProcessorAffinity(processId, affinityMask.Value);
            if (!affinityResult.Success)
            {
                return affinityResult;
            }
        }

        LoggingService.LogDebug($"[ResourceAllocatorService.ApplyProfile] Exit — result=ProfileApplied({level})");
        return new QosResult(true, $"Profile applied: {level}" + (affinityMask.HasValue ? $" + affinity 0x{affinityMask.Value:X}" : ""),
            processId, priorityResult.ProcessName);
    }

    public PriorityLevel? GetProcessPriority(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return ToPriorityLevel(proc.PriorityClass);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ProcessQosSnapshot> GetSystemSnapshot()
    {
        var result = new List<ProcessQosSnapshot>(256);
        foreach (Process proc in Process.GetProcesses())
        {
            try
            {
                PriorityLevel priority = PriorityLevel.Normal;
                try
                {
                    priority = ToPriorityLevel(proc.PriorityClass);
                }
                catch (Exception exSwallow)
                {
                    LoggingService.LogException(exSwallow, "ResourceAllocatorService.SnapshotPriority");
                }

                result.Add(new ProcessQosSnapshot(
                    Pid: proc.Id,
                    Name: proc.ProcessName,
                    Priority: priority,
                    AffinityMask: proc.ProcessorAffinity.ToInt64(),
                    WorkingSetMb: proc.WorkingSet64 / BytesPerMb));
            }
            catch (Exception exSwallow)
            {
                LoggingService.LogException(exSwallow, "ResourceAllocatorService.SnapshotEntry");
            }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }

        return result;
    }

    private static PriorityLevel ToPriorityLevel(ProcessPriorityClass cls)
    {
        foreach (KeyValuePair<PriorityLevel, ProcessPriorityClass> kv in _priorityMap)
        {
            if (kv.Value == cls)
            {
                return kv.Key;
            }
        }

        return PriorityLevel.Normal;
    }

    private static QosResult Fail(int pid, string reason)
    {
        LoggingService.LogDebug($"[QoS] PID {pid} — {reason}");
        return new QosResult(false, reason, pid, string.Empty);
    }
}