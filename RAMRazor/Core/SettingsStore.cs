using System.Text.Json;

namespace RAMRazor.Core;

/// <summary>
/// All user-configurable behaviour, persisted as JSON at
/// %ProgramData%\RAMRazor\settings.json.
/// </summary>
public sealed class AppSettings
{
    // ---- clean engine ----
    public string CleanLevel { get; set; } = "Deep";      // Light | Deep | Extreme (CLEAN RAM button)
    public string SmartScope { get; set; } = "Standard";  // Standard | Nuclear (SMART CLEAN button)

    // ---- nuclear scope behaviour ----
    public bool RestartExplorer { get; set; } = true;     // bring the shell back after a nuclear clean
    public bool IncludeServices { get; set; } = false;    // also kill session-0 service hosts (nuclear only)

    // ---- automation ----
    public bool AutoClean { get; set; } = false;
    public int AutoCleanMinutes { get; set; } = 10;       // interval
    public int AutoCleanThreshold { get; set; } = 0;      // 0 = always; else only when RAM used >= this %
    public bool CleanAtStartup { get; set; } = false;
    public bool StartInTray { get; set; } = false;
    public bool AutoRekill { get; set; } = false;         // re-kill apps that re-open themselves

    // ---- miner watchdog ----
    public bool MinerScan { get; set; } = true;
    public int MinerIntervalSec { get; set; } = 20;
    public bool AutoKillMiners { get; set; } = false;     // kill + blacklist miners without asking

    // ---- misc ----
    public bool LogToFile { get; set; } = true;

    /// <summary>Process names Smart Clean must never close (user keep-list).</summary>
    public List<string> KeepList { get; set; } = new();
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _lock = new();

    public string FilePath { get; }

    public SettingsStore(string? baseDir = null)
    {
        var dir = baseDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RAMRazor");
        try { Directory.CreateDirectory(dir); } catch { }
        FilePath = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                return s ?? new AppSettings();
            }
            catch (Exception ex)
            {
                LogService.Add("ERR", "SETTINGS", "load failed: " + ex.Message);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings s)
    {
        lock (_lock)
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s, JsonOpts)); }
            catch (Exception ex) { LogService.Add("ERR", "SETTINGS", "save failed: " + ex.Message); }
        }
    }
}
