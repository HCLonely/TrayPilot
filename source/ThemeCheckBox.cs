using System.Drawing.Drawing2D;

namespace TrayPilot;

internal static class CheckBoxVisual
{
    internal static void Draw(Graphics graphics, Rectangle bounds, CheckState state, bool enabled = true, bool hovered = false, bool focused = false, bool selectedRow = false)
    {
        var saved = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = bounds.Width / 18f;
            bool marked = state != CheckState.Unchecked;
            var accent = UiTheme.Dark ? UiTheme.Accent : UiTheme.Primary;
            var fillColor = !enabled ? UiTheme.Header : marked ? accent : hovered ? UiTheme.Hover : UiTheme.Surface;
            var borderColor = !enabled ? UiTheme.Border : marked || hovered || selectedRow ? accent : UiTheme.Muted;
            var box = new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
            using var path = UiTheme.RoundedRectangle(box, Math.Max(2, (int)(4 * scale)));
            using var fill = new SolidBrush(fillColor); using var border = new Pen(borderColor, Math.Max(1, 1.3f * scale));
            graphics.FillPath(fill, path); graphics.DrawPath(border, path);
            if (marked)
            {
                using var mark = new Pen(!enabled ? UiTheme.Muted : UiTheme.Dark ? UiTheme.Canvas : Color.White, Math.Max(1.6f, 2 * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                PointF At(float x, float y) => new(bounds.X + x * scale, bounds.Y + y * scale);
                if (state == CheckState.Indeterminate) graphics.DrawLine(mark, At(5, 9), At(13, 9));
                else graphics.DrawLines(mark, new[] { At(4.5f, 9), At(7.5f, 12), At(13.5f, 6) });
            }
            if (focused)
            {
                using var ring = new Pen(UiTheme.Accent, Math.Max(1, scale));
                using var focus = UiTheme.RoundedRectangle(Rectangle.Inflate(bounds, 2, 2), Math.Max(3, (int)(5 * scale)));
                graphics.DrawPath(ring, focus);
            }
        }
        finally { graphics.Restore(saved); }
    }
}

// Preserve the standard CheckBox keyboard, tri-state and accessibility behavior.
internal sealed class ThemeCheckBox : CheckBox
{
    bool hovered;
    internal ThemeCheckBox() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int size = 18 * DeviceDpi / 96, gap = 8 * DeviceDpi / 96;
        var box = new Rectangle(2, (Height - size) / 2, size, size);
        CheckBoxVisual.Draw(e.Graphics, box, CheckState, Enabled, hovered, Focused && ShowFocusCues);
        var text = new Rectangle(box.Right + gap, 0, Math.Max(0, Width - box.Right - gap), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? ForeColor : UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new(text.Width + 30 * DeviceDpi / 96 + Padding.Horizontal, Math.Max(text.Height + 6 * DeviceDpi / 96, 24 * DeviceDpi / 96) + Padding.Vertical);
    }
}
