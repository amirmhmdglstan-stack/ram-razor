using System.Text.Json;

namespace RAMRazor.Core;

public sealed class BlacklistEntry
{
    public string Name { get; set; } = "";       // process name without extension, lower-case
    public string ExePath { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Persistent "force close forever" blacklist. Watched continuously — any
/// blacklisted process found running is killed on sight.
/// Stored at %ProgramData%\RAMRazor\blacklist.json.
/// </summary>
public sealed class BlacklistStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private readonly object _lock = new();
    private List<BlacklistEntry> _entries = new();

    public string BaseDirectory { get; }
    public string FilePath { get; }

    public event Action? Changed;

    public BlacklistStore(string? baseDir = null)
    {
        BaseDirectory = baseDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RAMRazor");
        Directory.CreateDirectory(BaseDirectory);
        FilePath = Path.Combine(BaseDirectory, "blacklist.json");
        Load();
    }

    public IReadOnlyList<BlacklistEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public static string Normalize(string processName) =>
        (processName ?? "").Trim().ToLowerInvariant().Replace(".exe", "");

    public bool Contains(string processName)
    {
        var n = Normalize(processName);
        lock (_lock) return _entries.Any(e => e.Name == n);
    }

    public bool Add(string processName, string exePath, string reason)
    {
        var n = Normalize(processName);
        if (n.Length == 0) return false;
        lock (_lock)
        {
            if (_entries.Any(e => e.Name == n)) return false;
            _entries.Add(new BlacklistEntry
            {
                Name = n,
                ExePath = exePath ?? "",
                Reason = reason ?? "",
                AddedAt = DateTime.Now
            });
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public bool Remove(string processName)
    {
        var n = Normalize(processName);
        bool removed;
        lock (_lock)
        {
            removed = _entries.RemoveAll(e => e.Name == n) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            SaveLocked();
        }
        Changed?.Invoke();
    }

    private void SaveLocked()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "BLACKLIST", "save failed: " + ex.Message);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = File.ReadAllText(FilePath);
            var parsed = JsonSerializer.Deserialize<List<BlacklistEntry>>(data);
            if (parsed != null) _entries = parsed;
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "BLACKLIST", "load failed: " + ex.Message);
        }
    }
}
