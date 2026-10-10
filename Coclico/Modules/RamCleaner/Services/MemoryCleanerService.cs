using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Coclico.Services;

public static class MemoryCleanerService
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    private static extern int NtSetSystemInformationInt(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemFileCacheSize(IntPtr MinimumFileCacheSize, IntPtr MaximumFileCacheSize, uint Flags);

    [DllImport("advapi32.dll")]
    private static extern int RegFlushKey(IntPtr hKey);

    [DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessHeap();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr HeapCompact(IntPtr hHeap, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int VK_LWIN = 0x5B;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_B = 0x42;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static int GetForegroundProcessId()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return -1;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
        catch { return -1; }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int FlushIpNetTable(uint dwIfIndex);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TokPriv1Luid
    {
        public int Count;
        public long Luid;
        public int Attr;
    }

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? host, string name, ref long pluid);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

    private const int SE_PRIVILEGE_ENABLED = 2;

    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandby = 5;
    private const int MemoryFlushModifiedListFast = 6;
    private const int MemoryPurgeStandbyListFast = 7;

    private static readonly IntPtr HKEY_LOCAL_MACHINE = new(-2147483646);
    private static readonly IntPtr HKEY_CURRENT_USER = new(-2147483647);

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_ALL_TRIM = PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION;

    private static readonly FrozenSet<string> _criticalProcessNames =
        new[] { "smss", "csrss", "wininit", "winlogon", "lsass", "services", "dwm", "explorer", "audiodg", "system", "idle",
                "fontdrvhost", "sihost", "ctfmon", "startmenuexperiencehost", "shellexperiencehost" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> _kernelProcessNames =
        new[] { "smss", "csrss", "wininit", "winlogon", "lsass", "services" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly object _cpuLock = new();
    private static long _cpuIdleLast, _cpuKernelLast, _cpuUserLast;

    private static bool AcquirePrivilege(string privilege)
    {
        try
        {
            IntPtr htok = IntPtr.Zero;
            var tp = new TokPriv1Luid { Count = 1, Luid = 0, Attr = SE_PRIVILEGE_ENABLED };
            if (OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, ref htok))
            {
                bool success = false;
                if (LookupPrivilegeValue(null, privilege, ref tp.Luid))
                {
                    if (AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastWin32Error();
                        success = err == 0;
                        if (!success && err == 1300)
                        {
                            LoggingService.LogWarning($"[MemoryCleaner] Privilege '{privilege}' not assigned in current token (elevation required).");
                        }
                    }
                }
                _ = CloseHandle(htok);
                return success;
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, $"[MemoryCleaner] AcquirePrivilege failed for '{privilege}'"); }
        return false;
    }

    public struct RamInfo
    {
        public long TotalPhysBytes;
        public long AvailPhysBytes;
        public long UsedPhysBytes;
        public long TotalVirtBytes;
        public long AvailVirtBytes;
        public long UsedVirtBytes;
        public long TotalPageBytes;
        public long AvailPageBytes;
        public double PhysUsedPercent;
        public double VirtUsedPercent;
    }

    public static RamInfo GetRamInfo()
    {
        var stat = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref stat))
        {
            return new RamInfo();
        }

        long commitTotal = (long)stat.ullTotalPageFile;
        long commitAvail = (long)stat.ullAvailPageFile;
        long commitUsed = Math.Max(0, commitTotal - commitAvail);

        return new RamInfo
        {
            TotalPhysBytes = (long)stat.ullTotalPhys,
            AvailPhysBytes = (long)stat.ullAvailPhys,
            UsedPhysBytes = (long)(stat.ullTotalPhys - stat.ullAvailPhys),
            TotalVirtBytes = commitTotal,
            AvailVirtBytes = commitAvail,
            UsedVirtBytes = commitUsed,
            TotalPageBytes = (long)stat.ullTotalPageFile,
            AvailPageBytes = (long)stat.ullAvailPageFile,
            PhysUsedPercent = stat.ullTotalPhys > 0
                ? (double)(stat.ullTotalPhys - stat.ullAvailPhys) / stat.ullTotalPhys * 100.0 : 0,
            VirtUsedPercent = commitTotal > 0
                ? (double)commitUsed / commitTotal * 100.0 : 0,
        };
    }

    public static long EmptyWorkingSets()
    {
        return EmptyWorkingSets(protectForeground: false);
    }

    public static long EmptyWorkingSets(bool protectForeground, int? specificExcludePid = null)
    {
        LoggingService.LogInfo($"[MemoryCleanerService.EmptyWorkingSets] Entry — protectForeground={protectForeground}");
        RamInfo before = GetRamInfo();
        _ = AcquirePrivilege("SeDebugPrivilege");
        _ = AcquirePrivilege("SeIncreaseQuotaPrivilege");

        if (!protectForeground)
        {
            _ = NtMemoryCommandRaw(MemoryEmptyWorkingSets);
        }

        int fgPid = protectForeground ? GetForegroundProcessId() : -1;
        int selfPid;
        try { selfPid = Process.GetCurrentProcess().Id; } catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.EmptyWorkingSets"); selfPid = -1; }

        foreach (Process proc in Process.GetProcesses())
        {
            try
            {
                int pid = proc.Id;
                if (pid <= 4 || pid == selfPid || pid == specificExcludePid || (protectForeground && pid == fgPid))
                {
                    continue;
                }

                if (_criticalProcessNames.Contains(proc.ProcessName))
                {
                    continue;
                }

                nint h = OpenProcess(PROCESS_ALL_TRIM, false, pid);
                if (h != IntPtr.Zero)
                {
                    _ = SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                    _ = CloseHandle(h);
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.EmptyWorkingSets.TrimProcess"); }
            finally { try { proc.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.EmptyWorkingSets.DisposeProcess"); } }
        }

        Thread.Sleep(200);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long FlushStandbyList()
    {
        LoggingService.LogInfo("[MemoryCleanerService.FlushStandbyList] Entry");
        RamInfo before = GetRamInfo();
        return NtMemoryCommand(MemoryPurgeStandbyList, before);
    }

    public static long FlushLowPriorityStandbyList()
    {
        RamInfo before = GetRamInfo();
        return NtMemoryCommand(MemoryPurgeLowPriorityStandby, before);
    }

    public static long FlushModifiedPageList()
    {
        RamInfo before = GetRamInfo();
        return NtMemoryCommand(MemoryFlushModifiedList, before);
    }

    public static long FlushCombinedPageList()
    {
        RamInfo before = GetRamInfo();
        _ = NtMemoryCommandRaw(MemoryFlushModifiedList);
        _ = NtMemoryCommandRaw(MemoryPurgeStandbyList);
        Thread.Sleep(200);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long FlushModifiedFileCache()
    {
        RamInfo before = GetRamInfo();
        int r = NtMemoryCommandRaw(MemoryFlushModifiedListFast);
        if (r != 0)
        {
            _ = NtMemoryCommandRaw(MemoryFlushModifiedList);
        }

        Thread.Sleep(150);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long ClearSystemFileCache()
    {
        try
        {
            RamInfo before = GetRamInfo();
            _ = AcquirePrivilege("SeIncreaseQuotaPrivilege");
            _ = SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0);
            Thread.Sleep(150);
            RamInfo after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] ClearSystemFileCache failed"); return 0; }
    }

    public static long FlushRegistryCache()
    {
        try
        {
            RamInfo before = GetRamInfo();
            _ = AcquirePrivilege("SeBackupPrivilege");
            _ = RegFlushKey(HKEY_LOCAL_MACHINE);
            _ = RegFlushKey(HKEY_CURRENT_USER);
            RamInfo after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] FlushRegistryCache failed"); return 0; }
    }

    public static long FlushDnsCache()
    {
        try
        {
            RamInfo before = GetRamInfo();
            _ = DnsFlushResolverCache();
            RamInfo after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] FlushDnsCache failed"); return 0; }
    }

    public static long ForceGcCollect()
    {
        LoggingService.LogInfo("[MemoryCleanerService.ForceGcCollect] Entry");
        RamInfo before = GetRamInfo();
        CollectAndCompactManagedHeap();
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    private static void CollectAndCompactManagedHeap()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static long NtMemoryCommand(int command, RamInfo before)
    {
        try
        {
            _ = NtMemoryCommandRaw(command);
            Thread.Sleep(150);
            RamInfo after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] NtMemoryCommand failed"); return 0; }
    }

    private static int NtMemoryCommandRaw(int command)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0))
        {
            LoggingService.LogWarning("[MemoryCleaner] NtSetSystemInformation requires Windows 10 or later.");
            return -1;
        }

        _ = AcquirePrivilege("SeProfileSingleProcessPrivilege");
        return NtSetSystemInformationInt(SystemMemoryListInformation, ref command, sizeof(int));
    }

    public struct CleanResult
    {
        public long WorkingSetsFreed;
        public long StandbyFreed;
        public long LowPriorityStandbyFreed;
        public long ModifiedPageListFreed;
        public long CombinedPageListFreed;
        public long ModifiedFileCacheFreed;
        public long SystemFileCacheFreed;
        public long RegistryCacheFreed;
        public long DnsCacheFreed;
        public long GcCollectFreed;
        public long StandbyFastFreed;
        public long KernelTrimFreed;
        public long SuperFetchFreed;
        public long HeapCompactFreed;
        public long ClipboardFreed;
        public long ArpCacheFreed;
        public long NetBiosCacheFreed;
        public long AllSessionsFreed;
        public long TotalFreed;
        public RamInfo Before;
        public RamInfo After;
    }

    public static Task<CleanResult> FullCleanAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return FullCleanAsync(protectForeground: false, progress, ct);
    }

    public static async Task<CleanResult> FullCleanAsync(
        bool protectForeground,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[MemoryCleanerService.FullCleanAsync] Entry — protectFg={protectForeground}");

        RamInfo before = GetRamInfo();
        var result = new CleanResult { Before = before };

        progress?.Report("Emptying Working Sets...");
        result.WorkingSetsFreed = await Task.Run(() => EmptyWorkingSets(protectForeground), ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing modified file cache...");
        result.ModifiedFileCacheFreed = await Task.Run(FlushModifiedFileCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Clearing system file cache...");
        result.SystemFileCacheFreed = await Task.Run(ClearSystemFileCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing registry cache...");
        result.RegistryCacheFreed = await Task.Run(FlushRegistryCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging Standby list...");
        result.StandbyFreed = await Task.Run(FlushStandbyList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging low-priority Standby list...");
        result.LowPriorityStandbyFreed = await Task.Run(FlushLowPriorityStandbyList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging combined page list...");
        result.CombinedPageListFreed = await Task.Run(FlushCombinedPageList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing modified page list...");
        result.ModifiedPageListFreed = await Task.Run(FlushModifiedPageList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing DNS cache...");
        result.DnsCacheFreed = await Task.Run(FlushDnsCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report(".NET Garbage Collect...");
        result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Fast Standby purge...");
        result.StandbyFastFreed = await Task.Run(FlushStandbyListFast, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Windows kernel working set...");
        result.KernelTrimFreed = await Task.Run(TrimKernelWorkingSet, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Compacting memory heaps...");
        result.HeapCompactFreed = await Task.Run(CompactAllHeaps, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Clearing clipboard...");
        result.ClipboardFreed = await Task.Run(ClearClipboard, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing ARP cache...");
        result.ArpCacheFreed = await Task.Run(FlushArpCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing NetBIOS cache...");
        result.NetBiosCacheFreed = await Task.Run(FlushNetBiosCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Trimming all sessions working sets...");
        result.AllSessionsFreed = await Task.Run(TrimAllSessionsWorkingSets, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing SuperFetch / SysMain...");
        result.SuperFetchFreed = await FlushSuperFetchAsync(ct);

        await Task.Delay(800, ct);
        result.After = GetRamInfo();

        long sumIndividual = result.WorkingSetsFreed + result.StandbyFreed
            + result.LowPriorityStandbyFreed + result.ModifiedPageListFreed
            + result.CombinedPageListFreed + result.ModifiedFileCacheFreed
            + result.SystemFileCacheFreed + result.RegistryCacheFreed
            + result.DnsCacheFreed + result.GcCollectFreed
            + result.StandbyFastFreed + result.KernelTrimFreed
            + result.SuperFetchFreed + result.HeapCompactFreed
            + result.ClipboardFreed + result.ArpCacheFreed
            + result.NetBiosCacheFreed + result.AllSessionsFreed;
        long globalDelta = Math.Max(0, result.After.AvailPhysBytes - before.AvailPhysBytes);
        result.TotalFreed = Math.Max(sumIndividual, globalDelta);

        progress?.Report("Cleaning complete.");
        return result;
    }

    public static string FormatBytes(long bytes)
    {
        return bytes < 0
            ? "0 B"
            : bytes < 1024L
            ? $"{bytes} B"
            : bytes < 1024L * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB" : $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    private static long FtToLong(FILETIME f)
    {
        return ((long)f.High << 32) | f.Low;
    }

    public static double GetSystemCpuPercent()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
        {
            return 0;
        }

        lock (_cpuLock)
        {
            long i = FtToLong(idle);
            long k = FtToLong(kernel);
            long u = FtToLong(user);
            long di = i - _cpuIdleLast;
            long total = k - _cpuKernelLast + (u - _cpuUserLast);
            _cpuIdleLast = i;
            _cpuKernelLast = k;
            _cpuUserLast = u;
            return total <= 0 ? 0 : Math.Min(100.0, Math.Max(0.0, (total - di) / (double)total * 100.0));
        }
    }

    public static void TrimSelfWorkingSet()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            _ = SetProcessWorkingSetSize(self.Handle, new IntPtr(-1), new IntPtr(-1));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] TrimSelfWorkingSet failed"); }
    }

    public static void TrimProcessWorkingSetById(int processId)
    {
        try
        {
            _ = AcquirePrivilege("SeDebugPrivilege");
            _ = AcquirePrivilege("SeIncreaseQuotaPrivilege");
            nint h = OpenProcess(PROCESS_ALL_TRIM, false, processId);
            if (h != IntPtr.Zero)
            {
                _ = SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                _ = CloseHandle(h);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] TrimProcessWorkingSetById failed"); }
    }

    public struct ProcessMemInfo
    {
        public string Name;
        public long WorkingSetMb;
        public long PrivateMb;
        public int Pid;
    }

    public static List<ProcessMemInfo> GetTopProcessesByMemory(int n = 8)
    {
        var heap = new PriorityQueue<ProcessMemInfo, long>(n + 1);
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                var info = new ProcessMemInfo
                {
                    Name = p.ProcessName,
                    WorkingSetMb = p.WorkingSet64 / (1024 * 1024),
                    PrivateMb = p.PrivateMemorySize64 / (1024 * 1024),
                    Pid = p.Id,
                };
                heap.Enqueue(info, info.WorkingSetMb);

                if (heap.Count > n)
                {
                    _ = heap.Dequeue();
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] GetTopProcessesByMemory: failed to read process info"); }
            finally { try { p.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] GetTopProcessesByMemory: failed to dispose process"); } }
        }

        var result = new List<ProcessMemInfo>(heap.Count);
        while (heap.Count > 0)
        {
            result.Add(heap.Dequeue());
        }

        result.Sort((a, b) => b.WorkingSetMb.CompareTo(a.WorkingSetMb));
        return result;
    }

    public struct GpuInfo
    {
        public string Name;
        public long AdapterRamMb;
        public long SharedUsedMb;
        public bool IsIntegrated;
    }

    public static GpuInfo GetGpuInfo()
    {
        var result = new GpuInfo { Name = "GPU" };
        try
        {
            using var ctrl = new System.Management.ManagementObjectSearcher(
                "SELECT Caption, AdapterRAM FROM Win32_VideoController");
            foreach (System.Management.ManagementObject o in ctrl.Get())
            {
                result.Name = o["Caption"]?.ToString() ?? "GPU";
                ulong ram = 0UL;
                try { ram = Convert.ToUInt64(o["AdapterRAM"] ?? 0UL); } catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] GetGpuInfo: failed to read AdapterRAM"); }
                result.AdapterRamMb = (long)(ram / (1024 * 1024));
                result.IsIntegrated = result.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                                   || result.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                                   || result.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                                   || result.Name.Contains("Vega", StringComparison.OrdinalIgnoreCase);
                break;
            }

            try
            {
                using var memSrc = new System.Management.ManagementObjectSearcher(
                    "root\\cimv2",
                    "SELECT SharedUsage FROM Win32_PerfRawData_GPUPerformanceCounters_GPUAdapterMemory");
                foreach (System.Management.ManagementObject o in memSrc.Get())
                {
                    result.SharedUsedMb = Convert.ToInt64(o["SharedUsage"] ?? 0L) / (1024 * 1024);
                    break;
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] GetGpuInfo: failed to read SharedUsage"); }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] GetGpuInfo failed"); }
        return result;
    }

    public enum CleanProfile
    {
        Quick,
        Smart,
        Normal,
        Deep,
    }

    public static async Task<CleanResult> SmartCleanAsync(
        bool protectForeground = true,
        bool purgeStandbyOnly = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[MemoryCleanerService.SmartCleanAsync] Entry — protectFg={protectForeground}, standbyOnly={purgeStandbyOnly}");

        RamInfo before = GetRamInfo();
        var result = new CleanResult { Before = before };

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging Standby list (fast)...");
        result.StandbyFastFreed = await Task.Run(FlushStandbyListFast, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging Standby list...");
        result.StandbyFreed = await Task.Run(FlushStandbyList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Purging low-priority Standby...");
        result.LowPriorityStandbyFreed = await Task.Run(FlushLowPriorityStandbyList, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Clearing system file cache...");
        result.SystemFileCacheFreed = await Task.Run(ClearSystemFileCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report("Flushing DNS cache...");
        result.DnsCacheFreed = await Task.Run(FlushDnsCache, ct);

        ct.ThrowIfCancellationRequested();
        progress?.Report(".NET Garbage Collect...");
        result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);

        if (!purgeStandbyOnly)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Trimming background working sets...");
            result.WorkingSetsFreed = await Task.Run(() => EmptyWorkingSets(protectForeground: protectForeground), ct);
        }

        await Task.Delay(250, ct);
        result.After = GetRamInfo();

        long sumIndividual = result.WorkingSetsFreed + result.StandbyFreed
            + result.LowPriorityStandbyFreed + result.StandbyFastFreed
            + result.SystemFileCacheFreed + result.DnsCacheFreed + result.GcCollectFreed;
        long globalDelta = Math.Max(0, result.After.AvailPhysBytes - before.AvailPhysBytes);
        result.TotalFreed = Math.Max(sumIndividual, globalDelta);

        progress?.Report("Smart Clean completed.");
        return result;
    }

    public static string[] GetGpuShaderCacheDirectories()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(localAppData, "D3DSCache"),
            Path.Combine(localAppData, "NVIDIA", "DXCache"),
            Path.Combine(localAppData, "NVIDIA", "GLCache"),
            Path.Combine(localAppData, "AMD", "DxCache"),
            Path.Combine(localAppData, "AMD", "DxcCache"),
            Path.Combine(localAppData, "Intel", "ShaderCache")
        ];
    }

    public static long ClearDirectXShaderCaches()
    {
        long freed = 0;
        try
        {
            string[] shaderDirs = GetGpuShaderCacheDirectories();

            foreach (string dir in shaderDirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            fi.Delete();
                            freed += fi.Length;
                        }
                        catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.ClearDirectXShaderCaches.DeleteFile"); }
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.ClearDirectXShaderCaches.EnumerateFiles"); }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "[MemoryCleaner] ClearDirectXShaderCaches");
        }
        return freed;
    }

    public static void RestartGraphicsDriverPipeline()
    {
        try
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_B, 0, 0, UIntPtr.Zero);

            keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "[MemoryCleaner] RestartGraphicsDriverPipeline failed");
        }
    }

    public static Task<CleanResult> CleanByProfileAsync(
        CleanProfile profile,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return CleanByProfileAsync(profile, protectForeground: true, progress, ct);
    }

    public static async Task<CleanResult> CleanByProfileAsync(
        CleanProfile profile,
        bool protectForeground,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[MemoryCleanerService.CleanByProfileAsync] Entry — profile={profile}, protectFg={protectForeground}");
        if (profile == CleanProfile.Deep)
        {
            return await FullCleanAsync(protectForeground, progress, ct);
        }

        if (profile == CleanProfile.Smart)
        {
            return await SmartCleanAsync(protectForeground: protectForeground, purgeStandbyOnly: false, progress, ct);
        }

        RamInfo before = GetRamInfo();
        var result = new CleanResult { Before = before };

        if (profile == CleanProfile.Quick)
        {
            progress?.Report("Emptying Working Sets...");
            result.WorkingSetsFreed = await Task.Run(() => EmptyWorkingSets(protectForeground), ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Purging Standby list (fast)...");
            result.StandbyFastFreed = await Task.Run(FlushStandbyListFast, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Purging Standby list...");
            result.StandbyFreed = await Task.Run(FlushStandbyList, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Flushing DNS cache...");
            result.DnsCacheFreed = await Task.Run(FlushDnsCache, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report(".NET Garbage Collect...");
            result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);
        }
        else
        {
            progress?.Report("Emptying Working Sets...");
            result.WorkingSetsFreed = await Task.Run(() => EmptyWorkingSets(protectForeground), ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Flushing modified file cache...");
            result.ModifiedFileCacheFreed = await Task.Run(FlushModifiedFileCache, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Clearing system file cache...");
            result.SystemFileCacheFreed = await Task.Run(ClearSystemFileCache, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Flushing registry cache...");
            result.RegistryCacheFreed = await Task.Run(FlushRegistryCache, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Purging Standby list...");
            result.StandbyFreed = await Task.Run(FlushStandbyList, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Purging low-priority Standby...");
            result.LowPriorityStandbyFreed = await Task.Run(FlushLowPriorityStandbyList, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Purging combined page list...");
            result.CombinedPageListFreed = await Task.Run(FlushCombinedPageList, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Flushing modified page list...");
            result.ModifiedPageListFreed = await Task.Run(FlushModifiedPageList, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Flushing DNS cache...");
            result.DnsCacheFreed = await Task.Run(FlushDnsCache, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report(".NET Garbage Collect...");
            result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Compacting memory heaps...");
            result.HeapCompactFreed = await Task.Run(CompactAllHeaps, ct);

            ct.ThrowIfCancellationRequested();
            progress?.Report("Trimming all sessions...");
            result.AllSessionsFreed = await Task.Run(TrimAllSessionsWorkingSets, ct);
        }

        await Task.Delay(800, ct);
        result.After = GetRamInfo();

        long sumIndividual = result.WorkingSetsFreed + result.StandbyFreed
            + result.LowPriorityStandbyFreed + result.ModifiedPageListFreed
            + result.CombinedPageListFreed + result.ModifiedFileCacheFreed
            + result.SystemFileCacheFreed + result.RegistryCacheFreed
            + result.DnsCacheFreed + result.GcCollectFreed
            + result.StandbyFastFreed + result.HeapCompactFreed
            + result.AllSessionsFreed;
        long globalDelta = Math.Max(0, result.After.AvailPhysBytes - before.AvailPhysBytes);
        result.TotalFreed = Math.Max(sumIndividual, globalDelta);

        progress?.Report("Done.");
        return result;
    }

    public static long FlushStandbyListFast()
    {
        RamInfo before = GetRamInfo();
        _ = NtMemoryCommandRaw(MemoryPurgeStandbyListFast);
        Thread.Sleep(150);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long TrimKernelWorkingSet()
    {
        RamInfo before = GetRamInfo();
        _ = AcquirePrivilege("SeDebugPrivilege");
        _ = AcquirePrivilege("SeIncreaseQuotaPrivilege");

        foreach (int pid in new[] { 4, 8 })
        {
            try
            {
                nint h = OpenProcess(PROCESS_ALL_TRIM, false, pid);
                if (h != IntPtr.Zero)
                {
                    _ = SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                    _ = CloseHandle(h);
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.TrimKernelWorkingSet.TrimPid"); }
        }

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (_kernelProcessNames.Contains(p.ProcessName))
                {
                    nint h = OpenProcess(PROCESS_ALL_TRIM, false, p.Id);
                    if (h != IntPtr.Zero)
                    {
                        _ = SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                        _ = CloseHandle(h);
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.TrimKernelWorkingSet.TrimKernelProcess"); }
            finally { try { p.Dispose(); } catch (Exception ex2) { LoggingService.LogException(ex2, "MemoryCleanerService.TrimKernelWorkingSet.DisposeProcess"); } }
        }

        Thread.Sleep(150);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static async Task<long> FlushSuperFetchAsync(CancellationToken ct = default)
    {
        RamInfo before = GetRamInfo();
        try
        {
            await Task.Run(() =>
            {
                using var sc = new System.ServiceProcess.ServiceController("SysMain");
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(
                        System.ServiceProcess.ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(10));
                    ct.ThrowIfCancellationRequested();
                    sc.Start();
                    sc.WaitForStatus(
                        System.ServiceProcess.ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(10));
                }
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] FlushSuperFetchAsync failed"); }
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long CompactAllHeaps()
    {
        RamInfo before = GetRamInfo();
        try
        {
            _ = HeapCompact(GetProcessHeap(), 0);
            CollectAndCompactManagedHeap();
            TrimSelfWorkingSet();
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] CompactAllHeaps failed"); }
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long ClearClipboard()
    {
        RamInfo before = GetRamInfo();
        try
        {
            var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try { _ = EmptyClipboard(); }
                        finally { _ = CloseClipboard(); }
                    }
                }
                catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.ClearClipboard.ClipboardThread"); }
                finally { done.Set(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            _ = done.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] ClearClipboard failed"); }
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long FlushArpCache()
    {
        RamInfo before = GetRamInfo();
        try { _ = FlushIpNetTable(0); }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] FlushArpCache failed"); }
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long FlushNetBiosCache()
    {
        RamInfo before = GetRamInfo();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("nbtstat", "-R")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            _ = (p?.WaitForExit(3000));
        }
        catch (Exception ex) { LoggingService.LogException(ex, "[MemoryCleaner] FlushNetBiosCache failed"); }
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }

    public static long TrimAllSessionsWorkingSets()
    {
        RamInfo before = GetRamInfo();
        _ = AcquirePrivilege("SeDebugPrivilege");
        _ = AcquirePrivilege("SeIncreaseQuotaPrivilege");

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId > 0)
                {
                    nint h = OpenProcess(PROCESS_ALL_TRIM, false, p.Id);
                    if (h != IntPtr.Zero)
                    {
                        _ = SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                        _ = CloseHandle(h);
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "MemoryCleanerService.TrimAllSessionsWorkingSets.TrimProcess"); }
            finally { try { p.Dispose(); } catch (Exception ex2) { LoggingService.LogException(ex2, "MemoryCleanerService.TrimAllSessionsWorkingSets.DisposeProcess"); } }
        }

        Thread.Sleep(200);
        RamInfo after = GetRamInfo();
        return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
    }
}
