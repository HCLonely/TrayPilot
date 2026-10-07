using System.Drawing.Drawing2D;

namespace TrayPilot;

internal sealed class ThemeSwitch : CheckBox
{
    internal ThemeSwitch()
    {
        AutoCheck = false; AutoSize = false; Size = new(48, 30); TabStop = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int scale(int value) => value * DeviceDpi / 96;
        var track = new Rectangle(scale(3), (Height - scale(22)) / 2, Width - scale(6), scale(22));
        using var shape = UiTheme.RoundedRectangle(track, scale(11));
        using var fill = new SolidBrush(!Enabled ? UiTheme.Border : Checked ? UiTheme.Primary : UiTheme.Muted);
        e.Graphics.FillPath(fill, shape);
        int diameter = scale(16), x = Checked ? track.Right - diameter - scale(3) : track.Left + scale(3);
        using var knob = new SolidBrush(Enabled ? Color.White : UiTheme.Surface);
        e.Graphics.FillEllipse(knob, x, track.Top + scale(3), diameter, diameter);
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(UiTheme.Accent, 1.5f * DeviceDpi / 96);
            using var path = UiTheme.RoundedRectangle(Rectangle.Inflate(track, scale(2), scale(2)), scale(13)); e.Graphics.DrawPath(ring, path);
        }
    }
}
