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

        // ---- 6. v2 memory engine: kernel command constants must be the real 0-based enum ----
        bool c1 = MemoryService.CmdEmptyWorkingSets == 2 && MemoryService.CmdPurgeStandbyList == 4 &&
                  MemoryService.CmdFlushModifiedList == 3;
        results.Add(("Memory v2: kernel command constants", c1,
            c1 ? "EmptyWS=2 FlushModified=3 PurgeStandby=4" : "WRONG CONSTANTS"));

        // ---- 7. Smart Clean planner: standard scope ----
        var fakeSnap = new List<ProcInfo>
        {
            new() { Id = 10, Name = "chrome", SessionId = 1, ExePath = @"C:\Users\x\chrome.exe" },
            new() { Id = 11, Name = "steam", SessionId = 1, ExePath = @"C:\Program Files\steam.exe" },
            new() { Id = 12, Name = "explorer", SessionId = 1, ExePath = @"C:\Windows\explorer.exe" },
            new() { Id = 13, Name = "svchost", SessionId = 0, ExePath = @"C:\Windows\System32\svchost.exe" },
            new() { Id = 14, Name = "notepad", SessionId = 1, ExePath = @"C:\Windows\notepad.exe" }
        };
        var stdTargets = SmartClean.PlanTargets(fakeSnap, SmartCleanScope.Standard);
        bool s1 = stdTargets.Any(t => t.Name == "chrome") && stdTargets.Any(t => t.Name == "steam") &&
                  stdTargets.Any(t => t.Name == "notepad") &&
                  !stdTargets.Any(t => t.Name is "explorer" or "svchost");
        results.Add(("SmartClean: standard plan", s1,
            s1 ? "user apps targeted, shell+services spared" : $"targets: {string.Join(", ", stdTargets.Select(t => t.Name))}"));

        // ---- 8. Smart Clean planner: nuclear scope keeps only the BSOD-critical set ----
        var nucTargets = SmartClean.PlanTargets(fakeSnap, SmartCleanScope.Nuclear);
        bool s2 = nucTargets.Any(t => t.Name is "explorer" or "chrome" or "steam" or "notepad") &&
                  !nucTargets.Any(t => t.Name == "svchost");
        results.Add(("SmartClean: nuclear plan", s2,
            s2 ? "shell + apps closed, svchost spared" : $"targets: {string.Join(", ", nucTargets.Select(t => t.Name))}"));

        // ---- 9. Smart Clean planner: critical set sanity ----
        bool s3 = SmartClean.IsCriticalName("csrss") && SmartClean.IsCriticalName("winlogon") &&
                  SmartClean.IsCriticalName("lsass") && SmartClean.IsCriticalName("Memory Compression") &&
                  SmartClean.IsCriticalName("dwm") && !SmartClean.IsCriticalName("chrome") &&
                  !SmartClean.IsCriticalName("explorer");
        results.Add(("SmartClean: BSOD-critical set", s3, s3 ? "core set protected" : "FAILED"));

        // ---- 10. Smart Clean planner: user keep-list protects apps ----
        var keepTargets = SmartClean.PlanTargets(fakeSnap, SmartCleanScope.Nuclear,
            keepList: new[] { "CHROME.exe" }); // normalization must make case/extension irrelevant
        bool s4 = !keepTargets.Any(t => t.Name == "chrome") &&
                  keepTargets.Any(t => t.Name == "steam");
        results.Add(("SmartClean: keep-list protection", s4,
            s4 ? "chrome spared, steam still targeted" : "FAILED"));

        // ---- 11. Smart Clean planner: session-0 services only with IncludeServices ----
        var serviceSnap = new List<ProcInfo>
        {
            new() { Id = 20, Name = "wmiprvse", SessionId = 0, ExePath = @"C:\Windows\System32\wbem\wmiprvse.exe" }
        };
        bool s5 = SmartClean.PlanTargets(serviceSnap, SmartCleanScope.Nuclear, includeServices: false).Count == 0 &&
                  SmartClean.PlanTargets(serviceSnap, SmartCleanScope.Nuclear, includeServices: true).Count == 1;
        results.Add(("SmartClean: services gate", s5, s5 ? "session-0 opt-in works" : "FAILED"));

        // ---- 12. Settings store round-trip (isolated temp dir) ----
        string stDir = Path.Combine(Path.GetTempPath(), "ramrazor-settest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new SettingsStore(stDir);
            var settings = new AppSettings
            {
                CleanLevel = "Extreme",
                SmartScope = "Nuclear",
                RestartExplorer = false,
                AutoClean = true,
                AutoCleanMinutes = 15,
                AutoCleanThreshold = 80,
                StartInTray = true,
                MinerIntervalSec = 60,
                AutoKillMiners = true,
                KeepList = { "chrome", "my-game" }
            };
            store.Save(settings);
            var reloaded = new SettingsStore(stDir).Load();
            bool r1 = reloaded.CleanLevel == "Extreme" && reloaded.SmartScope == "Nuclear" &&
                      !reloaded.RestartExplorer && reloaded.AutoClean && reloaded.AutoCleanMinutes == 15 &&
                      reloaded.AutoCleanThreshold == 80 && reloaded.StartInTray &&
                      reloaded.MinerIntervalSec == 60 && reloaded.AutoKillMiners &&
                      reloaded.KeepList.Count == 2 && reloaded.KeepList.Contains("chrome");
            results.Add(("Settings: persist + reload", r1, r1 ? "all fields round-tripped" : "MISMATCH"));
        }
        catch (Exception ex)
        {
            results.Add(("Settings: round-trip", false, ex.Message));
        }
        finally
        {
            try { Directory.Delete(stDir, true); } catch { }
        }

        return results;
    }
}
