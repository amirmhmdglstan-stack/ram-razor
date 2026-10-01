namespace RAMRazor.UI;

/// <summary>
/// Selection dialog used by "Close selected…" and "Close all but selected…".
/// Lists every running, closable row (Apps or Tasks depending on the view)
/// with checkboxes; returns the checked keys via CheckedKeys.
/// </summary>
internal sealed class ProcessSelectForm : Form
{
    private readonly BufferedListView _list;
    private readonly Button _btnAction;
    private readonly Button _btnAll;
    private readonly Button _btnNone;
    private readonly Button _btnCancel;

    public HashSet<string> CheckedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ProcessSelectForm(List<SelectRow> rows, string title, string actionText, Color actionColor)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 620);
        BackColor = Theme.Bg;
        Font = new Font("Segoe UI", 9f);

        var hint = Theme.Label(
            "Tick the items you want to include, then press the action button.", Theme.TextDim);
        hint.Location = new Point(16, 14);

        _list = new BufferedListView
        {
            Location = new Point(16, 42),
            Size = new Size(848, 470),
            CheckBoxes = true
        };
        _list.Columns.Add("App / Task", 280);
        _list.Columns.Add("PID", 80);
        _list.Columns.Add("CPU %", 70);
        _list.Columns.Add("RAM", 90);
        _list.Columns.Add("Window", 70);
        _list.Columns.Add("Path", 300);

        foreach (var r in rows.OrderByDescending(x => x.MemBytes))
        {
            var item = new ListViewItem(new[]
            {
                r.Name, r.PidText, r.Cpu, r.Mem, r.Window,
                string.IsNullOrEmpty(r.Path) ? "(unknown)" : r.Path
            })
            { Tag = r.CheckKey };
            _list.Items.Add(item);
        }

        _btnAll = Theme.Button("Select all", Theme.Panel, Theme.Text, 120, 34);
        _btnAll.Location = new Point(16, 530);
        _btnAll.Click += (_, _) => SetAllChecks(true);

        _btnNone = Theme.Button("Select none", Theme.Panel, Theme.Text, 120, 34);
        _btnNone.Location = new Point(146, 530);
        _btnNone.Click += (_, _) => SetAllChecks(false);

        _btnAction = Theme.Button(actionText, actionColor, Color.White, 220, 40);
        _btnAction.Location = new Point(520, 566);
        _btnAction.Click += (_, _) =>
        {
            foreach (var it in _list.Items.Cast<ListViewItem>().Where(i => i.Checked))
                if (it.Tag is string k) CheckedKeys.Add(k);
            DialogResult = DialogResult.OK;
        };

        _btnCancel = Theme.Button("Cancel", Theme.Panel, Theme.TextDim, 110, 40);
        _btnCancel.Location = new Point(754, 566);
        _btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { hint, _list, _btnAll, _btnNone, _btnAction, _btnCancel });
        CancelButton = _btnCancel;
    }

    private void SetAllChecks(bool value)
    {
        foreach (var it in _list.Items.Cast<ListViewItem>())
            it.Checked = value;
    }
}
