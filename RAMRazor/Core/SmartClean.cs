using System.Diagnostics;

namespace RAMRazor.Core;

public enum SmartCleanScope { Standard, Nuclear }

/// <summary>
/// The v2 "Smart Clean" engine.
///
/// Standard scope: closes every process that is NOT a Windows system component
/// (full SystemWhitelist protection) — safe for the desktop.
///
/// Nuclear scope: closes EVERY process except the BSOD-critical keep-alive set
/// (the absolute minimum Windows needs to not crash) plus anything the user put
/// on the keep-list. The shell (explorer) is closed too and automatically
/// restarted afterwards. Session-0 service hosts are only touched when
/// IncludeServices is enabled — killing services does not crash Windows but can
/// degrade it until reboot, so it is off by default.
/// </summary>
public static class SmartClean
{
    /// <summary>The absolute minimum Windows cannot survive without (BSOD-level).</summary>
    public static readonly HashSet<string> CriticalKeepAlive = new(StringComparer.OrdinalIgnoreCase)
    {
        // kernel + session manager + csrss pair + logon chain + service host + lsass
        "system", "idle", "secure system", "registry", "memory compression",
        "smss", "csrss", "wininit", "winlogon", "services", "svchost", "lsass",
        // display compositor + font service (killing dwm blacks the screen)
        "dwm", "fontdrvhost",
        // ourselves
        "ramrazor"
    };

    public static bool IsCriticalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return CriticalKeepAlive.Contains(name.Trim());
    }

    /// <summary>
    /// Pure planner — returns exactly which processes a smart clean would close.
    /// Runs on any OS so it is fully unit-testable.
    /// </summary>
    public static List<ProcInfo> PlanTargets(IEnumerable<ProcInfo> snapshot, SmartCleanScope scope,
        IEnumerable<string>? keepList = null, bool includeServices = false)
    {
        var keep = new HashSet<string>(
            (keepList ?? Enumerable.Empty<string>()).Select(BlacklistStore.Normalize),
            StringComparer.OrdinalIgnoreCase);

        var targets = new List<ProcInfo>();
        foreach (var p in snapshot)
        {
            if (p == null || p.IsSelf || p.Id <= 4) continue;
            if (BlacklistStore.Normalize(p.Name) == "ramrazor") continue;
            if (keep.Contains(BlacklistStore.Normalize(p.Name))) continue;

            if (scope == SmartCleanScope.Standard)
            {
                if (SystemWhitelist.IsSystem(p)) continue;
                targets.Add(p);
            }
            else // Nuclear — only the BSOD-critical set survives
            {
                if (IsCriticalName(p.Name)) continue;
                if (p.SessionId == 0 && !includeServices) continue;
                targets.Add(p);
            }
        }
        return targets;
    }

    public sealed class SmartReport
    {
        public int Targets { get; set; }
        public int Closed { get; set; }
        public List<string> Failed { get; set; } = new();
        public bool ExplorerRestarted { get; set; }
        public CleanResult? Memory { get; set; }
        public long FreedTotal => Memory?.FreedBytes ?? 0;
        public double BeforePercent => Memory?.BeforePercent ?? 0;
        public double AfterPercent => Memory?.AfterPercent ?? 0;
    }

    /// <summary>
    /// Executes a smart clean: kills planned targets with the fast killer,
    /// restarts explorer if the nuclear scope killed it, then runs the memory
    /// purge. Everything is logged.
    /// </summary>
    public static SmartReport Execute(ProcessEnumerator enumerator, SmartCleanScope scope,
        IEnumerable<string>? keepList, bool includeServices, bool restartExplorer,
        CleanMode purgeLevel, Action<string>? progress = null)
    {
        var snap = enumerator.Snapshot();
        var targets = PlanTargets(snap, scope, keepList, includeServices);
        var report = new SmartReport { Targets = targets.Count };

        LogService.Add("INFO", "SMARTCLEAN",
            $"starting {scope} clean — {targets.Count} target process(es), purge level {purgeLevel}");

        foreach (var t in targets)
        {
            progress?.Invoke(t.Name);
            var (outcome, detail) = ProcessKiller.KillPidFast(t.Id);
            if (outcome == KillOutcome.Failed)
            {
                report.Failed.Add($"{t.Name} (pid {t.Id}) — {detail}");
                LogService.Add("ERR", "SMARTCLEAN", $"FAILED to close '{t.Name}' (pid {t.Id}) — {detail}");
            }
            else
            {
                report.Closed++;
                LogService.Add("INFO", "SMARTCLEAN", $"closed '{t.Name}' (pid {t.Id}) — {detail}");
            }
        }

        // bring the shell back if nuclear mode removed it
        if (scope == SmartCleanScope.Nuclear && restartExplorer &&
            targets.Any(t => t.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase)))
        {
            Thread.Sleep(1000);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                report.ExplorerRestarted = true;
                LogService.Add("INFO", "SMARTCLEAN", "explorer.exe restarted");
            }
            catch (Exception ex)
            {
                LogService.Add("ERR", "SMARTCLEAN", "explorer restart failed: " + ex.Message);
            }
        }

        report.Memory = MemoryService.Clean(purgeLevel);

        LogService.Add("INFO", "SMARTCLEAN",
            $"done: {report.Closed}/{report.Targets} closed, {report.Failed.Count} failed, " +
            $"memory freed {FormatUtil.Bytes(report.FreedTotal)} " +
            $"({report.BeforePercent:0.0}% -> {report.AfterPercent:0.0}% used)");
        return report;
    }
}
