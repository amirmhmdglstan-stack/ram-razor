using System.Runtime.InteropServices;

namespace RAMRazor.Core;

/// <summary>
/// Physical RAM statistics and memory cleaning.
///
/// Cleaning strategy (technique popularized by open-source tools such as
/// Mem Reduct and WinMemoryCleaner — see README credits):
///   1. NtSetSystemInformation(SystemMemoryListInformation / MemoryEmptyWorkingSets)
///      — asks the kernel to trim working sets of all processes (SeIncreaseQuotaPrivilege).
///   2. MemoryPurgeStandbyList — frees the standby list (SeProfileSingleProcessPrivilege).
///   3. Per-process EmptyWorkingSet fallback + managed GC for our own process.
/// </summary>
public static class MemoryService
{
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private const int SystemMemoryListInformation = 80;
    private const int MemoryEmptyWorkingSets = 3;      // requires SeIncreaseQuotaPrivilege
    private const int MemoryPurgeStandbyList = 5;      // requires SeProfileSingleProcessPrivilege

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int InfoClass, ref int Info, int Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    private const int TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const int TOKEN_QUERY = 0x8;
    private const int SE_PRIVILEGE_ENABLED = 0x2;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, int BufferLength,
        IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public sealed class RamInfo
    {
        public uint LoadPercent { get; init; }
        public ulong Total { get; init; }
        public ulong Available { get; init; }
        public ulong Used => Total - Available;
        public double UsedPercent => Total == 0 ? 0 : Math.Round(Used * 100.0 / Total, 1);
    }

    public static RamInfo Read()
    {
        var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref st))
            return new RamInfo { LoadPercent = 0, Total = 0, Available = 0 };
        return new RamInfo
        {
            LoadPercent = st.dwMemoryLoad,
            Total = st.ullTotalPhys,
            Available = st.ullAvailPhys
        };
    }

    public static bool EnablePrivilege(string name)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var hToken))
                return false;
            try
            {
                if (!LookupPrivilegeValue(null, name, out var luid))
                    return false;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };
                AdjustTokenPrivileges(hToken, false, ref tp,
                    (int)Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero);
                return Marshal.GetLastWin32Error() == 0; // 1300 = ERROR_NOT_ALL_ASSIGNED -> false
            }
            finally { CloseHandle(hToken); }
        }
        catch { return false; }
    }

    private static int SendMemoryListCommand(int command)
    {
        try
        {
            int cmd = command;
            int size = sizeof(int);
            int status = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, size);
            return status; // 0 = STATUS_SUCCESS
        }
        catch (DllNotFoundException) { return -1; }
        catch (EntryPointNotFoundException) { return -1; }
        catch { return -1; }
    }

    /// <summary>Runs the RAM clean and reports how much was freed.</summary>
    public static CleanResult Clean(bool deep)
    {
        var result = new CleanResult { DeepClean = deep };

        var before = Read();
        result.TotalBytes = before.Total;
        result.BeforePercent = before.UsedPercent;

        if (before.Total == 0)
        {
            result.Note = "could not query memory status";
            return result;
        }

        // 1) Standby list purge (deep clean)
        if (deep)
        {
            bool ok = EnablePrivilege("SeProfileSingleProcessPrivilege");
            int status = SendMemoryListCommand(MemoryPurgeStandbyList);
            result.StandbyPurged = ok && status == 0;
            if (ok && status != 0)
                result.Note = $"standby purge status 0x{status:X8}; ";
        }

        // 2) System-wide working set emptying
        bool quotaOk = EnablePrivilege("SeIncreaseQuotaPrivilege");
        int wsStatus = SendMemoryListCommand(MemoryEmptyWorkingSets);

        // 3) Per-process fallback (EmptyWorkingSet equivalent through Kill? no — uses SetProcessWorkingSetSize)
        TrimWorkingSetsFallback();

        // 4) Managed GC for our own process
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        Thread.Sleep(900);

        var after = Read();
        result.AfterPercent = after.UsedPercent;
        long freed = (long)after.Available - (long)before.Available;
        result.FreedBytes = Math.Max(0, freed);

        if (string.IsNullOrEmpty(result.Note))
            result.Note = wsStatus == 0 || quotaOk ? "ok" : $"working-set command status 0x{wsStatus:X8}";
        return result;
    }

    private static void TrimWorkingSetsFallback()
    {
        // Soft-trim every accessible process through SetProcessWorkingSetSize(-1,-1).
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                _ = SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { /* protected process — skip */ }
            finally { try { p.Dispose(); } catch { } }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
