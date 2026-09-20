using System.Runtime.InteropServices;

namespace TrayPilot;

internal sealed class TrayListView : ListView
{
    ListViewItem? hovered;
    internal ListViewItem? HoveredItem => hovered;
    internal void ClearHover() => SetHover(null);
    void InvalidateItem(ListViewItem? item)
    {
        if (item?.ListView != this) return;
        var bounds = View == View.LargeIcon ? Rectangle.Inflate(Cell(item), 1, 1)
            : new Rectangle(0, item.Bounds.Top, ClientSize.Width, item.Bounds.Height);
        Invalidate(bounds);
    }
    void SetHover(ListViewItem? item)
    {
        if (item == hovered) return;
        var previous = hovered; hovered = item;
        InvalidateItem(previous); InvalidateItem(item);
    }
    protected override void OnItemSelectionChanged(ListViewItemSelectionChangedEventArgs e)
    {
        base.OnItemSelectionChanged(e); InvalidateItem(e.Item);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); InvalidateItem(FocusedItem); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); InvalidateItem(FocusedItem); }
    internal TrayListView() { DoubleBuffered = true; OwnerDraw = true; }
    internal ListViewItem? ItemAt(Point point) => !ClientRectangle.Contains(point) ? null : View == View.LargeIcon
        ? Items.Cast<ListViewItem>().FirstOrDefault(x => Cell(x).Contains(point))
        : Items.Cast<ListViewItem>().FirstOrDefault(x => x.Bounds.Top <= point.Y && x.Bounds.Bottom > point.Y);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(ItemAt(e.Location));
    }
    protected override void OnMouseLeave(EventArgs e) { ClearHover(); base.OnMouseLeave(e); }
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var fill = new SolidBrush(UiTheme.Header); e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, Rectangle.Inflate(e.Bounds, -10, 0), UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    Color Background(ListViewItem item) => item.Selected ? UiTheme.Selection : item == hovered ? UiTheme.Highlight : View == View.Details && item.Index % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
    Color Foreground(ListViewItem item) => item.Selected || item == hovered ? Color.White : item.Tag is TrayEntry entry && entry.State == 1 ? UiTheme.Muted : UiTheme.Ink;
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
        graphics.IntersectClip(ClientRectangle);
        using var background = new SolidBrush(UiTheme.Surface);
        graphics.FillRectangle(background, ClientRectangle);
        foreach (ListViewItem item in Items)
        {
            var bounds = Cell(item);
            if (!bounds.IntersectsWith(ClientRectangle) || !graphics.IsVisible(Rectangle.Inflate(bounds, 1, 1))) continue;
            using var fill = new SolidBrush(Background(item)); using var border = new Pen(item.Selected ? UiTheme.SelectionBorder : item == hovered ? UiTheme.Highlight : UiTheme.Border, item.Selected ? 2 * DeviceDpi / 96f : 1);
            graphics.FillRectangle(fill, bounds); graphics.DrawRectangle(border, bounds);
            if (item.Selected)
                TextRenderer.DrawText(graphics, "✓", Font, new Rectangle(bounds.Left + 3, bounds.Top + 3, 20, 20), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
    [StructLayout(LayoutKind.Sequential)]
    struct PaintState
    {
        public nint Hdc;
        public int Erase, Left, Top, Right, Bottom, Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [DllImport("user32.dll")] static extern nint BeginPaint(nint window, out PaintState state);
    [DllImport("user32.dll")] static extern bool EndPaint(nint window, ref PaintState state);
    protected override void WndProc(ref Message message)
    {
        if (View == View.LargeIcon && message.Msg == 0x0014)
        {
            // The buffered paint includes the background; never expose an erased frame.
            message.Result = 1; return;
        }
        if (View == View.LargeIcon && message.Msg == 0x000F && IsHandleCreated)
        {
            var hdc = BeginPaint(Handle, out var paint);
            try
            {
                var dirty = Rectangle.FromLTRB(paint.Left, paint.Top, paint.Right, paint.Bottom);
                if (hdc != 0 && dirty.Width > 0 && dirty.Height > 0)
                {
                    using var target = Graphics.FromHdc(hdc);
                    // Keep the buffer origin at (0, 0) for GDI text and image-list drawing.
                    // BeginPaint clips the final blit to the actual invalid region.
                    using var buffer = BufferedGraphicsManager.Current.Allocate(target, ClientRectangle);
                    buffer.Graphics.SetClip(dirty);
                    DrawGrid(buffer.Graphics);
                    buffer.Render(target);
                }
            }
            finally { EndPaint(Handle, ref paint); }
            message.Result = 0; return;
        }
        base.WndProc(ref message);
        if (!IsHandleCreated || IsDisposed) return;
        if (message.Msg is 0x0114 or 0x0115 or 0x020A)
        {
            hovered = ItemAt(PointToClient(Cursor.Position)); Invalidate();
        }
        if (View == View.LargeIcon && message.Msg is 0x0317 or 0x0318 && message.WParam != 0)
        {
            using var graphics = Graphics.FromHdc(message.WParam); DrawGrid(graphics);
        }
    }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        using var fill = new SolidBrush(Background(e.Item)); e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Item.Selected && e.ColumnIndex == 0)
        {
            using var marker = new SolidBrush(UiTheme.SelectionBorder);
            e.Graphics.FillRectangle(marker, e.Bounds.Left, e.Bounds.Top, Math.Max(4, 4 * DeviceDpi / 96), e.Bounds.Height);
        }
        var text = Rectangle.Inflate(e.Bounds, -8, 0);
        if (e.ColumnIndex == 0 && SmallImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < SmallImageList.Images.Count)
        {
            var size = SmallImageList.ImageSize;
            if (e.Item.Tag is int)
            {
                using var attributes = new System.Drawing.Imaging.ImageAttributes();
                var color = Foreground(e.Item);
                attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix00 = color.R / 255f, Matrix11 = color.G / 255f, Matrix22 = color.B / 255f });
                using var icon = SmallImageList.Images[e.Item.ImageIndex];
                e.Graphics.DrawImage(icon, new Rectangle(text.Left, text.Top + (text.Height - size.Height) / 2, size.Width, size.Height),
                    0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
            }
            else
            {
                if (UiTheme.Dark) { using var plate = new SolidBrush(Color.FromArgb(225, 232, 243)); e.Graphics.FillRectangle(plate, text.Left, text.Top + (text.Height - size.Height) / 2, size.Width, size.Height); }
                SmallImageList.Draw(e.Graphics, text.Left, text.Top + (text.Height - size.Height) / 2, e.Item.ImageIndex);
            }
            text.X += size.Width + 8; text.Width -= size.Width + 8;
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, Foreground(e.Item), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (e.Item.Focused && Focused && e.ColumnIndex == 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1), Foreground(e.Item), Background(e.Item));
    }
}

internal sealed class TrayListItem(string text, bool matchesRule) : ListViewItem(text)
{
    internal bool MatchesRule { get; set; } = matchesRule;
}
