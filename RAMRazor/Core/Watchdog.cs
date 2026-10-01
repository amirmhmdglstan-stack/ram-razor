using System.Diagnostics;

namespace RAMRazor.Core;

/// <summary>
/// Watches after mass-close operations: logs any app that re-opens itself,
/// optionally re-kills it (up to 2 times), and permanently kills anything
/// present on the blacklist.
/// </summary>
public sealed class Watchdog
{
    private sealed class WatchEntry
    {
        public string Key = "";
        public string Display = "";
        public int Rekills;
        public DateTime Until;
        public bool Reported;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, WatchEntry> _watching = new();
    private readonly BlacklistStore _blacklist;
    private readonly ProcessEnumerator _enumerator;

    /// <summary>Updated by the UI each tick; kills re-opened apps again when true.</summary>
    public bool AutoRekill { get; set; }

    public event Action<string>? Reopened;          // display name
    public event Action<string>? BlacklistedKilled; // process name

    public Watchdog(BlacklistStore blacklist, ProcessEnumerator enumerator)
    {
        _blacklist = blacklist;
        _enumerator = enumerator;
    }

    /// <summary>Arms resurrection tracking for the given groups (usually right after Close All).</summary>
    public void ArmWatch(IEnumerable<AppGroup> closedGroups, TimeSpan duration)
    {
        lock (_lock)
        {
            foreach (var g in closedGroups)
            {
                _watching[g.Key] = new WatchEntry
                {
                    Key = g.Key,
                    Display = g.DisplayName,
                    Until = DateTime.UtcNow.Add(duration)
                };
            }
        }
    }

    /// <summary>Periodic tick (call every ~4 s from the UI timer).</summary>
    public void Tick()
    {
        KillBlacklistedOnSight();
        CheckResurrections();
    }

    private void KillBlacklistedOnSight()
    {
        try
        {
            var entries = _blacklist.Entries;
            if (entries.Count == 0) return;
            var names = entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Process.GetProcesses())
            {
                string? pname = null;
                try
                {
                    pname = p.ProcessName;
                    if (pname != null && names.Contains(pname) && p.Id != Environment.ProcessId)
                    {
                        var (outcome, detail) = ProcessKiller.KillPid(p.Id, force: true);
                        if (outcome != KillOutcome.Failed)
                        {
                            LogService.Add("INFO", "BLACKLIST",
                                $"auto-killed blacklisted '{pname}' (pid {p.Id}) — {detail}");
                            BlacklistedKilled?.Invoke(pname);
                        }
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }
    }

    private void CheckResurrections()
    {
        List<WatchEntry> due;
        lock (_lock)
        {
            due = _watching.Values.Where(w => w.Until > DateTime.UtcNow).ToList();
        }
        if (due.Count == 0) return;

        List<ProcInfo> snap;
        try { snap = _enumerator.Snapshot(); }
        catch { return; }

        foreach (var w in due)
        {
            var alive = snap.Where(x => x.GroupKey == w.Key).ToList();
            if (alive.Count == 0) continue;

            if (!w.Reported)
            {
                w.Reported = true;
                LogService.Add("WARN", "REOPEN",
                    $"'{w.Display}' re-opened itself after being closed ({alive.Count} process(es))");
                Reopened?.Invoke(w.Display);
            }

            if (AutoRekill && w.Rekills < 2)
            {
                w.Rekills++;
                foreach (var proc in alive)
                {
                    var (outcome, detail) = ProcessKiller.KillPid(proc.Id, force: true);
                    LogService.Add(outcome == KillOutcome.Failed ? "ERR" : "INFO", "REOPEN",
                        $"re-kill of '{w.Display}' pid {proc.Id}: {outcome} — {detail}");
                }
            }
        }

        lock (_lock)
        {
            foreach (var expired in _watching.Where(kv => kv.Value.Until <= DateTime.UtcNow).ToList())
                _watching.Remove(expired.Key);
        }
    }
}
