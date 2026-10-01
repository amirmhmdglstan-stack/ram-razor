using RAMRazor.Core;

namespace RAMRazor.UI;

/// <summary>Manage the permanent blacklist: remove entries so apps can run again.</summary>
internal sealed class BlacklistForm : Form
{
    private readonly BlacklistStore _blacklist;
    private readonly BufferedListView _list;
    private readonly Button _btnRemove;
    private readonly Button _btnClear;
    private readonly Button _btnClose;
    private readonly Label _lblEmpty;

    public BlacklistForm(BlacklistStore blacklist)
    {
        _blacklist = blacklist;

        Text = "Blacklist — force-closed-forever apps";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 480);
        BackColor = Theme.Bg;
        Font = new Font("Segoe UI", 9f);

        var hint = Theme.Label(
            "Apps here are killed automatically whenever they start. Remove one to allow it to run again.",
            Theme.TextDim);
        hint.Location = new Point(16, 14);

        _list = new BufferedListView
        {
            Location = new Point(16, 42),
            Size = new Size(788, 340),
            MultiSelect = true
        };
        _list.Columns.Add("Process", 160);
        _list.Columns.Add("Executable", 380);
        _list.Columns.Add("Added", 130);
        _list.Columns.Add("Reason", 160);

        _btnRemove = Theme.Button("Remove selected\n(allow again)", Theme.AccentDk, Theme.Text, 160, 46);
        _btnRemove.Location = new Point(16, 400);
        _btnRemove.Click += (_, _) => RemoveSelected();

        _btnClear = Theme.Button("Clear all", Theme.DangerDk, Theme.Text, 120, 46);
        _btnClear.Location = new Point(186, 400);
        _btnClear.Click += (_, _) =>
        {
            if (_blacklist.Entries.Count == 0) return;
            var mb = MessageBox.Show("Remove ALL entries from the blacklist?", "Clear blacklist",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (mb == DialogResult.Yes)
            {
                _blacklist.Clear();
                LogService.Add("INFO", "BLACKLIST", "blacklist cleared — all apps allowed again");
                Reload();
            }
        };

        _btnClose = Theme.Button("Close", Theme.Panel, Theme.Text, 110, 46);
        _btnClose.Location = new Point(694, 400);
        _btnClose.Click += (_, _) => Close();

        _lblEmpty = Theme.Label("Blacklist is empty — nothing is force-closed forever right now.", Theme.TextDim);
        _lblEmpty.Location = new Point(320, 406);

        Controls.AddRange(new Control[] { hint, _list, _btnRemove, _btnClear, _btnClose, _lblEmpty });
        Reload();
    }

    private void Reload()
    {
        var entries = _blacklist.Entries;
        _list.Items.Clear();
        foreach (var e in entries)
        {
            _list.Items.Add(new ListViewItem(new[]
            {
                e.Name,
                string.IsNullOrEmpty(e.ExePath) ? "(unknown)" : e.ExePath,
                e.AddedAt.ToString("yyyy-MM-dd HH:mm"),
                string.IsNullOrEmpty(e.Reason) ? "-" : e.Reason
            }));
        }
        _lblEmpty.Visible = entries.Count == 0;
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var names = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.SubItems[0].Text).ToList();
        foreach (var n in names)
        {
            if (_blacklist.Remove(n))
                LogService.Add("INFO", "BLACKLIST", $"'{n}' removed from blacklist — allowed to run again");
        }
        Reload();
    }
}
