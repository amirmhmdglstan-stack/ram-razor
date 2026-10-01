using System.Drawing;
using System.Windows.Forms;

namespace RAMRazor.UI;

/// <summary>Dark theme palette + small factory helpers.</summary>
public static class Theme
{
    public static readonly Color Bg        = Color.FromArgb(24, 27, 33);
    public static readonly Color Panel     = Color.FromArgb(32, 36, 44);
    public static readonly Color PanelLine = Color.FromArgb(48, 54, 66);
    public static readonly Color Text      = Color.FromArgb(226, 230, 238);
    public static readonly Color TextDim   = Color.FromArgb(140, 148, 162);
    public static readonly Color Accent    = Color.FromArgb(76, 194, 255);
    public static readonly Color AccentDk  = Color.FromArgb(30, 90, 120);
    public static readonly Color Ok        = Color.FromArgb(155, 212, 107);
    public static readonly Color Warn      = Color.FromArgb(255, 196, 87);
    public static readonly Color Danger    = Color.FromArgb(255, 83, 112);
    public static readonly Color DangerDk  = Color.FromArgb(120, 40, 55);
    public static readonly Color RowAlt    = Color.FromArgb(28, 32, 39);

    public static Button Button(string text, Color back, Color fore, int w, int h)
    {
        var b = new Button
        {
            Text = text,
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Width = w,
            Height = h,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
        return b;
    }

    public static Label Label(string text, Color? fore = null, float size = 9f, bool bold = false)
        => new()
        {
            Text = text,
            ForeColor = fore ?? Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            AutoSize = true
        };

    public static CheckBox CheckBox(string text, bool @checked, Color? fore = null)
        => new()
        {
            Text = text,
            Checked = @checked,
            ForeColor = fore ?? Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9f)
        };
}

/// <summary>ListView with double buffering (no flicker on frequent rebuilds).</summary>
public sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
        BorderStyle = BorderStyle.None;
        FullRowSelect = true;
        HideSelection = false;
        View = View.Details;
        Font = new Font("Segoe UI", 9f);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
    }
}
