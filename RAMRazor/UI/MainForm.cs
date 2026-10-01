using System.ComponentModel;
using RAMRazor.Core;

namespace RAMRazor.UI;

internal sealed partial class MainForm : Form
{
    private readonly ProcessEnumerator _enumerator;
    private readonly BlacklistStore _blacklist;
    private readonly Watchdog _watchdog;

    private List<ProcInfo> _currentProcs = new();
    private List<AppGroup> _currentGroups = new();

    private readonly HashSet<string> _checkedKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _rebuilding;
    private bool _busy;

    // UI controls
    private readonly RamGauge _gauge;
    private readonly BufferedListView _list;
    private readonly Label _lblUsed, _lblAvail, _lblProcs, _lblClean, _lblStatus, _lblAdmin;
    private readonly CheckBox _chkDeep, _chkShowSystem, _chkAutoRekill, _chkMinerScan, _chkAutoClean;
    private readonly RadioButton _rbApps, _rbTasks;
    private readonly Button _btnClean, _btnCloseAll, _btnSel, _btnAllBut, _btnForceForever,
                            _btnBlacklist, _btnLog, _btnScanMiners, _btnRefresh;
    private readonly System.Windows.Forms.Timer _refreshTimer, _watchTimer, _minerTimer;
    private readonly NotifyIcon _tray;
    private Container _components;
    private readonly ContextMenuStrip _listMenu;

    // alerts (miner findings / unkillable / re-opened)
    private readonly List<AlertItem> _pendingAlerts = new();
    private MinerAlertForm? _alertForm;
    private readonly HashSet<string> _alertSuppressed = new();
    private DateTime _lastAutoClean = DateTime.Now;

