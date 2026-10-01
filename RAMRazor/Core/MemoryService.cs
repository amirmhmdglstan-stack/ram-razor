using System.Runtime.InteropServices;

namespace RAMRazor.Core;

public enum CleanMode { Light, Deep, Extreme }

/// <summary>
/// Physical RAM statistics and the v2 memory-cleaning engine.
///
/// v2 FIXES a critical v1 bug: the SYSTEM_MEMORY_LIST_COMMAND enum passed to
/// NtSetSystemInformation is 0-based — EmptyWorkingSets = 2 and PurgeStandbyList
/// = 4. v1 wrongly sent 3 and 5 (i.e. FlushModifiedList, which fails without
/// privileges, and PurgeLowPriorityStandbyList, which has almost no visible
/// effect) — that is why v1 cleaning appeared to "do nothing".
///
/// v2 sequence (technique matches Mem Reduct / WinMemoryCleaner, see README):
///   1. Enable SeIncreaseQuotaPrivilege + SeProfileSingleProcessPrivilege + SeDebugPrivilege
///   2. EmptyWorkingSets  (2)  — kernel trims every process working set
///   3. PurgeStandbyList  (4)  — frees the standby list   (Deep, Extreme)
///   4. FlushModifiedList (3)  — writes dirty pages out   (Extreme)
///   5. Per-process SetProcessWorkingSetSize(-1,-1) fallback
///   6. Second empty + purge pass (Deep, Extreme) so pages pushed to standby
///      in step 5 actually get freed
///   7. Own-process GC + trim
/// Every stage is reported with its NTSTATUS so the user can SEE what worked.
/// </summary>
public static class MemoryService
{
    // ---- public, test-locked kernel command constants (see LogicSelfCheck) ----
    public const int CmdCaptureAccessedBits = 0;
    public const int CmdCaptureAndResetAccessedBits = 1;
    public const int CmdEmptyWorkingSets = 2;            // SeIncreaseQuotaPrivilege
    public const int CmdFlushModifiedList = 3;           // SeProfileSingleProcessPrivilege
    public const int CmdPurgeStandbyList = 4;            // SeProfileSingleProcessPrivilege
    public const int CmdPurgeLowPriorityStandbyList = 5;

    private const int SystemMemoryListInformation = 80;

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

    /// <summary>Enables all three privileges the clean engine needs.</summary>
    public static Dictionary<string, bool> EnableAllPrivileges()
    {
        var d = new Dictionary<string, bool>();
        foreach (var p in new[]
                 {
                     "SeIncreaseQuotaPrivilege",
                     "SeProfileSingleProcessPrivilege",
                     "SeDebugPrivilege"
                 })
            d[p] = EnablePrivilege(p);
        return d;
    }

    /// <summary>Sends a SystemMemoryListInformation command; 0 = STATUS_SUCCESS. Exposed for tests.</summary>
    public static int SendMemoryCommand(int command)
    {
        try
        {
            int cmd = command;
            return NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        }
        catch (DllNotFoundException) { return -1; }
        catch (EntryPointNotFoundException) { return -1; }
        catch { return -1; }
    }

    private static string Status(int nt) => nt == 0 ? "ok" : (nt < 0 ? $"status 0x{nt:X8}" : $"status 0x{nt:X8}");

    /// <summary>Averages several samples so a momentary spike does not fake results.</summary>
    private static RamInfo Sample(int count, int gapMs)
    {
        ulong total = 0, avail = 0; uint load = 0;
        for (int i = 0; i < count; i++)
        {
            var r = Read();
            total = r.Total;                    // constant
            avail += r.Available;
            load = Math.Max(load, r.LoadPercent);
            if (i < count - 1) Thread.Sleep(gapMs);
        }
        return new RamInfo { LoadPercent = load, Total = total, Available = avail / (ulong)Math.Max(1, count) };
    }

