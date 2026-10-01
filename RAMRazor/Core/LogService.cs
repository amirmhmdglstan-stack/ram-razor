namespace RAMRazor.Core;

/// <summary>
/// Central activity log. Everything the app closes (and everything it fails
/// to close, plus re-opened apps) is recorded here and written to
/// %ProgramData%\RAMRazor\logs\app-yyyyMMdd.log.
/// </summary>
public static class LogService
{
    private static readonly object _lock = new();
    private static readonly List<string> _recent = new();
    private static string? _dir;

    public static event Action<string, string, string>? EntryAdded; // level, category, message

    public static string LogDirectory
    {
        get
        {
            Init();
            return _dir!;
        }
    }

    public static void Init(string? dir = null)
    {
        if (_dir != null && dir == null) return;
        _dir = dir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RAMRazor", "logs");
        try { Directory.CreateDirectory(_dir); } catch { }
    }

    public static void Add(string level, string category, string message)
    {
        Init();
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{level}\t{category}\t{message}";
        List<string> snapshot;
        lock (_lock)
        {
            _recent.Add(line);
            if (_recent.Count > 800) _recent.RemoveRange(0, _recent.Count - 800);
            snapshot = _recent.ToList();
        }

        try
        {
            var file = Path.Combine(_dir!, $"app-{DateTime.Now:yyyyMMdd}.log");
            using var w = new StreamWriter(file, append: true) { AutoFlush = true };
            w.WriteLine(line);
        }
        catch { /* log IO must never crash the app */ }

        try { EntryAdded?.Invoke(level, category, message); } catch { }
    }

    public static IReadOnlyList<string> Recent()
    {
        lock (_lock) return _recent.ToList();
    }

    /// <summary>For very early logging before UI exists; never throws.</summary>
    public static void TryQuickLog(string category, string message)
    {
        try { Add("INFO", category, message); } catch { }
    }
}
