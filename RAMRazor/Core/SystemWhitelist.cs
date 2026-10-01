namespace RAMRazor.Core;

/// <summary>
/// Protects Windows system components from being closed.
/// A process is treated as a system task when its name is on the protected list,
/// when it runs in session 0 (services kernel), or when its image lives under C:\Windows.
/// </summary>
public static class SystemWhitelist
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / core
        "system", "idle", "secure system", "registry", "memory compression",
        "csrss", "wininit", "winlogon", "smss", "lsass", "services", "svchost",

        // Infrastructure hosts
        "wmiprvse", "wudfhost", "sppsvc", "spoolsv", "taskhostw", "dllhost",
        "sihost", "ctfmon", "runtimebroker", "fontdrvhost", "conhost", "openconsole",

        // Shell & UI
        "explorer", "dwm", "searchhost", "searchapp", "startmenuexperiencehost",
        "shellexperiencehost", "textinputhost", "widgetservice", "widgets",

        // Security
        "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray",
        "smartscreen", "sense", "mssense",

        // Audio / graphics driver service containers
        "audiodg", "audioconfigtask", "nvcontainer", "nvdisplay.container",
        "nvtmru", "atiesrxx", "atieclxx", "amdwddmg", "intelmetadata",

        // Ourselves
        "ramrazor"
    };

    public static bool IsProtectedName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        return ProtectedNames.Contains(processName.Trim());
    }

    public static bool IsSystem(ProcInfo p)
    {
        if (p == null) return true;
        if (p.IsSelf) return true;
        if (p.Id <= 4) return true;                       // System Idle/Kernel
        if (p.SessionId == 0) return true;                // services session
        if (ProtectedNames.Contains(p.Name)) return true;

        if (!string.IsNullOrEmpty(p.ExePath))
        {
            if (p.ExePath.StartsWith(@"C:\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
                p.ExePath.StartsWith(@"C:\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase) ||
                p.ExePath.Equals(@"C:\Windows\explorer.exe", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
