namespace RAMRazor.Core;

public enum KillOutcome
{
    Closed,
    AlreadyGone,
    Failed
}

public sealed class KillReport
{
    public string Target { get; set; } = "";
    public KillOutcome Outcome { get; set; }
    public string Detail { get; set; } = "";
    public List<int> Pids { get; set; } = new();
    public List<int> SurvivedPids { get; set; } = new();
}

public sealed class ProcInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public string Title { get; set; } = "";
    public bool HasWindow { get; set; }
    public long MemBytes { get; set; }
    public double CpuPercent { get; set; }
    public bool IsSystem { get; set; }
    public bool IsSelf { get; set; }
    public int SessionId { get; set; }
    public DateTime? Started { get; set; }
}

/// <summary>
/// A logical "app" — every process sharing the same executable (or process name
/// when the path is unavailable). The Apps view operates on groups; the Tasks
/// view operates on individual ProcInfo entries.
/// </summary>
public sealed class AppGroup
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool HasWindow { get; set; }
    public bool IsSystem { get; set; }
    public long MemBytes { get; set; }
    public double CpuPercent { get; set; }
    public List<ProcInfo> Processes { get; set; } = new();

    public string PidText =>
        Processes.Count == 1 ? Processes[0].Id.ToString() : $"{Processes.Count} procs";

    public IEnumerable<int> Pids => Processes.Select(p => p.Id);
}

public sealed class CleanResult
{
    public long FreedBytes { get; set; }
    public ulong TotalBytes { get; set; }
    public double BeforePercent { get; set; }
    public double AfterPercent { get; set; }
    public bool DeepClean { get; set; }
    public bool StandbyPurged { get; set; }
    public string Note { get; set; } = "";

    public double FreedPercentOfTotal =>
        TotalBytes <= 0 ? 0 : Math.Round(FreedBytes * 100.0 / TotalBytes, 1);
}

public sealed class AlertItem
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Source { get; set; } = "miner"; // miner | reopen | unkillable
}

public static class FormatUtil
{
    public static string Bytes(long bytes)
    {
        double b = bytes;
        if (Math.Abs(b) >= 1024 * 1024 * 1024) return $"{b / (1024 * 1024 * 1024):0.00} GB";
        if (Math.Abs(b) >= 1024 * 1024) return $"{b / (1024 * 1024):0.0} MB";
        if (Math.Abs(b) >= 1024) return $"{b / 1024:0} KB";
        return $"{b} B";
    }
}
