using System.Drawing.Drawing2D;

namespace TrayPilot;

internal static class UiTheme
{
    internal static readonly Color Canvas = Color.FromArgb(243, 246, 251);
    internal static readonly Color Ink = Color.FromArgb(31, 44, 67);
    internal static readonly Color Muted = Color.FromArgb(106, 119, 139);
    internal static readonly Color Accent = Color.FromArgb(53, 92, 198);
    internal static readonly Color Border = Color.FromArgb(221, 228, 239);
    internal static readonly Color Header = Color.FromArgb(239, 243, 249);
    internal static readonly Color Stripe = Color.FromArgb(249, 251, 254);

    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new(82, 36),
            Padding = new(12, 4, 12, 4), FlatStyle = FlatStyle.Flat, Margin = new(0, 0, 8, 0),
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(42, 77, 174) : Header;
        return button;
    }

    internal static Panel Heading(string title, string subtitle, Font font)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 92 };
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, ForeColor = Muted });
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 48, Font = new(font.FontFamily, 21, FontStyle.Bold), ForeColor = Ink });
        return panel;
    }

    internal static void StyleList(ListView list)
    {
        list.BorderStyle = BorderStyle.None; list.BackColor = Color.White; list.ForeColor = Ink;
        list.GridLines = false; list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Header); e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text, list.Font, Rectangle.Inflate(e.Bounds, -10, 0), Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
    }

    internal static void Toggle(RadioButton button)
    {
        button.FlatStyle = FlatStyle.Flat; button.Padding = new(10, 5, 10, 5);
        button.Margin = Padding.Empty; button.MinimumSize = new(60, 36);
        button.TextAlign = ContentAlignment.MiddleCenter; button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = Border; button.FlatAppearance.CheckedBackColor = Color.FromArgb(225, 234, 254);
        void Update() { button.ForeColor = button.Checked ? Accent : Muted; button.BackColor = button.Checked ? Color.FromArgb(225, 234, 254) : Color.White; }
        button.CheckedChanged += (_, _) => Update(); Update();
    }
}

internal sealed class SurfacePanel : Panel
{
    internal SurfacePanel() { DoubleBuffered = true; BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 16 || Height < 16) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        const int diameter = 16;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(Color.White); using var border = new Pen(UiTheme.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}

internal sealed class SmoothListView : ListView
{
    internal SmoothListView() { DoubleBuffered = true; }
}
