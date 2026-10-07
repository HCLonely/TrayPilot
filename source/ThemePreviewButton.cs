using System.Drawing.Drawing2D;

namespace TrayPilot;

internal sealed class ThemePreviewButton : Button
{
    internal readonly string Mode;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get; set; }
    internal ThemePreviewButton(string mode)
    {
        Mode = mode; AutoSize = false; Size = new(180, 144); Text = L.T(mode == "system" ? "followSystem" : mode == "light" ? "lightTheme" : "darkTheme");
        AccessibleName = Text; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => value * DeviceDpi / 96;
        var bounds = new Rectangle(2, 2, Width - 5, Height - 5);
        using var shape = UiTheme.RoundedRectangle(bounds, S(10));
        using var fill = new SolidBrush(Selected ? UiTheme.SelectedSurface : UiTheme.Surface);
        using var border = new Pen(Selected || Focused ? UiTheme.Accent : UiTheme.Border, (Focused ? 2 : 1) * DeviceDpi / 96);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
        var preview = new Rectangle(S(14), S(14), Width - S(28), Height - S(58));
        bool dark = Mode == "dark" || Mode == "system" && UiTheme.Dark;
        using var canvas = new SolidBrush(dark ? Color.FromArgb(16, 23, 36) : Color.FromArgb(233, 238, 247));
        using var surface = new SolidBrush(dark ? Color.FromArgb(37, 50, 71) : Color.White);
        using var blue = new SolidBrush(UiTheme.Primary);
        e.Graphics.FillRectangle(canvas, preview);
        e.Graphics.FillRectangle(surface, preview.X + S(4), preview.Y + S(4), S(24), preview.Height - S(8));
        for (int i = 0; i < 3; i++) e.Graphics.FillRectangle(surface, preview.X + S(35), preview.Y + S(7 + i * 22), Math.Max(1, preview.Width - S(42)), S(15));
        e.Graphics.FillRectangle(blue, preview.X + S(8), preview.Y + S(19), S(16), S(5));
        TextRenderer.DrawText(e.Graphics, (Selected ? "✓  " : "") + Text, Font, new Rectangle(S(10), Height - S(38), Width - S(20), S(26)), UiTheme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
