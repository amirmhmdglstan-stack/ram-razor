using RAMRazor.Core;

namespace RAMRazor.UI;

/// <summary>Live viewer for the activity log (closed / failed / re-opened / cleans / miners).</summary>
internal sealed class LogForm : Form
{
    private readonly BufferedListView _list;
    private bool _live = true;

    public LogForm()
    {
        Text = "Activity log — everything RAM Razor closed (and everything it couldn't)";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 560);
        BackColor = Theme.Bg;
        Font = new Font("Segoe UI", 9f);

        _list = new BufferedListView
        {
            Location = new Point(16, 16),
            Size = new Size(948, 460),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _list.Columns.Add("Time", 130);
        _list.Columns.Add("Level", 55);
        _list.Columns.Add("Category", 95);
        _list.Columns.Add("Message", 700);

        var btnFolder = Theme.Button("Open log folder", Theme.Panel, Theme.Text, 140, 36);
        btnFolder.Location = new Point(16, 496);
        btnFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnFolder.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", LogService.LogDirectory); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "RAM Razor"); }
        };

        var btnLive = Theme.Button("Pause live", Theme.Panel, Theme.Text, 120, 36);
        btnLive.Location = new Point(166, 496);
        btnLive.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnLive.Click += (_, _) =>
        {
            _live = !_live;
            btnLive.Text = _live ? "Pause live" : "Resume live";
        };

        var btnClose = Theme.Button("Close", Theme.Panel, Theme.Text, 100, 36);
        btnClose.Location = new Point(864, 496);
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _list, btnFolder, btnLive, btnClose });

        Load += (_, _) =>
        {
            foreach (var line in LogService.Recent()) AppendLine(line);
            LogService.EntryAdded += OnEntry;
        };
        FormClosed += (_, _) => LogService.EntryAdded -= OnEntry;
    }

    private void OnEntry(string level, string category, string message)
    {
        if (!_live) return;
        try { BeginInvoke(() => AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{level}\t{category}\t{message}")); }
        catch { }
    }

    private void AppendLine(string line)
    {
        var parts = line.Split('\t');
        Color color = parts.Length >= 2
            ? parts[1] switch
            {
                "ERR" => Theme.Danger,
                "WARN" => Theme.Warn,
                _ => Theme.Text
            }
            : Theme.Text;
        var item = new ListViewItem(parts.Length >= 4 ? new[] { parts[0], parts[1], parts[2], parts[3] }
                                                   : new[] { line, "", "", "" })
        {
            ForeColor = color
        };
        _list.Items.Add(item);
        if (_list.Items.Count > 800) _list.Items.RemoveAt(0);
        item.EnsureVisible();
    }
}