    /// <summary>Runs the full v2 clean pipeline for the requested intensity.</summary>
    public static CleanResult Clean(CleanMode mode)
    {
        var result = new CleanResult { Mode = mode, DeepClean = mode != CleanMode.Light };
        var stages = new List<CleanStage>();

        var before = Sample(3, 200);
        result.TotalBytes = before.Total;
        result.BeforePercent = before.UsedPercent;
        result.BeforeBytes = before.Used;

        if (before.Total == 0)
        {
            result.Note = "could not query memory status";
            result.Stages = stages;
            return result;
        }

        // 0) privileges first — without these the kernel commands are rejected
        var priv = EnableAllPrivileges();
        result.Privileges = priv;
        stages.Add(new CleanStage
        {
            Name = "acquire privileges",
            Ok = priv.Values.Any(v => v),
            Detail = string.Join(", ", priv.Select(kv => kv.Key.Replace("Se", "").Replace("Privilege", "") + " " + (kv.Value ? "on" : "off")))
        });

        // 1) kernel-wide working-set trim — pushes private pages towards standby/modified
        int ws1 = SendMemoryCommand(CmdEmptyWorkingSets);
        stages.Add(new CleanStage { Name = "empty working sets (all processes)", Ok = ws1 == 0, Detail = Status(ws1) });

        // 2) standby purge — the step that actually turns cached pages into free RAM
        if (mode != CleanMode.Light)
        {
            int sb = SendMemoryCommand(CmdPurgeStandbyList);
            stages.Add(new CleanStage { Name = "purge standby list", Ok = sb == 0, Detail = Status(sb) });
        }

        // 3) modified-page flush — writes dirty pages to the pagefile and frees them (heavy, Extreme only)
        if (mode == CleanMode.Extreme)
        {
            int fm = SendMemoryCommand(CmdFlushModifiedList);
            stages.Add(new CleanStage { Name = "flush modified page list", Ok = fm == 0, Detail = Status(fm) });
        }

        // 4) per-process fallback trim (covers processes the kernel pass may miss)
        int trimmed = TrimWorkingSetsFallback();
        stages.Add(new CleanStage { Name = "per-process working-set trim", Ok = trimmed >= 0, Detail = $"{Math.Max(0, trimmed)} processes trimmed" });

        // 5) second pass: pages pushed out in step 4 are now in standby — purge again
        if (mode != CleanMode.Light)
        {
            int ws2 = SendMemoryCommand(CmdEmptyWorkingSets);
            int sb2 = SendMemoryCommand(CmdPurgeStandbyList);
            stages.Add(new CleanStage
            {
                Name = "second pass (empty + purge)",
                Ok = ws2 == 0 && sb2 == 0,
                Detail = Status(ws2) + " / " + Status(sb2)
            });
        }

        // 6) our own process: full GC + trim
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            _ = SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            stages.Add(new CleanStage { Name = "self GC + trim", Ok = true, Detail = "done" });
        }
        catch (Exception ex)
        {
            stages.Add(new CleanStage { Name = "self GC + trim", Ok = false, Detail = ex.Message });
        }

        Thread.Sleep(1200);
        var after = Sample(3, 200);
        result.AfterPercent = after.UsedPercent;
        result.AfterBytes = after.Used;
        long freed = (long)after.Available - (long)before.Available;
        result.FreedBytes = Math.Max(0, freed);
        result.Stages = stages;

        var failed = stages.Where(s => !s.Ok).ToList();
        result.Note = failed.Count == 0
            ? "all stages ok"
            : "stage issues: " + string.Join("; ", failed.Select(f => $"{f.Name} [{f.Detail}]"));
        return result;
    }

    /// <summary>Backwards-compatible wrapper.</summary>
    public static CleanResult Clean(bool deep) => Clean(deep ? CleanMode.Deep : CleanMode.Light);

    /// <returns>number of processes actually trimmed, or -1 on global failure</returns>
    private static int TrimWorkingSetsFallback()
    {
        int trimmed = 0, seen = 0;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) { continue; }
                seen++;
                if (SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1)))
                    trimmed++;
            }
            catch { /* protected process — skip */ }
            finally { try { p.Dispose(); } catch { } }
        }
        return seen == 0 ? -1 : trimmed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
