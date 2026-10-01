using RAMRazor.Core;

namespace RAMRazor.UI;

internal sealed partial class MainForm
{
    // ================= helpers =================

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        foreach (var b in new[] { _btnClean, _btnCloseAll, _btnSel, _btnAllBut, _btnForceForever })
            b.Enabled = !busy;
    }

    private List<AppGroup> VisibleTargetGroups()
        => _currentGroups.Where(g => !g.IsSystem).ToList();

    private List<ProcInfo> VisibleTargetProcs()
        => _currentProcs.Where(p => !p.IsSystem).ToList();

    private List<AppGroup> CheckedGroups()
        => VisibleTargetGroups().Where(g => _checkedKeys.Contains(g.Key)).ToList();

    private List<ProcInfo> CheckedProcs()
        => VisibleTargetProcs().Where(p => _checkedKeys.Contains("pid:" + p.Id)).ToList();

    /// <summary>Kills a batch of groups on a worker thread; logs everything;
    /// failed apps are pushed into the alert card.</summary>
    private void KillGroupsBatch(List<AppGroup> groups, bool force, string category)
    {
        if (groups.Count == 0) return;
        SetBusy(true);
        var groupsCopy = groups.ToList();
        Task.Run(() =>
        {
            int ok = 0, failed = 0;
            var failedNames = new List<string>();
            foreach (var g in groupsCopy)
            {
                var report = ProcessKiller.KillGroup(g, force);
                if (report.Outcome == KillOutcome.Failed)
                {
                    failed++;
                    failedNames.Add(g.DisplayName);
                    LogService.Add("ERR", category, $"FAILED to close '{g.DisplayName}' — {report.Detail}");
                    _pendingAlerts.Add(new AlertItem
                    {
                        Name = g.DisplayName,
                        ExePath = g.ExePath,
                        Source = "unkillable",
                        Detail = $"Could not be closed ({report.Detail}). Protected, access denied, or restarted faster than we can kill."
                    });
                }
                else
                {
                    ok++;
                    LogService.Add("INFO", category, $"closed '{g.DisplayName}' — {report.Detail}");
                }
            }
            LogService.Add("INFO", category, $"batch done: {ok} closed, {failed} failed, {groupsCopy.Count} total");
            BeginInvoke(() =>
            {
                SetBusy(false);
                RefreshData();
                if (failed > 0) ShowAlertFormSafe();
            });
        });
    }

    // ================= clean RAM =================

    private void DoCleanRam()
    {
        if (_busy) return;
        SetBusy(true);
        _btnClean.Text = "CLEANING…";
        bool deep = _chkDeep.Checked;
        Task.Run(() =>
        {
            var result = MemoryService.Clean(deep);
            BeginInvoke(() =>
            {
                SetBusy(false);
                _btnClean.Text = "CLEAN RAM";
                _lastAutoClean = DateTime.Now;
                _lblClean.Text = deep
                    ? $"Deep clean freed {FormatUtil.Bytes(result.FreedBytes)} ({result.FreedPercentOfTotal}% of total RAM)"
                    : $"Clean freed {FormatUtil.Bytes(result.FreedBytes)} ({result.FreedPercentOfTotal}% of total RAM)";
                _lblClean.ForeColor = result.FreedBytes > 0 ? Theme.Ok : Theme.TextDim;
                LogService.Add("INFO", "CLEAN",
                    $"{(deep ? "deep" : "soft")} clean freed {FormatUtil.Bytes(result.FreedBytes)} " +
                    $"({result.BeforePercent:0.0}% → {result.AfterPercent:0.0}% used) — {result.Note}");
                RefreshData();
            });
        });
    }

    // ================= close all =================

    private void DoCloseAll()
    {
        if (_busy) return;
        var targets = VisibleTargetGroups();
        if (targets.Count == 0)
        {
            MessageBox.Show("No closable apps are running — only protected Windows system tasks.",
                "RAM Razor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var mb = MessageBox.Show(
            $"This will FORCE-CLOSE all {targets.Count} non-system apps now.\n\n" +
            "Windows system components (shell, services, security, drivers) are protected and stay open.\n" +
            "Unsaved work in the closed apps will be lost.\n\nContinue?",
            "Close all apps", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (mb != DialogResult.Yes) return;

        LogService.Add("INFO", "CLOSEALL", $"closing {targets.Count} apps (force)");
        _watchdog.ArmWatch(targets, TimeSpan.FromSeconds(25));
        KillGroupsBatch(targets, force: true, category: "CLOSEALL");

        // automatic soft RAM clean after the massacre
        Task.Delay(2500).ContinueWith(_ => BeginInvoke(DoCleanRam));
    }

    // ================= close selected / all but selected =================

    private void DoCloseSelected()
    {
        if (_busy) return;
        bool appsView = _rbApps.Checked;
        string title = appsView ? "Select apps to close" : "Select tasks to close";
        var rows = BuildRows(appsView, _chkShowSystem.Checked).Where(r => !r.IsSystem).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("Nothing closable is running.", "RAM Razor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ProcessSelectForm(rows, title,
            "Close selected", Theme.Danger);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var selectedKeys = form.CheckedKeys;
        if (appsView)
        {
            var groups = VisibleTargetGroups().Where(g => selectedKeys.Contains(g.Key)).ToList();
            if (groups.Count == 0) return;
            LogService.Add("INFO", "CLOSESEL", $"closing {groups.Count} selected app(s)");
            KillGroupsBatch(groups, force: false, category: "CLOSESEL");
        }
        else
        {
            var procs = VisibleTargetProcs().Where(p => selectedKeys.Contains("pid:" + p.Id)).ToList();
            if (procs.Count == 0) return;
            SetBusy(true);
            Task.Run(() =>
            {
                foreach (var p in procs)
                {
                    var (outcome, detail) = ProcessKiller.KillPid(p.Id, force: false);
                    LogService.Add(outcome == KillOutcome.Failed ? "ERR" : "INFO", "CLOSESEL",
                        $"task '{p.Name}' (pid {p.Id}): {outcome} — {detail}");
                    if (outcome == KillOutcome.Failed)
                    {
                        _pendingAlerts.Add(new AlertItem
                        {
                            Name = p.Name,
                            ExePath = p.ExePath,
                            Source = "unkillable",
                            Detail = $"Task could not be closed ({detail})."
                        });
                    }
                }
                BeginInvoke(() => { SetBusy(false); RefreshData(); });
            });
        }
    }

    private void DoCloseAllButSelected()
    {
        if (_busy) return;
        bool appsView = _rbApps.Checked;
        string title = appsView ? "Select apps to KEEP (everything else closes)" : "Select tasks to KEEP (everything else closes)";
        var rows = BuildRows(appsView, _chkShowSystem.Checked).Where(r => !r.IsSystem).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("Nothing closable is running.", "RAM Razor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ProcessSelectForm(rows, title,
            "Close everything else", Theme.Danger);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var keep = form.CheckedKeys;
        if (appsView)
        {
            var victims = VisibleTargetGroups().Where(g => !keep.Contains(g.Key)).ToList();
            if (victims.Count == 0)
            {
                MessageBox.Show("Nothing to close — you selected everything.", "RAM Razor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var mb = MessageBox.Show(
                $"Force-close {victims.Count} app(s), keeping only the {keep.Count} you selected?\n\nUnsaved work will be lost.",
                "Close all but selected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (mb != DialogResult.Yes) return;

            LogService.Add("INFO", "CLOSEBUT", $"closing {victims.Count} app(s), keeping {keep.Count}");
            _watchdog.ArmWatch(victims, TimeSpan.FromSeconds(25));
            KillGroupsBatch(victims, force: true, category: "CLOSEBUT");
            Task.Delay(2500).ContinueWith(_ => BeginInvoke(DoCleanRam));
        }
        else
        {
            var victims = VisibleTargetProcs().Where(p => !keep.Contains("pid:" + p.Id)).ToList();
            if (victims.Count == 0) return;
            var mb = MessageBox.Show(
                $"Force-close {victims.Count} task(s), keeping only the {keep.Count} you selected?",
                "Close all but selected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (mb != DialogResult.Yes) return;

            SetBusy(true);
            Task.Run(() =>
            {
                foreach (var p in victims)
                {
                    var (outcome, detail) = ProcessKiller.KillPid(p.Id, force: true);
                    LogService.Add(outcome == KillOutcome.Failed ? "ERR" : "INFO", "CLOSEBUT",
                        $"task '{p.Name}' (pid {p.Id}): {outcome} — {detail}");
                }
                BeginInvoke(() => { SetBusy(false); RefreshData(); });
            });
        }
    }

    // ================= force close forever =================

    private void DoForceSelectedForever()
    {
        if (_busy) return;
        bool appsView = _rbApps.Checked;

        if (appsView)
        {
            var groups = CheckedGroups();
            if (groups.Count == 0)
            {
                MessageBox.Show("Tick the checkbox of the apps you want to blacklist first.\n" +
                    "Blacklisted apps are killed on sight whenever they appear.",
                    "Force close (forever)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var names = string.Join(", ", groups.Select(g => g.DisplayName));
            var mb = MessageBox.Show(
                $"Add {groups.Count} app(s) to the permanent blacklist?\n\n{names}\n\n" +
                "They will be closed now and killed automatically whenever they try to run again.\n" +
                "You can un-blacklist them anytime in the Blacklist section.",
                "Force close (forever)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (mb != DialogResult.Yes) return;

            foreach (var g in groups)
                ForceCloseForever(g.DisplayName, NameFromPathOrKey(g), "user blacklisted from main window");
            RefreshData();
        }
        else
        {
            var procs = CheckedProcs();
            if (procs.Count == 0)
            {
                MessageBox.Show("Tick the checkbox of the tasks you want to blacklist first.",
                    "Force close (forever)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var mb = MessageBox.Show(
                $"Add {procs.Count} task(s) to the permanent blacklist? They will be killed now and on every future start.",
                "Force close (forever)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (mb != DialogResult.Yes) return;

            foreach (var p in procs.GroupBy(x => x.Name.ToLowerInvariant()))
                ForceCloseForever(p.Key, p.First().ExePath, "user blacklisted from main window (tasks view)");
            RefreshData();
        }
    }

    private string NameFromPathOrKey(AppGroup g)
    {
        try
        {
            if (!string.IsNullOrEmpty(g.ExePath)) return Path.GetFileNameWithoutExtension(g.ExePath);
            if (g.Processes.Count > 0) return g.Processes[0].Name;
        }
        catch { }
        return g.DisplayName;
    }

    /// <summary>Blacklists by process name, kills every running instance, logs everything.</summary>
    private string ForceCloseForever(string name, string exePath, string reason)
    {
        bool added = false;
        try
        {
            added = _blacklist.Add(name, exePath, reason);
            if (added)
                LogService.Add("INFO", "BLACKLIST", $"'{name}' added to permanent blacklist ({reason})");
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "BLACKLIST", $"add '{name}' failed: {ex.Message}");
        }

        int killed = 0;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase) &&
                    p.Id != Environment.ProcessId)
                {
                    var (outcome, detail) = ProcessKiller.KillPid(p.Id, force: true);
                    if (outcome != KillOutcome.Failed) killed++;
                    LogService.Add(outcome == KillOutcome.Failed ? "ERR" : "INFO", "BLACKLIST",
                        $"kill '{name}' (pid {p.Id}): {outcome} — {detail}");
                }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return $"{(added ? "added to blacklist" : "already blacklisted")}; {killed} running instance(s) killed";
    }

    /// <summary>Force close forever + delete/quarantine the executable. Returns a status string.</summary>
    private string ForceDelete(string name, string exePath)
    {
        var mb = MessageBox.Show(
            $"Force-close '{name}' AND delete its executable?\n\n{exePath}\n\n" +
            "The file is moved to the RAM Razor quarantine folder (or deleted at next reboot if it is locked).\n" +
            "This cannot be undone from RAM Razor.",
            "Force delete", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
        if (mb != DialogResult.Yes) return "cancelled";

        ForceCloseForever(name, exePath, "force delete requested");
        Thread.Sleep(400);
        string status = ProcessKiller.TryQuarantineFile(exePath, _blacklist.BaseDirectory);
        LogService.Add("WARN", "DELETE", $"force delete of '{name}': {status}");
        RefreshData();
        return status;
    }

    // ================= list context menu =================

    private void ContextClose(bool force)
    {
        bool appsView = _rbApps.Checked;
        if (_list.SelectedItems.Count == 0) return;
        var rows = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag as SelectRow).Where(r => r != null && !r.IsSystem).ToList();

        if (appsView)
        {
            var groups = rows.Where(r => r.Group != null).Select(r => r.Group!).Distinct().ToList();
            KillGroupsBatch(groups, force, category: force ? "FORCECLOSE" : "CLOSE");
        }
        else
        {
            var procs = rows.Where(r => r.Proc != null).Select(r => r.Proc!).ToList();
            SetBusy(true);
            Task.Run(() =>
            {
                foreach (var p in procs)
                {
                    var (outcome, detail) = ProcessKiller.KillPid(p.Id, force);
                    LogService.Add(outcome == KillOutcome.Failed ? "ERR" : "INFO", "CLOSE",
                        $"task '{p.Name}' (pid {p.Id}): {outcome} — {detail}");
                }
                BeginInvoke(() => { SetBusy(false); RefreshData(); });
            });
        }
    }

    private void ContextCopyPath()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is SelectRow r && !string.IsNullOrEmpty(r.Path))
        {
            try { Clipboard.SetText(r.Path); } catch { }
        }
    }

    // ================= miner scan & alert card =================

    private void RunMinerScan(bool manual)
    {
        if (_busy && manual) return;
        try
        {
            var ports = MinerDetector.GetRemotePortsByPid();
            var findings = new List<MinerFinding>();
            foreach (var g in VisibleTargetGroups())
            {
                var f = MinerDetector.Evaluate(g, ports);
                if (f != null) findings.Add(f);
            }

            int newOnes = 0;
            foreach (var f in findings)
            {
                if (!_alertSuppressed.Add("m:" + f.GroupKey) && !manual) continue;
                if (_pendingAlerts.Any(a => a.Name == f.DisplayName)) continue;

                _pendingAlerts.Add(new AlertItem
                {
                    Name = f.DisplayName,
                    ExePath = f.ExePath,
                    Source = "miner",
                    Detail = $"Suspicious mining-like behavior (score {f.Score:0}): {f.ReasonText}."
                });
                newOnes++;
                LogService.Add("WARN", "MINER",
                    $"suspected miner: '{f.DisplayName}' — {f.ReasonText} (score {f.Score:0})");
            }

            if (newOnes > 0) ShowAlertFormSafe();
            else if (manual && _pendingAlerts.Count == 0)
                _lblStatus.Text = $"Miner scan finished {DateTime.Now:HH:mm:ss} — nothing suspicious found.";
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "MINER", "scan failed: " + ex.Message);
        }
    }

    private void ShowAlertForm()
    {
        // drop suppressed items
        _pendingAlerts.RemoveAll(a => _alertSuppressed.Contains("a:" + a.Name + a.Source));

        if (_pendingAlerts.Count == 0) return;
        if (_alertForm != null && !_alertForm.IsDisposed && _alertForm.Visible)
        {
            _alertForm.UpdateItems(_pendingAlerts.ToList());
            _alertForm.BringToFront();
            return;
        }

        _alertForm?.Dispose();
        _alertForm = new MinerAlertForm(_pendingAlerts.ToList(),
            onForceForever: item =>
            {
                var status = ForceCloseForever(item.Name, item.ExePath, item.Source);
                RefreshData();
                return status;
            },
            onForceDelete: item => ForceDelete(item.Name, item.ExePath),
            onDismiss: item => _alertSuppressed.Add("m:" + HashKey(item.Name)));
        _alertForm.Show(this);
    }

    private static string HashKey(string s) => s.ToLowerInvariant();
}
