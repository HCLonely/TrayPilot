using System.Drawing.Drawing2D;

namespace TrayPilot;

internal static class UiTheme
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, Icon> FormIcons = new();
    internal static bool Dark { get; private set; }
    internal static Color Canvas => Dark ? Color.FromArgb(19, 23, 31) : Color.FromArgb(245, 247, 250);
    internal static Color Surface => Dark ? Color.FromArgb(27, 33, 44) : Color.White;
    internal static Color Ink => Dark ? Color.FromArgb(234, 239, 247) : Color.FromArgb(28, 39, 57);
    internal static Color Muted => Dark ? Color.FromArgb(164, 177, 198) : Color.FromArgb(94, 109, 131);
    internal static Color Accent => Dark ? Color.FromArgb(124, 165, 255) : Color.FromArgb(53, 92, 198);
    internal static Color Highlight => Color.FromArgb(42, 96, 204);
    internal static Color Selection => Dark ? Color.FromArgb(38, 77, 148) : Color.FromArgb(30, 72, 160);
    internal static Color SelectionBorder => Dark ? Color.FromArgb(156, 196, 255) : Color.FromArgb(16, 48, 115);
    internal static Color Border => Dark ? Color.FromArgb(62, 74, 94) : Color.FromArgb(221, 228, 239);
    internal static Color Header => Dark ? Color.FromArgb(39, 48, 63) : Color.FromArgb(239, 243, 249);
    internal static Color Stripe => Dark ? Color.FromArgb(34, 42, 55) : Color.FromArgb(249, 251, 254);
    internal static Color Hover => Dark ? Color.FromArgb(39, 51, 71) : Color.FromArgb(237, 243, 253);
    internal static Color Primary => Color.FromArgb(45, 85, 185);
    internal static Color PrimaryHover => Color.FromArgb(36, 71, 161);

    internal static bool Set(string mode)
    {
        bool dark = mode == "dark";
        if (mode != "light" && mode != "dark")
        {
            try { dark = (int?)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        bool changed = dark != Dark; Dark = dark; return changed;
    }

    internal static void Apply(Control control, bool surface = false)
    {
        surface |= control is SurfacePanel || control.Tag as string == "surface";
        bool muted = control.ForeColor == Color.FromArgb(94, 109, 131) || control.ForeColor == Color.FromArgb(164, 177, 198)
            || control.ForeColor == Color.FromArgb(106, 119, 139) || control.ForeColor == Color.FromArgb(156, 171, 193);
        control.ForeColor = control.ForeColor == Color.Firebrick ? Color.Firebrick : muted ? Muted : Ink;
        control.BackColor = surface ? Surface : Canvas;
        if (control is TextBox text) text.BackColor = text.ReadOnly ? Header : Surface;
        if (control is ComboBox) control.BackColor = Surface;
        if (control is ListView list)
        {
            list.BackColor = Surface;
            foreach (ListViewItem row in list.Items)
            {
                row.BackColor = list.View == View.Details && row.Index % 2 != 0 ? Stripe : Surface;
                row.ForeColor = row.Tag is TrayEntry entry && entry.State == 1 ? Muted : Ink;
            }
        }
        if (control is Button button)
        {
            bool primary = button is ThemeButton { Primary: true };
            button.BackColor = primary ? Primary : Surface; button.ForeColor = primary ? Color.White : Ink;
            button.FlatAppearance.BorderColor = primary ? Primary : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? PrimaryHover : Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? Selection : Header;
        }
        if (control is RadioButton radio)
        {
            radio.BackColor = radio.Checked ? Header : Surface; radio.ForeColor = radio.Checked ? Accent : Muted;
            radio.FlatAppearance.BorderColor = Border; radio.FlatAppearance.CheckedBackColor = Header;
        }
        if (control is ToolStrip strip)
        {
            strip.BackColor = Surface; strip.Renderer = new ThemeMenuRenderer();
            foreach (ToolStripItem item in strip.Items) item.ForeColor = Ink;
        }
        foreach (Control child in control.Controls) Apply(child, surface);
        if (control is Form form)
        {
            if (!FormIcons.TryGetValue(form, out var icon))
            {
                icon = AppIcon.Create(); FormIcons.Add(form, icon); var ownedIcon = icon;
                form.Disposed += (_, _) => ownedIcon.Dispose();
            }
            form.Icon = icon;
            if (form.IsHandleCreated) { int dark = Dark ? 1 : 0; Native.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); }
        }
        control.Invalidate(true);
    }

    internal static Button Button(string text, bool primary = false)
    {
        var button = new ThemeButton { Primary = primary, Text = text, AutoSize = true, MinimumSize = new(82, 36),
            Padding = new(12, 4, 12, 4), FlatStyle = FlatStyle.Flat, Margin = new(0, 0, 8, 0),
            BackColor = primary ? Primary : Surface, ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = primary ? Primary : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? PrimaryHover : Hover;
        return button;
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
        list.BorderStyle = BorderStyle.None; list.BackColor = Surface; list.ForeColor = Ink;
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
        void Update() { button.ForeColor = button.Checked ? Accent : Muted; button.BackColor = button.Checked ? Header : Surface; }
        button.CheckedChanged += (_, _) => Update(); Update();
    }
}

internal sealed class SurfacePanel : Panel
{
    internal SurfacePanel() { DoubleBuffered = true; BackColor = Color.Transparent; }
    internal bool FocusBorder { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 16 || Height < 16) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Canvas);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRectangle(bounds, 10 * DeviceDpi / 96);
        using var fill = new SolidBrush(UiTheme.Surface);
        using var border = new Pen(FocusBorder && ContainsFocus ? UiTheme.Accent : UiTheme.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}

// Native button semantics and keyboard activation, with immediate, static states.
internal sealed class ThemeButton : Button
{
    internal bool Primary { get; init; }
    bool hovered, pressed;
    internal ThemeButton() { DoubleBuffered = true; }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = Math.Max(1, DeviceDpi / 96);
        var bounds = new Rectangle(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
        using var path = UiTheme.RoundedRectangle(bounds, 7 * DeviceDpi / 96);
        var background = !Enabled ? UiTheme.Header : Primary
            ? pressed && (hovered || Focused) ? UiTheme.Selection : hovered ? UiTheme.PrimaryHover : UiTheme.Primary
            : pressed && (hovered || Focused) ? UiTheme.Header : hovered ? UiTheme.Hover : UiTheme.Surface;
        using var fill = new SolidBrush(background);
        using var border = new Pen(Focused && ShowFocusCues ? UiTheme.Accent : Primary && Enabled ? background : UiTheme.Border,
            Focused && ShowFocusCues ? 2 * DeviceDpi / 96f : 1);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(bounds, -Padding.Horizontal / 2, 0),
            !Enabled ? UiTheme.Muted : Primary ? Color.White : UiTheme.Ink, flags);
    }
}

