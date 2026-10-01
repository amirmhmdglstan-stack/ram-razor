using System.Drawing;
using System.Windows.Forms;

namespace RAMRazor.UI;

/// <summary>Small scrolling line chart of RAM usage history (last ~6 minutes).</summary>
public sealed class RamGraph : Control
{
    private const int MaxSamples = 120;
    private readonly List<double> _samples = new();

    public RamGraph()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 7f);
    }

    public void AddSample(double percent)
    {
        _samples.Add(Math.Clamp(percent, 0, 100));
        if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Theme.Panel);

        using var penGrid = new Pen(Theme.PanelLine, 1f);
        for (int i = 1; i < 4; i++)
        {
            int y = Height * i / 4;
            g.DrawLine(penGrid, 0, y, Width, y);
        }

        if (_samples.Count > 1)
        {
            var pts = new PointF[_samples.Count];
            float step = Width / (float)(MaxSamples - 1);
            for (int i = 0; i < _samples.Count; i++)
                pts[i] = new PointF(
                    Width - (_samples.Count - 1 - i) * step,
                    Height - 2 - (float)(_samples[i] / 100.0) * (Height - 8));

            // area under the curve
            var area = new PointF[pts.Length + 2];
            area[0] = new PointF(pts[0].X, Height);
            pts.CopyTo(area, 1);
            area[^1] = new PointF(pts[^1].X, Height);
            using (var fill = new SolidBrush(Color.FromArgb(40, Theme.Accent)))
                g.FillPolygon(fill, area);

            using var penLine = new Pen(Theme.Accent, 1.6f);
            g.DrawLines(penLine, pts);

            var last = pts[^1];
            using var dot = new SolidBrush(Theme.Accent);
            g.FillEllipse(dot, last.X - 2.5f, last.Y - 2.5f, 5, 5);
        }

        double peak = _samples.Count == 0 ? 0 : _samples.Max();
        using var tb = new SolidBrush(Theme.TextDim);
        g.DrawString($"RAM history · peak {peak:0}%", Font, tb, 4, 2);

        using var border = new Pen(Theme.PanelLine, 1f);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
