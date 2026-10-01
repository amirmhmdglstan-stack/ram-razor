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
            bool granted = MemoryService.EnablePrivilege("SeProfileSingleProcessPrivilege");
            results.Add(("Memory: privilege acquire", true,
                granted ? "granted (elevated)" : "not granted (expected on non-elevated runner)"));
        }
        catch (Exception ex)
        {
            results.Add(("Memory: privilege acquire", false, ex.Message));
        }

        return results;
    }
}
