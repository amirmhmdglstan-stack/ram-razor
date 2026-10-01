namespace RAMRazor.Core;

/// <summary>
/// Platform-neutral logic checks shared by the Windows self-test
/// (RAMRazor.exe --selftest in CI) and the local Linux test console.
/// </summary>
public static class LogicSelfCheck
{
    public static List<(string Name, bool Pass, string Note)> RunPure()
    {
        var results = new List<(string, bool, string)>();

        // ---- 1. System whitelist classification ----
        var sysService = new ProcInfo { Id = 500, Name = "svchost", SessionId = 0 };
        var sysExplorer = new ProcInfo { Id = 600, Name = "explorer", SessionId = 1 };
        var userApp = new ProcInfo
        {
            Id = 12345,
            Name = "notepad",
            SessionId = 1,
            ExePath = @"C:\Users\amir\AppData\Local\MyApp\notepad.exe"
        };
        bool w1 = SystemWhitelist.IsSystem(sysService);
        bool w2 = SystemWhitelist.IsSystem(sysExplorer);
        bool w3 = !SystemWhitelist.IsSystem(userApp);
        results.Add(("Whitelist: session-0 classified system", w1, w1 ? "svchost→system" : "FAILED"));
        results.Add(("Whitelist: explorer protected", w2, w2 ? "explorer→system" : "FAILED"));
        results.Add(("Whitelist: user app closable", w3, w3 ? "notepad→user" : "FAILED"));

        // ---- 2. Miner heuristics ----
        var fakeMiner = new AppGroup
        {
            Key = "c:/temp/xmrig.exe",
            DisplayName = "xmrig",
            ExePath = @"C:\Temp\xmrig.exe",
            CpuPercent = 90,
            HasWindow = false,
            IsSystem = false,
            Processes = new List<ProcInfo> { new() { Id = 9999, Name = "xmrig" } }
        };
        var finding = MinerDetector.Evaluate(fakeMiner, new Dictionary<int, List<int>>());
        bool m1 = finding != null && finding.Score >= 100;
        results.Add(("Miner: known name detected", m1, m1 ? $"score={finding?.Score}" : "FAILED"));

        var fakeChrome = new AppGroup
        {
            Key = "c:/program files/chrome.exe",
            DisplayName = "chrome",
            ExePath = @"C:\Program Files\chrome.exe",
            CpuPercent = 7,
            HasWindow = true,
            IsSystem = false,
            Processes = new List<ProcInfo> { new() { Id = 8888, Name = "chrome" } }
        };
        var clean = MinerDetector.Evaluate(fakeChrome, new Dictionary<int, List<int>>());
        bool m2 = clean == null;
        results.Add(("Miner: normal app not flagged", m2, m2 ? "no finding" : "FALSE POSITIVE"));

        bool m3 = MinerDetector.NameLooksLikeMiner("XMRig-Worker") &&
                  !MinerDetector.NameLooksLikeMiner("Calculator");
        results.Add(("Miner: name matcher", m3, m3 ? "ok" : "FAILED"));

        // ---- 3. Blacklist store round-trip (isolated temp dir) ----
        string blDir = Path.Combine(Path.GetTempPath(), "ramrazor-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var bl = new BlacklistStore(blDir);
            bool added = bl.Add("TestMiner", @"C:\Temp\testminer.exe", "unit test");
            bool contains = bl.Contains("TestMiner") && bl.Contains("testminer.exe");
            results.Add(("Blacklist: add + contains", added && contains, added && contains ? "ok" : "FAILED"));

            // reload from disk in a fresh instance
            var bl2 = new BlacklistStore(blDir);
            bool persisted = bl2.Contains("TestMiner");
            bool removed = bl2.Remove("TestMiner") && !bl2.Contains("TestMiner");
            results.Add(("Blacklist: persist + remove", persisted && removed, persisted && removed ? "ok" : "FAILED"));
        }
        catch (Exception ex)
        {
            results.Add(("Blacklist: round-trip", false, ex.Message));
        }
        finally
        {
            try { Directory.Delete(blDir, true); } catch { }
        }

        // ---- 4. Log service round-trip (isolated temp dir) ----
        string logDir = Path.Combine(Path.GetTempPath(), "ramrazor-logtest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // reset static dir by writing into explicit dir via Init
            LogService.Init(Path.Combine(logDir, "logs"));
            string received = "";
            void Handler(string lvl, string cat, string msg) => received = msg;
            LogService.EntryAdded += Handler;
            LogService.Add("INFO", "TEST", "roundtrip-check");
            LogService.EntryAdded -= Handler;

            bool logged = received == "roundtrip-check";
            var files = Directory.GetFiles(Path.Combine(logDir, "logs"));
            bool written = files.Length > 0 && new FileInfo(files[0]).Length > 0;
            results.Add(("Log: event + file written", logged && written, logged && written ? files[0] : "FAILED"));
        }
        catch (Exception ex)
        {
            results.Add(("Log: round-trip", false, ex.Message));
        }
        finally
        {
            try { Directory.Delete(logDir, true); } catch { }
        }

        // ---- 5. Grouping logic ----
        var enumerator = new ProcessEnumerator();
        var snap = new List<ProcInfo>
        {
            new() { Id = 1, Name = "app", ExePath = @"C:\A\app.exe", GroupKey = @"c:\a\app.exe", MemBytes = 100, HasWindow = true },
            new() { Id = 2, Name = "app", ExePath = @"C:\A\app.exe", GroupKey = @"c:\a\app.exe", MemBytes = 200 },
            new() { Id = 3, Name = "other", ExePath = "", GroupKey = "name:other", MemBytes = 50 }
        };
        var groups = enumerator.BuildGroups(snap);
        var appGroup = groups.FirstOrDefault(g => g.Key == @"c:\a\app.exe");
        bool g1 = appGroup != null && appGroup.Processes.Count == 2 &&
                  Math.Abs(appGroup.MemBytes - 300) < 1 && appGroup.HasWindow;
        results.Add(("Grouping: same exe = one app", g1, g1 ? "2 procs merged" : "FAILED"));

        return results;
    }
}
