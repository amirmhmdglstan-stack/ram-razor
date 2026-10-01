using System.Diagnostics;

namespace RAMRazor.Core;

/// <summary>
/// Snapshots the running process table, tracks per-process CPU deltas,
/// and builds AppGroups (all processes of one executable = one app).
/// </summary>
public sealed class ProcessEnumerator
{
    private readonly Dictionary<int, (TimeSpan Total, DateTime At)> _lastCpu = new();

    public List<ProcInfo> Snapshot()
    {
        var list = new List<ProcInfo>(256);
        int selfPid = Environment.ProcessId;
        int cores = Math.Max(1, Environment.ProcessorCount);

        foreach (var p in Process.GetProcesses())
        {
            ProcInfo? pi = null;
            try
            {
                pi = new ProcInfo
                {
                    Id = p.Id,
                    Name = SafeName(p),
                    IsSelf = p.Id == selfPid,
                    SessionId = SafeSession(p),
                    MemBytes = SafeWorkingSet(p),
                    HasWindow = SafeHasWindow(p),
                    Title = SafeTitle(p)
                };

                try { pi.ExePath = p.MainModule?.FileName ?? ""; }
                catch { pi.ExePath = ""; }

                pi.GroupKey = GroupKeyFor(pi);

                try { pi.Started = p.StartTime; } catch { }

                try
                {
                    var now = DateTime.UtcNow;
                    var total = p.TotalProcessorTime;
                    if (_lastCpu.TryGetValue(p.Id, out var prev) &&
                        (now - prev.At).TotalMilliseconds > 250)
                    {
                        pi.CpuPercent = Math.Clamp(
                            (total - prev.Total).TotalMilliseconds /
                            ((now - prev.At).TotalMilliseconds * cores) * 100.0,
                            0, 128);
                    }
                    _lastCpu[p.Id] = (total, now);
                }
                catch { }

                pi.IsSystem = SystemWhitelist.IsSystem(pi);
                list.Add(pi);
            }
            catch
            {
                // process died mid-snapshot — skip
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        if (_lastCpu.Count > 4096) _lastCpu.Clear();
        return list;
    }

    public static string GroupKeyFor(ProcInfo pi)
    {
        if (!string.IsNullOrEmpty(pi.ExePath))
            return pi.ExePath.ToLowerInvariant();
        return "name:" + pi.Name.ToLowerInvariant();
    }

    /// <summary>Groups the snapshot into apps (all processes of one executable).</summary>
    public List<AppGroup> BuildGroups(List<ProcInfo> snapshot)
    {
        return snapshot
            .GroupBy(p => p.GroupKey)
            .Select(g =>
            {
                var main = g.OrderByDescending(x => x.HasWindow)
                            .ThenByDescending(x => x.MemBytes)
                            .First();
                return new AppGroup
                {
                    Key = g.Key,
                    DisplayName = FriendlyName(main),
                    ExePath = main.ExePath,
                    HasWindow = g.Any(x => x.HasWindow),
                    IsSystem = g.All(x => x.IsSystem),
                    MemBytes = g.Sum(x => x.MemBytes),
                    CpuPercent = Math.Round(g.Sum(x => x.CpuPercent), 1),
                    Processes = g.OrderBy(x => x.Id).ToList()
                };
            })
            .ToList();
    }

    public static string FriendlyName(ProcInfo p)
    {
        if (p.HasWindow && !string.IsNullOrWhiteSpace(p.Title))
        {
            var t = p.Title.Trim();
            if (t.Length > 64) t = t[..64] + "…";
            return t;
        }
        try
        {
            if (!string.IsNullOrEmpty(p.ExePath))
                return Path.GetFileNameWithoutExtension(p.ExePath);
        }
        catch { }
        return p.Name;
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName ?? ""; } catch { return ""; }
    }
    private static int SafeSession(Process p)
    {
        try { return p.SessionId; } catch { return -1; }
    }
    private static long SafeWorkingSet(Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }
    private static bool SafeHasWindow(Process p)
    {
        try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; }
    }
    private static string SafeTitle(Process p)
    {
        try { return p.MainWindowTitle ?? ""; } catch { return ""; }
    }
}
