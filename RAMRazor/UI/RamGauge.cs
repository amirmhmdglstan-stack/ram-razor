using System.Drawing.Drawing2D;

namespace RAMRazor.UI;

/// <summary>Circular RAM usage gauge (custom-painted).</summary>
public sealed class RamGauge : Control
{
    private double _percent;

    public double UsagePercent
    {
        get => _percent;
        set
        {
            _percent = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public string CenterText { get; set; } = "--%";

    public RamGauge()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        int side = Math.Min(Width, Height) - 8;
        if (side < 20) return;
        var rect = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);

        using var track = new Pen(Theme.PanelLine, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(track, rect, 135, 270);

        Color color = _percent >= 85 ? Theme.Danger : _percent >= 65 ? Theme.Warn : Theme.Accent;
        using var value = new Pen(color, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var sweep = 270.0 * (_percent / 100.0);
        if (sweep < 0.5) sweep = 0.5;
        g.DrawArc(value, rect, 135, (float)sweep);

        var mid = new Rectangle(rect.X + 22, rect.Y + 22, rect.Width - 44, rect.Height - 44);
        string label = CenterText.Length > 0 ? CenterText : $"{_percent:0}%";

        using var f = new Font("Segoe UI", Math.Max(11f, side / 11f), FontStyle.Bold);
        var sz = g.MeasureString(label, f);
        using var tb = new SolidBrush(Theme.Text);
        g.DrawString(label, f, tb,
            rect.X + (rect.Width - sz.Width) / 2,
            rect.Y + (rect.Height - sz.Height) / 2);

        using var sf = new Font("Segoe UI", 7.5f);
        var sz2 = g.MeasureString("RAM USED", sf);
        using var tb2 = new SolidBrush(Theme.TextDim);
        g.DrawString("RAM USED", sf, tb2,
            rect.X + (rect.Width - sz2.Width) / 2,
            rect.Y + (rect.Height - sz2.Height) / 2 + sz.Height - 2);
    }
}
