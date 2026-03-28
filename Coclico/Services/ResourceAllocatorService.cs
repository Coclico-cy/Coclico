#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Coclico.Services;

public sealed class ResourceAllocatorService(IDynamicTracer? tracer = null) : IResourceAllocator
{
    private readonly IDynamicTracer? _tracer = tracer;

    private static readonly Dictionary<PriorityLevel, ProcessPriorityClass> _priorityMap = new()
    {
        [PriorityLevel.Idle] = ProcessPriorityClass.Idle,
        [PriorityLevel.BelowNormal] = ProcessPriorityClass.BelowNormal,
        [PriorityLevel.Normal] = ProcessPriorityClass.Normal,
        [PriorityLevel.AboveNormal] = ProcessPriorityClass.AboveNormal,
        [PriorityLevel.High] = ProcessPriorityClass.High,
        [PriorityLevel.RealTime] = ProcessPriorityClass.RealTime,
    };

    public QosResult SetProcessPriority(int processId, PriorityLevel level)
    {
        LoggingService.LogInfo($"[ResourceAllocatorService.SetProcessPriority] Entry — processId={processId}, priority={level}");
        using var span = _tracer?.BeginOperation("QoS.SetPriority", "CPU");
        try
        {
            using var proc = Process.GetProcessById(processId);
            var name = proc.ProcessName;
            proc.PriorityClass = _priorityMap[level];
            LoggingService.LogInfo($"[QoS] PID {processId} ({name}) → {level}");
            LoggingService.LogInfo($"[ResourceAllocatorService.SetProcessPriority] Exit — result=Success({name})");
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
        LoggingService.LogInfo($"[ResourceAllocatorService.SetProcessorAffinity] Entry — processId={processId}, mask=0x{affinityMask:X}");
        using var span = _tracer?.BeginOperation("QoS.SetAffinity", "CPU");
        try
        {
            using var proc = Process.GetProcessById(processId);
            var name = proc.ProcessName;
            proc.ProcessorAffinity = new IntPtr(affinityMask);
            LoggingService.LogInfo($"[QoS] PID {processId} ({name}) affinity → 0x{affinityMask:X}");
            LoggingService.LogInfo($"[ResourceAllocatorService.SetProcessorAffinity] Exit — result=Success({name})");
            return new QosResult(true, $"Affinity set to 0x{affinityMask:X}", processId, name);
        }
        catch (ArgumentException) { return Fail(processId, "Process not found"); }
        catch (InvalidOperationException) { return Fail(processId, "Process has exited"); }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"QoS.SetAffinity PID={processId}");
            return Fail(processId, ex.Message);
        }
    }

    public QosResult TrimProcessWorkingSet(int processId)
    {
        LoggingService.LogInfo($"[ResourceAllocatorService.TrimProcessWorkingSet] Entry — processId={processId}");
        using var span = _tracer?.BeginOperation("QoS.TrimWorkingSet", "Memory");
        try
        {
            using var proc = Process.GetProcessById(processId);
            var name = proc.ProcessName;
            var before = proc.WorkingSet64 / (1024 * 1024);

            MemoryCleanerService.TrimSelfWorkingSet();
            LoggingService.LogInfo($"[ResourceAllocatorService.TrimProcessWorkingSet] Exit — result=Trimmed({name}, was {before} MB)");
            return new QosResult(true, $"WS trimmed (was {before} MB)", processId, name);
        }
        catch (ArgumentException) { return Fail(processId, "Process not found"); }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"QoS.TrimWS PID={processId}");
            return Fail(processId, ex.Message);
        }
    }

    public QosResult ApplyProfile(int processId, PriorityLevel level, long? affinityMask = null)
    {
        LoggingService.LogInfo($"[ResourceAllocatorService.ApplyProfile] Entry — processId={processId}, level={level}");
        using var span = _tracer?.BeginOperation("QoS.ApplyProfile", "CPU",
            new Dictionary<string, object> { ["pid"] = processId, ["level"] = level.ToString() });

        var priorityResult = SetProcessPriority(processId, level);
        if (!priorityResult.Success)
            return priorityResult;

        if (affinityMask.HasValue)
        {
            var affinityResult = SetProcessorAffinity(processId, affinityMask.Value);
            if (!affinityResult.Success)
                return affinityResult;
        }

        LoggingService.LogInfo($"[ResourceAllocatorService.ApplyProfile] Exit — result=ProfileApplied({level})");
        return new QosResult(true, $"Profile applied: {level}" + (affinityMask.HasValue ? $" + affinity 0x{affinityMask.Value:X}" : ""),
            processId, priorityResult.ProcessName);
    }

    public PriorityLevel? GetProcessPriority(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            var cls = proc.PriorityClass;
            foreach (var kv in _priorityMap)
                if (kv.Value == cls) return kv.Key;
            return PriorityLevel.Normal;
        }
        catch { return null; }
    }

    public IReadOnlyList<ProcessQosSnapshot> GetSystemSnapshot()
    {
        var result = new List<ProcessQosSnapshot>(256);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                PriorityLevel priority = PriorityLevel.Normal;
                try
                {
                    var cls = proc.PriorityClass;
                    foreach (var kv in _priorityMap)
                        if (kv.Value == cls) { priority = kv.Key; break; }
                }
                catch { }

                result.Add(new ProcessQosSnapshot(
                    Pid: proc.Id,
                    Name: proc.ProcessName,
                    Priority: priority,
                    AffinityMask: proc.ProcessorAffinity.ToInt64(),
                    WorkingSetMb: proc.WorkingSet64 / (1024 * 1024)));
            }
            catch { }
            finally { try { proc.Dispose(); } catch { } }
        }
        return result;
    }

    private static QosResult Fail(int pid, string reason)
    {
        LoggingService.LogInfo($"[QoS] PID {pid} — {reason}");
        return new QosResult(false, reason, pid, string.Empty);
    }
}