    public MainForm()
    {
        LogService.Init();
        _blacklist = new BlacklistStore();
        _enumerator = new ProcessEnumerator();
        _watchdog = new Watchdog(_blacklist, _enumerator);

        Text = "RAM Razor — Admin RAM Cleaner (Windows 10/11)";
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(940, 700);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ---------- top panel: gauge + RAM info ----------
        _gauge = new RamGauge { Location = new Point(16, 14), Size = new Size(150, 150), UsagePercent = 0 };

        _lblUsed = Theme.Label("Used: —", Theme.Text, 10f, bold: true);
        _lblUsed.Location = new Point(182, 24);
        _lblAvail = Theme.Label("Available: —", Theme.TextDim, 9.5f);
        _lblAvail.Location = new Point(182, 52);
        _lblProcs = Theme.Label("Processes: —", Theme.TextDim, 9.5f);
        _lblProcs.Location = new Point(182, 76);
        _lblClean = Theme.Label("No clean performed yet.", Theme.Ok, 9f);
        _lblClean.Location = new Point(182, 100);

        _chkDeep = Theme.CheckBox("Deep clean (purge standby list — needs admin)", true);
        _chkDeep.Location = new Point(182, 130);

        _btnClean = Theme.Button("CLEAN RAM", Theme.AccentDk, Theme.Text, 150, 46);
        _btnClean.Location = new Point(378, 24);
        _btnClean.Click += (_, _) => DoCleanRam();

        _chkAutoClean = Theme.CheckBox("Auto-clean every 10 minutes", false, Theme.TextDim);
        _chkAutoClean.Location = new Point(378, 84);

        _lblAdmin = Theme.Label("", Theme.Ok, 10f, bold: true);
        _lblAdmin.Location = new Point(ClientSize.Width - 220, 20);
        _lblAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        Controls.AddRange(new Control[] { _gauge, _lblUsed, _lblAvail, _lblProcs, _lblClean, _chkDeep, _btnClean, _chkAutoClean, _lblAdmin });

        // ---------- toolbar ----------
        var lblList = Theme.Label("Running software:", Theme.TextDim, 9.5f, bold: true);
        lblList.Location = new Point(16, 184);

        _rbApps = new RadioButton
        {
            Text = "Apps",
            Checked = true,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Location = new Point(150, 182),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        _rbTasks = new RadioButton
        {
            Text = "Tasks",
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Location = new Point(210, 182),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        _rbApps.CheckedChanged += (_, _) => { _checkedKeys.Clear(); RebuildList(); };

        _chkShowSystem = Theme.CheckBox("Show system tasks", false, Theme.TextDim);
        _chkShowSystem.Location = new Point(280, 182);
        _chkShowSystem.CheckedChanged += (_, _) => RebuildList();

        _btnRefresh = Theme.Button("Refresh", Theme.Panel, Theme.Text, 100, 26);
        _btnRefresh.Location = new Point(ClientSize.Width - 120, 178);
        _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnRefresh.Click += (_, _) => RefreshData();

        Controls.AddRange(new Control[] { lblList, _rbApps, _rbTasks, _chkShowSystem, _btnRefresh });

        // ---------- process list ----------
        _list = new BufferedListView
        {
            Location = new Point(16, 212),
            Size = new Size(ClientSize.Width - 32, ClientSize.Height - 212 - 96),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Sorting = SortOrder.None
        };
        _list.Columns.Add("App", 270);
        _list.Columns.Add("Processes", 90);
        _list.Columns.Add("CPU %", 70);
        _list.Columns.Add("RAM", 90);
        _list.Columns.Add("Type", 80);
        _list.Columns.Add("Window", 70);
        _list.Columns.Add("Path", 400);
        _list.ItemChecked += List_ItemChecked;

        _listMenu = new ContextMenuStrip();
        _listMenu.Items.Add("Close (graceful first)", null, (_, _) => ContextClose(false));
        _listMenu.Items.Add("Force close", null, (_, _) => ContextClose(true));
        _listMenu.Items.Add("Force close forever (blacklist)", null, (_, _) => DoForceSelectedForever());
        _listMenu.Items.Add("Copy executable path", null, (_, _) => ContextCopyPath());
        _list.ContextMenuStrip = _listMenu;

        Controls.Add(_list);

        // ---------- action buttons ----------
        _btnCloseAll = Theme.Button("CLOSE ALL APPS", Theme.Danger, Color.White, 170, 44);
        _btnCloseAll.Location = new Point(16, ClientSize.Height - 158);
        _btnCloseAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnCloseAll.Click += (_, _) => DoCloseAll();

        _btnSel = Theme.Button("Close selected…", Theme.Panel, Theme.Text, 160, 44);
        _btnSel.Location = new Point(196, ClientSize.Height - 158);
        _btnSel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnSel.Click += (_, _) => DoCloseSelected();

        _btnAllBut = Theme.Button("Close all but selected…", Theme.Panel, Theme.Text, 190, 44);
        _btnAllBut.Location = new Point(366, ClientSize.Height - 158);
        _btnAllBut.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnAllBut.Click += (_, _) => DoCloseAllButSelected();

        _btnForceForever = Theme.Button("Force close selected (forever)", Theme.DangerDk, Theme.Text, 230, 44);
        _btnForceForever.Location = new Point(566, ClientSize.Height - 158);
        _btnForceForever.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnForceForever.Click += (_, _) => DoForceSelectedForever();

        _btnBlacklist = Theme.Button("Blacklist", Theme.Panel, Theme.Text, 130, 36);
        _btnBlacklist.Location = new Point(16, ClientSize.Height - 104);
        _btnBlacklist.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnBlacklist.Click += (_, _) => { using var f = new BlacklistForm(_blacklist); f.ShowDialog(this); };

        _btnLog = Theme.Button("Activity log", Theme.Panel, Theme.Text, 120, 36);
        _btnLog.Location = new Point(156, ClientSize.Height - 104);
        _btnLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnLog.Click += (_, _) => { using var f = new LogForm(); f.ShowDialog(this); };

        _btnScanMiners = Theme.Button("Scan for miners", Theme.Panel, Theme.Text, 150, 36);
        _btnScanMiners.Location = new Point(286, ClientSize.Height - 104);
        _btnScanMiners.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnScanMiners.Click += (_, _) => RunMinerScan(manual: true);

        _chkAutoRekill = Theme.CheckBox("Auto re-kill apps that open back", false, Theme.TextDim);
        _chkAutoRekill.Location = new Point(452, ClientSize.Height - 100);
        _chkAutoRekill.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _chkAutoRekill.CheckedChanged += (_, _) =>
            LogService.Add("INFO", "SETTINGS", $"auto re-kill = {_chkAutoRekill.Checked}");

        _chkMinerScan = Theme.CheckBox("Miner scan every 20 s", true, Theme.TextDim);
        _chkMinerScan.Location = new Point(680, ClientSize.Height - 100);
        _chkMinerScan.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        Controls.AddRange(new Control[] { _btnCloseAll, _btnSel, _btnAllBut, _btnForceForever,
            _btnBlacklist, _btnLog, _btnScanMiners, _chkAutoRekill, _chkMinerScan });

        // ---------- status ----------
        _lblStatus = Theme.Label("Starting…", Theme.TextDim, 9f);
        _lblStatus.Location = new Point(16, ClientSize.Height - 56);
        _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_lblStatus);

        // ---------- tray ----------
        _components = new Container();
        _tray = new NotifyIcon(_components)
        {
            Icon = Icon,
            Text = "RAM Razor",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        // ---------- timers ----------
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => RefreshData();

        _watchTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _watchTimer.Tick += (_, _) => WatchTick();

        _minerTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        _minerTimer.Tick += (_, _) => { if (_chkMinerScan.Checked) RunMinerScan(manual: false); };

        _watchdog.Reopened += display =>
        {
            _pendingAlerts.Add(new AlertItem
            {
                Name = display,
                Source = "reopen",
                Detail = "Re-opened itself after being closed (auto-restart behavior)."
            });
            ShowAlertFormSafe();
        };
        _watchdog.BlacklistedKilled += name =>
        {
            _lblStatus.Text = $"Blacklisted '{name}' was killed on sight.";
        };

        LogService.EntryAdded += OnLogEntry;
        LogService.Add("INFO", "STARTUP", $"RAM Razor v{typeof(MainForm).Assembly.GetName().Version} started (elevated: {Program.IsElevated()})");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _lblAdmin.Text = "ADMIN MODE ✔";
        _lblAdmin.ForeColor = Theme.Ok;
        RefreshData();
        _refreshTimer.Start();
        _watchTimer.Start();
        _minerTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _watchTimer.Stop();
        _minerTimer.Stop();
        LogService.EntryAdded -= OnLogEntry;
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ================= refresh =================

    private void RefreshData()
    {
        if (_busy) return;
        try
        {
            _currentProcs = _enumerator.Snapshot();
            _currentGroups = _enumerator.BuildGroups(_currentProcs);

            var ram = MemoryService.Read();
            _gauge.UsagePercent = ram.UsedPercent;
            _gauge.CenterText = $"{ram.UsedPercent:0}%";
            _lblUsed.Text = $"Used: {FormatUtil.Bytes((long)ram.Used)} / {FormatUtil.Bytes((long)ram.Total)}  ({ram.LoadPercent}%)";
            _lblAvail.Text = $"Available: {FormatUtil.Bytes((long)ram.Available)}";
            _lblProcs.Text = $"Processes: {_currentProcs.Count}   Apps: {_currentGroups.Count(g => !g.IsSystem)}";

            RebuildList();

            _lblStatus.Text = $"Watching {_currentGroups.Count(g => !g.IsSystem)} apps · {_currentProcs.Count} tasks · refreshed {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "REFRESH", ex.Message);
        }
    }

    private List<SelectRow> BuildRows(bool appsView, bool showSystem)
    {
        var rows = new List<SelectRow>();
        if (appsView)
        {
            foreach (var g in _currentGroups)
            {
                if (g.IsSystem && !showSystem) continue;
                rows.Add(new SelectRow
                {
                    CheckKey = g.Key,
                    Name = g.DisplayName,
                    PidText = g.PidText,
                    Cpu = $"{g.CpuPercent:0.0}",
                    Mem = FormatUtil.Bytes(g.MemBytes),
                    Type = g.IsSystem ? "System" : "App",
                    Window = g.HasWindow ? "Yes" : "No",
                    Path = g.ExePath,
                    MemBytes = g.MemBytes,
                    Group = g,
                    IsSystem = g.IsSystem
                });
            }
        }
        else
        {
            foreach (var p in _currentProcs)
            {
                if (p.IsSystem && !showSystem) continue;
                rows.Add(new SelectRow
                {
                    CheckKey = "pid:" + p.Id,
                    Name = string.IsNullOrWhiteSpace(p.Title) ? p.Name : $"{p.Name} — {p.Title}",
                    PidText = p.Id.ToString(),
                    Cpu = $"{p.CpuPercent:0.0}",
                    Mem = FormatUtil.Bytes(p.MemBytes),
                    Type = p.IsSystem ? "System" : "Task",
                    Window = p.HasWindow ? "Yes" : "No",
                    Path = p.ExePath,
                    MemBytes = p.MemBytes,
                    Proc = p,
                    IsSystem = p.IsSystem
                });
            }
        }
        _checkedKeys.RemoveWhere(k => !rows.Any(r => r.CheckKey == k));
        return rows.OrderByDescending(r => r.MemBytes).ToList();
    }

    private void RebuildList()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        _list.BeginUpdate();
        _list.SuspendLayout();
        try
        {
            bool appsView = _rbApps.Checked;
            bool showSystem = _chkShowSystem.Checked;
            _list.Columns[0].Text = appsView ? "App" : "Task";
            _list.Columns[1].Text = appsView ? "Processes" : "PID";

            var rows = BuildRows(appsView, showSystem);
            _list.Items.Clear();
            foreach (var r in rows)
            {
                var item = new ListViewItem(new[]
                {
                    r.Name, r.PidText, r.Cpu, r.Mem, r.Type, r.Window, string.IsNullOrEmpty(r.Path) ? "(unknown)" : r.Path
                })
                {
                    Tag = r,
                    Checked = _checkedKeys.Contains(r.CheckKey),
                    ForeColor = r.IsSystem ? Theme.TextDim : Theme.Text
                };
                if (r.Name.Contains("miner", StringComparison.OrdinalIgnoreCase) && !r.IsSystem)
                    item.ForeColor = Theme.Warn;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.ResumeLayout(false);
            _list.EndUpdate();
            _rebuilding = false;
        }
    }

    private void List_ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_rebuilding) return;
        if (e.Item?.Tag is not SelectRow r) return;
        if (e.Item.Checked) _checkedKeys.Add(r.CheckKey);
        else _checkedKeys.Remove(r.CheckKey);
    }

    private void OnLogEntry(string level, string category, string message)
    {
        // cheap status flash for important events
        if (category is "REOPEN" or "MINER" or "DELETE")
        {
            try { BeginInvoke(() => _lblStatus.Text = $"[{level}] {category}: {message}"); } catch { }
        }
    }

    private void WatchTick()
    {
        _watchdog.AutoRekill = _chkAutoRekill.Checked;
        try
        {
            _watchdog.Tick();
        }
        catch (Exception ex)
        {
            LogService.Add("ERR", "WATCHDOG", ex.Message);
        }

        // auto clean
        if (_chkAutoClean.Checked && (DateTime.Now - _lastAutoClean).TotalMinutes >= 10)
        {
            _lastAutoClean = DateTime.Now;
            DoCleanRam();
        }
    }

    private void ShowAlertFormSafe()
    {
        try { BeginInvoke(ShowAlertForm); } catch { }
    }
}
