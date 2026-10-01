using RAMRazor.Core;

namespace RAMRazor.UI;

/// <summary>One selectable row in the process lists (shared by MainForm and dialogs).</summary>
internal sealed class SelectRow
{
    public string CheckKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string PidText { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Mem { get; set; } = "";
    public string Type { get; set; } = "";
    public string Window { get; set; } = "";
    public string Path { get; set; } = "";
    public double MemBytes { get; set; }
    public AppGroup? Group { get; set; }
    public ProcInfo? Proc { get; set; }
    public bool IsSystem { get; set; }
}
