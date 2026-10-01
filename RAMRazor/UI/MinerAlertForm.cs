using RAMRazor.Core;

namespace RAMRazor.UI;

/// <summary>
/// Notification card (bottom-right, non-modal) for suspected background miners,
/// apps that could not be closed, and apps that re-opened themselves.
/// Each entry offers "Force close (forever)" and "Force delete".
/// </summary>
internal sealed class MinerAlertForm : Form
{
    private List<AlertItem> _items;
    private readonly FlowLayoutPanel _panel;
    private readonly Func<AlertItem, string> _onForceForever;
    private readonly Func<AlertItem, string> _onForceDelete;
    private readonly Action<AlertItem> _onDismiss;

    public MinerAlertForm(List<AlertItem> items,
        Func<AlertItem, string> onForceForever,
        Func<AlertItem, string> onForceDelete,
        Action<AlertItem> onDismiss)
    {
        _items = items;
        _onForceForever = onForceForever;
        _onForceDelete = onForceDelete;
        _onDismiss = onDismiss;

        Text = "RAM Razor — warning";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(480, 10);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        TopMost = true;

        _panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Panel,
            Padding = new Padding(8)
        };
        Controls.Add(_panel);
        UpdateItems(items);
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1000, 700);
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
    }

    public void UpdateItems(List<AlertItem> items)
    {
        _items = items;
        _panel.Controls.Clear();
        _panel.SuspendLayout();

        var header = new Label
        {
            Text = "⚠  Suspicious / stubborn apps detected",
            ForeColor = Theme.Warn,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 4, 2, 8)
        };
        _panel.Controls.Add(header);

        var sub = new Label
        {
            Text = "These apps could not be closed normally, re-opened themselves, " +
                   "or look like background miners.",
            ForeColor = Theme.TextDim,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 0, 2, 8)
        };
        _panel.Controls.Add(sub);

        foreach (var item in _items)
            _panel.Controls.Add(BuildCard(item));

        if (_items.Count == 0)
        {
            _panel.Controls.Add(new Label
            {
                Text = "All clear.",
                ForeColor = Theme.Ok,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(2, 10, 2, 10)
            });
        }

        _panel.ResumeLayout();
        ClientSize = new Size(480, Math.Min(560, _panel.PreferredSize.Height + 16));
        PositionBottomRight();
    }

    private Panel BuildCard(AlertItem item)
    {
        var card = new Panel
        {
            Size = new Size(456, 108),
            BackColor = Theme.Bg,
            Margin = new Padding(2, 4, 2, 4)
        };
        card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(Theme.PanelLine), 0, 0, card.Width - 1, card.Height - 1);

        var icon = item.Source switch
        {
            "miner" => "⛏",
            "reopen" => "↻",
            _ => "⛔"
        };

        var title = new Label
        {
            Text = $"{icon}  {item.Name}",
            ForeColor = item.Source == "miner" ? Theme.Warn : Theme.Danger,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location = new Point(10, 8),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var detail = new Label
        {
            Text = item.Detail.Length > 160 ? item.Detail[..160] + "…" : item.Detail,
            ForeColor = Theme.TextDim,
            Location = new Point(12, 32),
            Size = new Size(432, 32),
            BackColor = Color.Transparent
        };

        var btnForever = Theme.Button("Force close (forever)", Theme.DangerDk, Theme.Text, 150, 30);
        btnForever.Location = new Point(10, 70);
        btnForever.Click += (_, _) =>
        {
            var status = _onForceForever(item);
            MessageBox.Show($"'{item.Name}': {status}", "Force close (forever)",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Remove(item);
        };

        var btnDelete = Theme.Button("Force delete", Theme.Danger, Color.White, 110, 30);
        btnDelete.Location = new Point(168, 70);
        btnDelete.Click += (_, _) =>
        {
            var status = _onForceDelete(item);
            Remove(item);
        };

        var btnIgnore = Theme.Button("Ignore", Theme.Panel, Theme.TextDim, 80, 30);
        btnIgnore.Location = new Point(286, 70);
        btnIgnore.Click += (_, _) => Remove(item);

        card.Controls.AddRange(new Control[] { title, detail, btnForever, btnDelete, btnIgnore });
        return card;
    }

    private void Remove(AlertItem item)
    {
        _items.Remove(item);
        _onDismiss(item);
        UpdateItems(_items);
        if (_items.Count == 0)
        {
            Close();
            Dispose();
        }
    }
}
