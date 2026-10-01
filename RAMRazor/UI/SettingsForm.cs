using RAMRazor.Core;

namespace RAMRazor.UI;

/// <summary>
/// Full settings dialog: clean intensity, smart-clean scope, nuclear behaviour,
/// automation, miner watchdog, file logging and the user keep-list
/// (apps Smart Clean must never close).
/// Mutates the passed AppSettings instance and saves it on OK.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly SettingsStore _store;

    private readonly ComboBox _cmbLevel, _cmbScope, _cmbAutoMin, _cmbAutoThr, _cmbMinerSec;
    private readonly CheckBox _chkRestartExplorer, _chkIncludeServices, _chkAutoClean,
        _chkCleanStartup, _chkStartTray, _chkAutoRekill, _chkMinerScan, _chkAutoKillMiners, _chkLogToFile;
    private readonly ListBox _lstKeep;
    private readonly TextBox _txtKeep;

    public SettingsForm(AppSettings settings, SettingsStore store)
    {
        _s = settings;
        _store = store;

        Text = "RAM Razor — Settings";
        ClientSize = new Size(560, 640);
        MinimumSize = new Size(576, 700);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        int y = 16;

        var hdr1 = Theme.Label("CLEAN RAM button intensity", Theme.Accent, 10f, bold: true);
        hdr1.Location = new Point(16, y); y += 28;

        _cmbLevel = NewCombo(new[] { "Light — working-set trim only", "Deep — trim + standby purge", "Extreme — trim + standby + modified-list flush" });
        _cmbLevel.Location = new Point(16, y);
        _cmbLevel.SelectedIndex = _s.CleanLevel switch { "Light" => 0, "Extreme" => 2, _ => 1 };
        y += 32;

        var hdr2 = Theme.Label("SMART CLEAN scope", Theme.Accent, 10f, bold: true);
        hdr2.Location = new Point(16, y); y += 28;

        _cmbScope = NewCombo(new[]
        {
            "Standard — close all non-system apps + purge",
            "Nuclear — close EVERYTHING except Windows-critical + purge"
        });
        _cmbScope.Location = new Point(16, y);
        _cmbScope.SelectedIndex = _s.SmartScope == "Nuclear" ? 1 : 0;
        y += 32;

        _chkRestartExplorer = Theme.CheckBox("Nuclear: restart explorer.exe automatically after the clean", _s.RestartExplorer);
        _chkRestartExplorer.Location = new Point(16, y); y += 26;
        _chkIncludeServices = Theme.CheckBox("Nuclear: also close service hosts (session 0) — can degrade Windows until reboot", _s.IncludeServices);
        _chkIncludeServices.Location = new Point(16, y); y += 34;

        var hdr3 = Theme.Label("Automation", Theme.Accent, 10f, bold: true);
        hdr3.Location = new Point(16, y); y += 28;

        _chkAutoClean = Theme.CheckBox("Auto CLEAN RAM every", _s.AutoClean);
        _chkAutoClean.Location = new Point(16, y);
        _cmbAutoMin = NewCombo(new[] { "5", "10", "15", "30", "60" });
        _cmbAutoMin.Location = new Point(280, y - 3);
        _cmbAutoMin.Width = 60;
        _cmbAutoMin.SelectedIndex = BestIndex(_cmbAutoMin, _s.AutoCleanMinutes.ToString());
        var lblMin = Theme.Label("minutes, only when RAM used ≥", Theme.TextDim);
        lblMin.Location = new Point(348, y);
        _cmbAutoThr = NewCombo(new[] { "0 (always)", "50", "60", "70", "80", "90" });
        _cmbAutoThr.Location = new Point(520 - 40, y - 3);
        _cmbAutoThr.Width = 70;
        _cmbAutoThr.SelectedIndex = BestIndex(_cmbAutoThr, _s.AutoCleanThreshold + (_s.AutoCleanThreshold == 0 ? " (always)" : "") + "%");
        y += 34;

        _chkCleanStartup = Theme.CheckBox("Run CLEAN RAM automatically at startup", _s.CleanAtStartup);
        _chkCleanStartup.Location = new Point(16, y); y += 26;
        _chkStartTray = Theme.CheckBox("Start minimized to the notification tray", _s.StartInTray);
        _chkStartTray.Location = new Point(16, y); y += 26;
        _chkAutoRekill = Theme.CheckBox("Auto re-kill apps that re-open themselves after being closed", _s.AutoRekill);
        _chkAutoRekill.Location = new Point(16, y); y += 34;

        var hdr4 = Theme.Label("Miner watchdog", Theme.Accent, 10f, bold: true);
        hdr4.Location = new Point(16, y); y += 28;

        _chkMinerScan = Theme.CheckBox("Background miner scan every", _s.MinerScan);
        _chkMinerScan.Location = new Point(16, y);
        _cmbMinerSec = NewCombo(new[] { "10", "20", "30", "60", "120" });
        _cmbMinerSec.Location = new Point(280, y - 3);
        _cmbMinerSec.Width = 60;
        _cmbMinerSec.SelectedIndex = BestIndex(_cmbMinerSec, _s.MinerIntervalSec.ToString());
        var lblSec = Theme.Label("seconds", Theme.TextDim);
        lblSec.Location = new Point(348, y);
        y += 34;

        _chkAutoKillMiners = Theme.CheckBox("Auto kill + blacklist detected miners without asking", _s.AutoKillMiners);
        _chkAutoKillMiners.Location = new Point(16, y); y += 34;

        var hdr5 = Theme.Label("Keep-list — apps Smart Clean must never close", Theme.Accent, 10f, bold: true);
        hdr5.Location = new Point(16, y); y += 26;

        _lstKeep = new ListBox
        {
            Location = new Point(16, y),
            Size = new Size(340, 110),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (var k in _s.KeepList) _lstKeep.Items.Add(k);

        _txtKeep = new TextBox
        {
            Location = new Point(16, y + 118),
            Width = 200,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var btnAdd = Theme.Button("Add", Theme.AccentDk, Theme.Text, 60, 26);
        btnAdd.Location = new Point(224, y + 117);
        btnAdd.Click += (_, _) =>
        {
            var n = BlacklistStore.Normalize(_txtKeep.Text);
            if (n.Length == 0) return;
            if (!_lstKeep.Items.Contains(n)) _lstKeep.Items.Add(n);
            _txtKeep.Text = "";
        };
        var btnDel = Theme.Button("Remove", Theme.Panel, Theme.Text, 80, 26);
        btnDel.Location = new Point(290, y + 117);
        btnDel.Click += (_, _) => { if (_lstKeep.SelectedIndex >= 0) _lstKeep.Items.RemoveAt(_lstKeep.SelectedIndex); };
        y += 156;

        _chkLogToFile = Theme.CheckBox("Write activity log to file (%ProgramData%\\RAMRazor\\logs)", _s.LogToFile);
        _chkLogToFile.Location = new Point(16, y); y += 40;

        var btnOk = Theme.Button("Save", Theme.Ok, Color.FromArgb(20, 30, 18), 120, 34);
        btnOk.Location = new Point(ClientSize.Width - 280, y);
        btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnOk.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };

        var btnCancel = Theme.Button("Cancel", Theme.Panel, Theme.Text, 100, 34);
        btnCancel.Location = new Point(ClientSize.Width - 140, y);
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        {
            hdr1, _cmbLevel, hdr2, _cmbScope, _chkRestartExplorer, _chkIncludeServices,
            hdr3, _chkAutoClean, _cmbAutoMin, lblMin, _cmbAutoThr,
            _chkCleanStartup, _chkStartTray, _chkAutoRekill,
            hdr4, _chkMinerScan, _cmbMinerSec, lblSec, _chkAutoKillMiners,
            hdr5, _lstKeep, _txtKeep, btnAdd, btnDel, _chkLogToFile, btnOk, btnCancel
        });
    }

    private static int BestIndex(ComboBox c, string value)
    {
        for (int i = 0; i < c.Items.Count; i++)
            if (string.Equals(c.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private static ComboBox NewCombo(string[] items)
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f)
        };
        c.Items.AddRange(items);
        return c;
    }

    private void Apply()
    {
        _s.CleanLevel = _cmbLevel.SelectedIndex switch { 0 => "Light", 2 => "Extreme", _ => "Deep" };
        _s.SmartScope = _cmbScope.SelectedIndex == 1 ? "Nuclear" : "Standard";
        _s.RestartExplorer = _chkRestartExplorer.Checked;
        _s.IncludeServices = _chkIncludeServices.Checked;
        _s.AutoClean = _chkAutoClean.Checked;
        _s.AutoCleanMinutes = int.TryParse(_cmbAutoMin.SelectedItem?.ToString(), out var m) ? m : 10;
        var thr = _cmbAutoThr.SelectedItem?.ToString() ?? "0";
        _s.AutoCleanThreshold = int.TryParse(thr.Split(' ')[0], out var t) ? t : 0;
        _s.CleanAtStartup = _chkCleanStartup.Checked;
        _s.StartInTray = _chkStartTray.Checked;
        _s.AutoRekill = _chkAutoRekill.Checked;
        _s.MinerScan = _chkMinerScan.Checked;
        _s.MinerIntervalSec = int.TryParse(_cmbMinerSec.SelectedItem?.ToString(), out var ms) ? ms : 20;
        _s.AutoKillMiners = _chkAutoKillMiners.Checked;
        _s.LogToFile = _chkLogToFile.Checked;
        _s.KeepList = _lstKeep.Items.Cast<object?>()
            .Select(x => x?.ToString() ?? "")
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList();

        _store.Save(_s);
        LogService.FileLogging = _s.LogToFile;
        LogService.Add("INFO", "SETTINGS", "settings saved");
    }
}
