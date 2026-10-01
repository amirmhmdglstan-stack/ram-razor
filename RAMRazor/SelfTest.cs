using System.Diagnostics;
using RAMRazor.Core;

namespace RAMRazor;

/// <summary>
/// Headless self-test: runs on any Windows machine without elevation and
/// without the GUI (RAMRazor.exe --selftest). Used by GitHub Actions to
/// prove the built exe actually works before it is published to a Release.
/// Exit code 0 = all tests passed; 1 = at least one failure.
/// </summary>
public static class SelfTest
{
    public static int RunAll()
    {
        var results = LogicSelfCheck.RunPure();
        results.AddRange(LogicSelfCheck.RunPure()); // idempotency check (same tests twice)

        if (OperatingSystem.IsWindows())
            results.AddRange(RunWindowsTests());

        var lines = results.Select(r =>
            $"{(r.Pass ? "PASS" : "FAIL")}  {r.Name}  {r.Note}").ToList();
        int passed = results.Count(r => r.Pass);
        lines.Add($"SUMMARY: {passed}/{results.Count} passed");

        string outPath;
        try
        {
            outPath = Path.Combine(AppContext.BaseDirectory, "selftest-result.txt");
            File.WriteAllLines(outPath, lines);
        }
        catch { outPath = "(could not write selftest-result.txt)"; }

        try { Console.WriteLine(string.Join(Environment.NewLine, lines)); } catch { }
        try { Console.WriteLine($"Results file: {outPath}"); } catch { }

        return results.All(r => r.Pass) ? 0 : 1;
    }

    private static List<(string Name, bool Pass, string Note)> RunWindowsTests()
    {
        var results = new List<(string, bool, string)>();

        // ---- Memory statistics (P/Invoke) ----
        try
        {
            var ram = MemoryService.Read();
            bool ok = ram.Total > 0 && ram.LoadPercent is >= 0 and <= 100;
            results.Add(("Memory: GlobalMemoryStatusEx", ok,
                ok ? $"load={ram.LoadPercent}% total={FormatUtil.Bytes((long)ram.Total)}" : "FAILED"));
        }
        catch (Exception ex)
        {
            results.Add(("Memory: GlobalMemoryStatusEx", false, ex.Message));
        }

        // ---- Process enumeration ----
        try
        {
            var enumerator = new ProcessEnumerator();
            var snap = enumerator.Snapshot();
            bool ok = snap.Count > 10 && snap.Any(x => x.Name.Equals("services", StringComparison.OrdinalIgnoreCase));
            results.Add(("Enumerate: snapshot works", ok,
                ok ? $"{snap.Count} processes" : $"only {snap.Count} procs / no services.exe"));
        }
        catch (Exception ex)
        {
            results.Add(("Enumerate: snapshot works", false, ex.Message));
        }

        // ---- Real spawn + kill round-trip (same-user child, no elevation needed) ----
        Process? child = null;
        try
        {
            child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 > nul")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Thread.Sleep(700);
            bool spawned = child != null && !child.HasExited;
            var (outcome, detail) = spawned ? ProcessKiller.KillPid(child!.Id, force: true) : (KillOutcome.Failed, "spawn failed");
            bool killed = spawned && outcome == KillOutcome.Closed && ProcessKiller.Gone(child.Id);
            results.Add(("Kill: spawn cmd.exe + force close", killed,
                killed ? $"ok ({detail})" : $"spawned={spawned} outcome={outcome} {detail}"));
        }
        catch (Exception ex)
        {
            results.Add(("Kill: spawn cmd.exe + force close", false, ex.Message));
        }
        finally { try { child?.Kill(); } catch { } }

        // ---- TCP table parse (miner-detection plumbing, must not throw) ----
        try
        {
            var map = MinerDetector.GetRemotePortsByPid();
            results.Add(("Miner: TCP table parsed", true, $"{map.Count} pids with remote ports"));
        }
        catch (Exception ex)
        {
            results.Add(("Miner: TCP table parsed", false, ex.Message));
        }

        // ---- Privilege acquisition (expected to FAIL on non-elevated CI — tolerant) ----
        try
        {
            var priv = MemoryService.EnableAllPrivileges();
            results.Add(("Memory: privilege acquire", true,
                priv.Any(kv => kv.Value)
                    ? "granted: " + string.Join(",", priv.Where(kv => kv.Value).Select(kv => kv.Key))
                    : "not granted (expected on non-elevated runner)"));
        }
        catch (Exception ex)
        {
            results.Add(("Memory: privilege acquire", false, ex.Message));
        }

        // ---- v2 clean pipeline smoke test: must run to completion and report stages ----
        // (on a non-elevated runner the kernel commands are rejected — the test
        //  only proves the pipeline executes safely and reports per-stage status)
        try
        {
            var res = MemoryService.Clean(CleanMode.Deep);
            bool ok = res.TotalBytes > 0 && res.Stages.Count >= 4 && res.FreedBytes >= 0;
            results.Add(("Memory v2: clean pipeline runs", ok,
                ok ? $"mode={res.ModeName} stages={res.Stages.Count} freed={FormatUtil.Bytes(res.FreedBytes)} note={res.Note}"
                   : $"total={res.TotalBytes} stages={res.Stages.Count}"));
        }
        catch (Exception ex)
        {
            results.Add(("Memory v2: clean pipeline runs", false, ex.Message));
        }

        // ---- Smart Clean planner against the REAL process table (no killing!) ----
        try
        {
            var snap = new ProcessEnumerator().Snapshot();
            var nuc = SmartClean.PlanTargets(snap, SmartCleanScope.Nuclear);
            var std = SmartClean.PlanTargets(snap, SmartCleanScope.Standard);
            bool protectedCore = !nuc.Any(p => p.Name is "csrss" or "wininit" or "winlogon" or "lsass" or "services" or "smss" or "dwm");
            bool sane = snap.Count > 20 && protectedCore && nuc.Count > 0 && std.Count > 0;
            results.Add(("SmartClean: planner on real snapshot", sane,
                sane ? $"{snap.Count} procs -> standard {std.Count} / nuclear {nuc.Count} targets, core protected"
                     : $"snap={snap.Count} std={std.Count} nuc={nuc.Count} coreOk={protectedCore}"));
        }
        catch (Exception ex)
        {
            results.Add(("SmartClean: planner on real snapshot", false, ex.Message));
        }

        return results;
    }
}
