namespace TrayPilot;

internal sealed class TrayListView : ListView
{
    ListViewItem? hovered;
    internal ListViewItem? HoveredItem => hovered;
    internal void ClearHover() { hovered = null; Invalidate(); }
    internal TrayListView() { DoubleBuffered = true; OwnerDraw = true; }
    internal ListViewItem? ItemAt(Point point) => !ClientRectangle.Contains(point) ? null : View == View.LargeIcon
        ? Items.Cast<ListViewItem>().FirstOrDefault(x => Cell(x).Contains(point))
        : Items.Cast<ListViewItem>().FirstOrDefault(x => x.Bounds.Top <= point.Y && x.Bounds.Bottom > point.Y);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var item = ItemAt(e.Location);
        if (item == hovered) return;
        hovered = item; Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { hovered = null; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var fill = new SolidBrush(UiTheme.Header); e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, Rectangle.Inflate(e.Bounds, -10, 0), UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    Color Background(ListViewItem item) => item == hovered ? UiTheme.Highlight : item.Selected ? UiTheme.Header : View == View.Details && item.Index % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
    Color Foreground(ListViewItem item) => item == hovered ? Color.White : item.Tag is TrayEntry entry && entry.State == 1 ? UiTheme.Muted : UiTheme.Ink;
    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        if (e.Item == null) return;
        // Native list-view can repaint just one subitem. Painting the entire row here
        // erases text in columns that do not receive a DrawSubItem notification.
        // Each subitem paints its own complete background and text instead.
    }
    Rectangle Cell(ListViewItem item)
    {
        var bounds = item.Bounds; bounds.Height = 100 * DeviceDpi / 96;
        bounds.Inflate(-4 * DeviceDpi / 96, -3 * DeviceDpi / 96); return bounds;
    }
    void DrawGrid(Graphics graphics)
    {
        graphics.SetClip(ClientRectangle); graphics.Clear(UiTheme.Surface);
        foreach (ListViewItem item in Items)
        {
            var bounds = Cell(item);
            if (!bounds.IntersectsWith(ClientRectangle)) continue;
            using var fill = new SolidBrush(Background(item)); using var border = new Pen(item == hovered ? UiTheme.Highlight : item.Selected ? UiTheme.Header : UiTheme.Border);
            graphics.FillRectangle(fill, bounds); graphics.DrawRectangle(border, bounds);
            if (LargeImageList != null && item.ImageIndex >= 0 && item.ImageIndex < LargeImageList.Images.Count)
            {
                var size = LargeImageList.ImageSize;
                int left = bounds.Left + (bounds.Width - size.Width) / 2;
                if (UiTheme.Dark) { using var plate = new SolidBrush(Color.FromArgb(225, 232, 243)); graphics.FillRectangle(plate, left - 2, bounds.Top + 4, size.Width + 4, size.Height + 4); }
                LargeImageList.Draw(graphics, left, bounds.Top + 6, item.ImageIndex);
                var text = new Rectangle(bounds.Left + 3, bounds.Top + size.Height + 10, bounds.Width - 6, Math.Max(1, bounds.Height - size.Height - 12));
                TextRenderer.DrawText(graphics, item.Text, Font, text, Foreground(item), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (item is TrayListItem { MatchesRule: true })
            {
                var badge = new Rectangle(bounds.Right - 19, bounds.Top + 3, 16, 16);
                using var marker = new SolidBrush(UiTheme.Highlight); graphics.FillEllipse(marker, badge);
                TextRenderer.DrawText(graphics, "✓", Font, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (item.Focused && Focused) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -2, -2), Foreground(item), Background(item));
        }
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (!IsHandleCreated || IsDisposed) return;
        if (message.Msg is 0x0114 or 0x0115 or 0x020A)
        {
            hovered = ItemAt(PointToClient(Cursor.Position)); Invalidate();
        }
        if (View != View.LargeIcon) return;
        if (message.Msg == 0x000F)
        {
            using var graphics = Graphics.FromHwnd(Handle); DrawGrid(graphics);
        }
        else if (message.Msg is 0x0317 or 0x0318 && message.WParam != 0)
        {
            using var graphics = Graphics.FromHdc(message.WParam); DrawGrid(graphics);
        }
    }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        using var fill = new SolidBrush(Background(e.Item)); e.Graphics.FillRectangle(fill, e.Bounds);
        var text = Rectangle.Inflate(e.Bounds, -8, 0);
        if (e.ColumnIndex == 0 && SmallImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < SmallImageList.Images.Count)
        {
            var size = SmallImageList.ImageSize;
            if (UiTheme.Dark) { using var plate = new SolidBrush(Color.FromArgb(225, 232, 243)); e.Graphics.FillRectangle(plate, text.Left, text.Top + (text.Height - size.Height) / 2, size.Width, size.Height); }
            SmallImageList.Draw(e.Graphics, text.Left, text.Top + (text.Height - size.Height) / 2, e.Item.ImageIndex);
            text.X += size.Width + 8; text.Width -= size.Width + 8;
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, Foreground(e.Item), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (e.Item.Focused && Focused && e.ColumnIndex == 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1), Foreground(e.Item), Background(e.Item));
    }
}

internal sealed class TrayListItem(string text, bool matchesRule) : ListViewItem(text)
{
    internal bool MatchesRule { get; } = matchesRule;
}