internal sealed class SmoothListView : ListView
{
    internal SmoothListView() { DoubleBuffered = true; }
}

internal sealed class ThemeMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => UiTheme.Surface;
    public override Color ImageMarginGradientBegin => UiTheme.Surface;
    public override Color ImageMarginGradientMiddle => UiTheme.Surface;
    public override Color ImageMarginGradientEnd => UiTheme.Surface;
    public override Color MenuItemSelected => UiTheme.Highlight;
    public override Color MenuItemSelectedGradientBegin => UiTheme.Highlight;
    public override Color MenuItemSelectedGradientEnd => UiTheme.Highlight;
    public override Color MenuItemPressedGradientBegin => UiTheme.Header;
    public override Color MenuItemPressedGradientMiddle => UiTheme.Header;
    public override Color MenuItemPressedGradientEnd => UiTheme.Header;
    public override Color MenuBorder => UiTheme.Border;
    public override Color SeparatorDark => UiTheme.Border;
    public override Color SeparatorLight => UiTheme.Surface;
}
internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    internal ThemeMenuRenderer() : base(new ThemeMenuColors()) { }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        using var fill = new SolidBrush(e.Item.Selected ? UiTheme.Highlight : UiTheme.Header);
        e.Graphics.FillRectangle(fill, bounds);
        using var pen = new Pen(e.Item.Selected ? Color.White : UiTheme.Ink, 2);
        e.Graphics.DrawLines(pen, new[] { new Point(bounds.Left + 3, bounds.Top + bounds.Height / 2),
            new Point(bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - 4), new Point(bounds.Right - 3, bounds.Top + 3) });
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = UiTheme.Ink; base.OnRenderArrow(e); }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? UiTheme.Muted : e.Item.Selected ? Color.White : UiTheme.Ink;
        base.OnRenderItemText(e);
    }
}

internal sealed class ThemeComboBox : ComboBox
{
    internal ThemeComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (!IsHandleCreated || IsDisposed || Width < 2 || Height < 2) return;
        if (message.Msg != 0x000F && message.Msg != 0x0317 && message.Msg != 0x0318) return;
        using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle)
            : message.WParam != 0 ? Graphics.FromHdc(message.WParam) : null;
        if (graphics == null) return;
        using var pen = new Pen(Focused ? UiTheme.Accent : UiTheme.Border);
        graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? UiTheme.Highlight : UiTheme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -4, 0), selected ? Color.White : UiTheme.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
