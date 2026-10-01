using System.Runtime.InteropServices;

namespace RAMRazor.Core;

/// <summary>
/// Heuristic background cryptocurrency-miner detection:
///   * known miner executable names,
///   * sustained high CPU while running invisibly (no window),
///   * live TCP connections to known mining-pool (Stratum) ports.
/// </summary>
public static class MinerDetector
{
    private static readonly string[] MinerNames =
    {
        "xmrig", "xmr-stak", "cpuminer", "minerd", "ethminer", "nbminer",
        "phoenixminer", "trex", "t-rex", "gminer", "lolminer", "teamredminer",
        "bminer", "ccminer", "excavator", "claymore", "stratum", "nicehash",
        "minergate", "unmineable", "cudominer", "srbminer", "wildrig",
        "monerod", "cryptonight", "rtmminer", "cpuminer-opt", "miner"
    };

    // Common Stratum / mining-pool ports (kept specific to avoid false positives)
    private static readonly HashSet<int> StratumPorts = new()
    { 3333, 4444, 5555, 7777, 14444, 14433, 45560, 45700 };

    public static bool NameLooksLikeMiner(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var m in MinerNames)
            if (name.Contains(m, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Remote TCP ports currently open per PID (IPv4).</summary>
    public static Dictionary<int, List<int>> GetRemotePortsByPid()
    {
        var map = new Dictionary<int, List<int>>();
        if (!OperatingSystem.IsWindows()) return map;

        const int AF_INET = 2;
        const int TCP_TABLE_OWNER_PID_ALL = 5;

        int size = 0;
        IntPtr buf = IntPtr.Zero;
        try
        {
            // query needed size
            _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return map;

            buf = Marshal.AllocHGlobal(size);
            uint ret = GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0) return map;

            int entries = Marshal.ReadInt32(buf, 0);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>(); // 24 bytes
            for (int i = 0; i < entries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(buf, 4 + i * rowSize));
                int pid = (int)row.dwOwningPid;
                if (pid <= 0) continue;
                int port = ((int)row.dwRemotePort >> 8) | (((int)row.dwRemotePort & 0xFF) << 8);
                if (port == 0) continue;
                if (!map.TryGetValue(pid, out var list))
                    map[pid] = list = new List<int>();
                list.Add(port);
            }
        }
        catch { /* best-effort */ }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
        return map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int TableClass, int Reserved);

    /// <summary>
    /// Scores one app group; returns a finding when the miner-suspicion
    /// score reaches the threshold, otherwise null.
    /// </summary>
    public static MinerFinding? Evaluate(AppGroup group, Dictionary<int, List<int>> portsByPid)
    {
        if (group == null || group.IsSystem) return null;

        double score = 0;
        var reasons = new List<string>();

        string file = "";
        try { file = Path.GetFileNameWithoutExtension(group.ExePath); } catch { }
        string lookup = group.DisplayName + " " + file;

        if (NameLooksLikeMiner(lookup))
        {
            score += 100;
            reasons.Add("known miner process name");
        }

        if (group.CpuPercent >= 60 && !group.HasWindow)
        {
            score += 45;
            reasons.Add($"sustained {group.CpuPercent:0}% CPU while running invisibly (no window)");
        }
        else if (group.CpuPercent >= 85)
        {
            score += 25;
            reasons.Add($"very high CPU usage ({group.CpuPercent:0}%)");
        }

        var poolPorts = new List<int>();
        foreach (var pid in group.Pids)
            if (portsByPid.TryGetValue(pid, out var ports))
                foreach (var port in ports)
                    if (StratumPorts.Contains(port) && !poolPorts.Contains(port))
                        poolPorts.Add(port);

        if (poolPorts.Count > 0)
        {
            score += 45;
            reasons.Add("connected to mining-pool port(s): " + string.Join(", ", poolPorts));
        }

        if (score < 70 || reasons.Count == 0) return null;

        return new MinerFinding
        {
            GroupKey = group.Key,
            DisplayName = group.DisplayName,
            ExePath = group.ExePath,
            CpuPercent = group.CpuPercent,
            Ports = poolPorts,
            Reasons = reasons,
            Score = score
        };
    }
}

public sealed class MinerFinding
{
    public string GroupKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExePath { get; set; } = "";
    public double CpuPercent { get; set; }
    public List<int> Ports { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
    public double Score { get; set; }

    public string ReasonText => string.Join("; ", Reasons);
}
