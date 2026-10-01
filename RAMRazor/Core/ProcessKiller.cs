using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RAMRazor.Core;

/// <summary>
/// Terminates processes/app-groups: graceful window close first (optional),
/// then taskkill /T /F (full tree), then .NET Kill(entireProcessTree).
/// Also provides the file quarantine used by "Force delete".
/// </summary>
public static class ProcessKiller
{
    public static KillReport KillGroup(AppGroup group, bool force)
    {
        var report = new KillReport { Target = group.DisplayName };
        var survived = new List<int>();
        int closed = 0, gone = 0;

        foreach (var p in group.Processes.ToList())
        {
            var (outcome, detail) = KillPid(p.Id, force);
            switch (outcome)
            {
                case KillOutcome.Closed: closed++; break;
                case KillOutcome.AlreadyGone: gone++; break;
                case KillOutcome.Failed:
                    survived.Add(p.Id);
                    report.Detail = detail;
                    break;
            }
        }

        report.SurvivedPids = survived;
        report.Pids = group.Processes.Select(x => x.Id).ToList();

        if (survived.Count == 0 && closed > 0)
        {
            report.Outcome = KillOutcome.Closed;
            report.Detail = closed == 1 ? "closed" : $"{closed} processes closed";
        }
        else if (survived.Count == 0)
        {
            report.Outcome = KillOutcome.AlreadyGone;
            report.Detail = "already exited";
        }
        else
        {
            report.Outcome = KillOutcome.Failed;
            if (string.IsNullOrEmpty(report.Detail))
                report.Detail = $"{survived.Count} process(es) survived";
        }
        return report;
    }

    public static (KillOutcome, string) KillPid(int pid, bool force)
    {
        Process? p = null;
        try
        {
            try { p = Process.GetProcessById(pid); }
            catch (ArgumentException) { return (KillOutcome.AlreadyGone, "process not found"); }
            catch (Exception ex) { return (KillOutcome.Failed, "open failed: " + ex.Message); }

            if (p is null) return (KillOutcome.AlreadyGone, "process not found");

            string name = "<unknown>";
            try { name = p.ProcessName; } catch { }

            // 1) graceful close of windowed processes
            if (!force)
            {
                try
                {
                    if (p.CloseMainWindow() && p.WaitForExit(3000))
                        return (KillOutcome.Closed, "closed gracefully");
                }
                catch { }
            }
            else
            {
                // still try a quick graceful shot; ignore failures
                try { p.CloseMainWindow(); p.WaitForExit(800); } catch { }
            }

            // 2) taskkill full tree /F
            try
            {
                var psi = new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var tk = Process.Start(psi);
                tk?.WaitForExit(5000);
            }
            catch { }

            Thread.Sleep(120);
            if (Gone(pid)) return (KillOutcome.Closed, $"force-closed ({name})");

            // 3) .NET kill entire tree
            try
            {
                try { p = Process.GetProcessById(pid); } catch { p = null; }
                if (p == null) return (KillOutcome.AlreadyGone, "process not found");
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                if (Gone(pid)) return (KillOutcome.Closed, "closed");
                return (KillOutcome.Failed, $"kill failed: {ex.Message}");
            }

            Thread.Sleep(150);
            return Gone(pid)
                ? (KillOutcome.Closed, $"force-closed ({name})")
                : (KillOutcome.Failed, "protected or access denied");
        }
        catch (Exception ex)
        {
            return (KillOutcome.Failed, ex.Message);
        }
        finally
        {
            try { p?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Fast mass-kill path used by Smart Clean: direct tree kill, no taskkill
    /// spawn (spawning one taskkill per process is far too slow for 100+ targets).
    /// Falls back to the full pipeline when the direct kill is refused.
    /// </summary>
    public static (KillOutcome, string) KillPidFast(int pid)
    {
        Process? p = null;
        try
        {
            try { p = Process.GetProcessById(pid); }
            catch (ArgumentException) { return (KillOutcome.AlreadyGone, "process not found"); }
            if (p is null) return (KillOutcome.AlreadyGone, "process not found");

            string name = "<unknown>";
            try { name = p.ProcessName; } catch { }

            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
            catch
            {
                if (Gone(pid)) return (KillOutcome.Closed, "closed");
                return KillPid(pid, force: true); // full pipeline: graceful -> taskkill /T /F -> .NET kill
            }

            return Gone(pid)
                ? (KillOutcome.Closed, $"fast-killed ({name})")
                : KillPid(pid, force: true);
        }
        catch
        {
            return KillPid(pid, force: true);
        }
        finally
        {
            try { p?.Dispose(); } catch { }
        }
    }

    public static bool Gone(int pid)
    {
        try
        {
            var x = Process.GetProcessById(pid);
            try { return x.HasExited; }
            finally { try { x.Dispose(); } catch { } }
        }
        catch (ArgumentException) { return true; }
        catch { return false; }
    }

    /// <summary>
    /// Moves an executable into the quarantine folder; if the file is locked,
    /// schedules deletion at next reboot via MoveFileEx.
    /// </summary>
    public static string TryQuarantineFile(string exePath, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return "executable location unknown — file not removed";

        try
        {
            var qDir = Path.Combine(baseDir, "quarantine");
            Directory.CreateDirectory(qDir);
            var dest = Path.Combine(qDir,
                Path.GetFileName(exePath) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".quarantined");
            File.Move(exePath, dest);
            return "file moved to quarantine: " + dest;
        }
        catch (Exception moveEx)
        {
            try
            {
                if (MoveFileExW(exePath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                    return "file locked — scheduled for deletion at next reboot";
                return "file locked and could not be scheduled for deletion (" + moveEx.Message + ")";
            }
            catch (Exception ex2)
            {
                return "quarantine failed: " + ex2.Message;
            }
        }
    }

    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, int dwFlags);
}
