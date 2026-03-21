using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Coclico.Services
{
    public static class MemoryCleanerService
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

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

        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(-2147483646);
        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(-2147483647);

        private static void AcquirePrivilege(string privilege)
        {
            try
            {
                IntPtr htok = IntPtr.Zero;
                var tp = new TokPriv1Luid { Count = 1, Luid = 0, Attr = SE_PRIVILEGE_ENABLED };
                if (OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, ref htok))
                {
                    if (LookupPrivilegeValue(null, privilege, ref tp.Luid))
                    {
                        AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                    CloseHandle(htok);
                }
            }
            catch { }
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
            if (!GlobalMemoryStatusEx(ref stat)) return new RamInfo();

            long commitTotal = (long)stat.ullTotalPageFile;
            long commitAvail = (long)stat.ullAvailPageFile;
            long commitUsed = commitTotal - commitAvail;

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
            long freed = 0;
            AcquirePrivilege("SeDebugPrivilege");

            Parallel.ForEach(
                Process.GetProcesses(),
                () => 0L,
                (proc, _, localFreed) =>
                {
                    try
                    {
                        long before = proc.WorkingSet64;
                        SetProcessWorkingSetSize(proc.Handle, new IntPtr(-1), new IntPtr(-1));
                        proc.Refresh();
                        return localFreed + Math.Max(0, before - proc.WorkingSet64);
                    }
                    catch { return localFreed; }
                    finally { try { proc.Dispose(); } catch { } }
                },
                local => Interlocked.Add(ref freed, local));

            return freed;
        }

        public static long FlushStandbyList()
        {
            var before = GetRamInfo();
            return NtMemoryCommand(MemoryPurgeStandbyList, before);
        }

        public static long FlushLowPriorityStandbyList()
        {
            var before = GetRamInfo();
            return NtMemoryCommand(MemoryPurgeLowPriorityStandby, before);
        }

        public static long FlushModifiedPageList()
        {
            var before = GetRamInfo();
            return NtMemoryCommand(MemoryFlushModifiedList, before);
        }

        public static long FlushCombinedPageList()
        {
            var before = GetRamInfo();
            NtMemoryCommand(MemoryFlushModifiedList, before);
            NtMemoryCommand(MemoryPurgeStandbyList, before);
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long FlushModifiedFileCache()
        {
            var before = GetRamInfo();
            int r = NtMemoryCommandRaw(MemoryFlushModifiedListFast);
            if (r != 0) NtMemoryCommandRaw(MemoryFlushModifiedList);
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long ClearSystemFileCache()
        {
            try
            {
                var before = GetRamInfo();
                AcquirePrivilege("SeIncreaseQuotaPrivilege");
                SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0);
                var after = GetRamInfo();
                return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
            }
            catch { return 0; }
        }

        public static long FlushRegistryCache()
        {
            try
            {
                var before = GetRamInfo();
                AcquirePrivilege("SeBackupPrivilege");
                RegFlushKey(HKEY_LOCAL_MACHINE);
                RegFlushKey(HKEY_CURRENT_USER);
                var after = GetRamInfo();
                return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
            }
            catch { return 0; }
        }

        public static long FlushDnsCache()
        {
            try
            {
                var before = GetRamInfo();
                DnsFlushResolverCache();
                var after = GetRamInfo();
                return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
            }
            catch { return 0; }
        }

        public static long ForceGcCollect()
        {
            var before = GetRamInfo();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        private static long NtMemoryCommand(int command, RamInfo before)
        {
            try
            {
                NtMemoryCommandRaw(command);
                var after = GetRamInfo();
                return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
            }
            catch { return 0; }
        }

        private static int NtMemoryCommandRaw(int command)
        {
            AcquirePrivilege("SeProfileSingleProcessPrivilege");

            int size = sizeof(int);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.WriteInt32(ptr, command);
                return NtSetSystemInformation(SystemMemoryListInformation, ptr, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
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

        public static async Task<CleanResult> FullCleanAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var before = GetRamInfo();
            var result = new CleanResult { Before = before };

            progress?.Report("Emptying Working Sets...");
            result.WorkingSetsFreed = await Task.Run(EmptyWorkingSets, ct);

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

            await Task.Delay(500, ct);
            result.After = GetRamInfo();
            result.TotalFreed = Math.Max(0, result.After.AvailPhysBytes - before.AvailPhysBytes);
            progress?.Report("Cleaning complete.");
            return result;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            if (bytes < 1024L) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low; public uint High; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        private static long _cpuIdleLast, _cpuKernelLast, _cpuUserLast;
        private static long FtToLong(FILETIME f) => (long)f.High << 32 | f.Low;

        public static double GetSystemCpuPercent()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            long i  = FtToLong(idle);
            long k  = FtToLong(kernel);
            long u  = FtToLong(user);
            long di = i - _cpuIdleLast;
            long total = (k - _cpuKernelLast) + (u - _cpuUserLast);
            _cpuIdleLast   = i;
            _cpuKernelLast = k;
            _cpuUserLast   = u;
            if (total <= 0) return 0;
            return Math.Min(100.0, Math.Max(0.0, (total - di) / (double)total * 100.0));
        }

        public static void TrimSelfWorkingSet()
        {
            try
            {
                using var self = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(self.Handle, new IntPtr(-1), new IntPtr(-1));
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
            }
            catch { }
        }

        public struct ProcessMemInfo
        {
            public string Name;
            public long   WorkingSetMb;
            public long   PrivateMb;
            public int    Pid;
        }

        public static List<ProcessMemInfo> GetTopProcessesByMemory(int n = 8)
        {
            var list = new List<ProcessMemInfo>(256);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    list.Add(new ProcessMemInfo
                    {
                        Name         = p.ProcessName,
                        WorkingSetMb = p.WorkingSet64        / (1024 * 1024),
                        PrivateMb    = p.PrivateMemorySize64 / (1024 * 1024),
                        Pid          = p.Id,
                    });
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            list.Sort((a, b) => b.WorkingSetMb.CompareTo(a.WorkingSetMb));
            return list.Count > n ? list.GetRange(0, n) : list;
        }

        public struct GpuInfo
        {
            public string Name;
            public long   AdapterRamMb;
            public long   SharedUsedMb;
            public bool   IsIntegrated;
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
                    var ram = 0UL;
                    try { ram = Convert.ToUInt64(o["AdapterRAM"] ?? 0UL); } catch { }
                    result.AdapterRamMb = (long)(ram / (1024 * 1024));
                    result.IsIntegrated = result.Name.Contains("Intel",  StringComparison.OrdinalIgnoreCase)
                                       || result.Name.Contains("UHD",    StringComparison.OrdinalIgnoreCase)
                                       || result.Name.Contains("Iris",   StringComparison.OrdinalIgnoreCase)
                                       || result.Name.Contains("Vega",   StringComparison.OrdinalIgnoreCase);
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
                catch { }
            }
            catch { }
            return result;
        }

        public enum CleanProfile
        {
            Quick,
            Normal,
            Deep,
        }

        public static async Task<CleanResult> CleanByProfileAsync(
            CleanProfile profile,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (profile == CleanProfile.Deep)
                return await FullCleanAsync(progress, ct);

            var before = GetRamInfo();
            var result = new CleanResult { Before = before };

            if (profile == CleanProfile.Quick)
            {
                progress?.Report("Emptying Working Sets...");
                result.WorkingSetsFreed = await Task.Run(EmptyWorkingSets, ct);

                ct.ThrowIfCancellationRequested();
                progress?.Report(".NET Garbage Collect...");
                result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);
            }
            else
            {
                progress?.Report("Emptying Working Sets...");
                result.WorkingSetsFreed = await Task.Run(EmptyWorkingSets, ct);

                ct.ThrowIfCancellationRequested();
                progress?.Report("Flushing modified file cache...");
                result.ModifiedFileCacheFreed = await Task.Run(FlushModifiedFileCache, ct);

                ct.ThrowIfCancellationRequested();
                progress?.Report("Purging Standby list...");
                result.StandbyFreed = await Task.Run(FlushStandbyList, ct);

                ct.ThrowIfCancellationRequested();
                progress?.Report("Low-priority Standby...");
                result.LowPriorityStandbyFreed = await Task.Run(FlushLowPriorityStandbyList, ct);

                ct.ThrowIfCancellationRequested();
                progress?.Report(".NET Garbage Collect...");
                result.GcCollectFreed = await Task.Run(ForceGcCollect, ct);
            }

            await Task.Delay(400, ct);
            result.After     = GetRamInfo();
            result.TotalFreed = Math.Max(0, result.After.AvailPhysBytes - before.AvailPhysBytes);
            progress?.Report("Done.");
            return result;
        }

        public static long FlushStandbyListFast()
        {
            var before = GetRamInfo();
            NtMemoryCommandRaw(MemoryPurgeStandbyListFast);
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        private const uint PROCESS_SET_QUOTA         = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        public static long TrimKernelWorkingSet()
        {
            var before = GetRamInfo();
            AcquirePrivilege("SeDebugPrivilege");
            AcquirePrivilege("SeIncreaseQuotaPrivilege");

            foreach (var pid in new[] { 4, 8 })
            {
                try
                {
                    var h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, pid);
                    if (h != IntPtr.Zero)
                    {
                        SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                        CloseHandle(h);
                    }
                }
                catch { }
            }

            foreach (var name in new[] { "smss", "csrss", "wininit", "winlogon", "lsass", "services" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { SetProcessWorkingSetSize(p.Handle, new IntPtr(-1), new IntPtr(-1)); }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }

            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static async Task<long> FlushSuperFetchAsync(CancellationToken ct = default)
        {
            var before = GetRamInfo();
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
            catch { }
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long CompactAllHeaps()
        {
            var before = GetRamInfo();
            try
            {
                HeapCompact(GetProcessHeap(), 0);
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                TrimSelfWorkingSet();
            }
            catch { }
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long ClearClipboard()
        {
            var before = GetRamInfo();
            try
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try { EmptyClipboard(); }
                    finally { CloseClipboard(); }
                }
            }
            catch { }
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long FlushArpCache()
        {
            var before = GetRamInfo();
            try { FlushIpNetTable(0); }
            catch { }
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long FlushNetBiosCache()
        {
            var before = GetRamInfo();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("nbtstat", "-R")
                {
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }

        public static long TrimAllSessionsWorkingSets()
        {
            var before = GetRamInfo();
            AcquirePrivilege("SeDebugPrivilege");
            AcquirePrivilege("SeIncreaseQuotaPrivilege");

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId > 0)
                        SetProcessWorkingSetSize(p.Handle, new IntPtr(-1), new IntPtr(-1));
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            var after = GetRamInfo();
            return Math.Max(0, after.AvailPhysBytes - before.AvailPhysBytes);
        }
    }
}
