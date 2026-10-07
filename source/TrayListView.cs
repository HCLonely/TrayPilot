using System.Runtime.InteropServices;

namespace TrayPilot;

internal sealed class TrayListView : ListView
{
    ListViewItem? hovered;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SortColumn { get; set; } = -1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SortDescending { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int[] SortableColumns { get; set; } = [0, 1, 2];
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
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
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
        var text = Rectangle.Inflate(e.Bounds, -10, 0);
        bool sortable = SortColumn >= 0 && SortableColumns.Contains(e.ColumnIndex);
        if (sortable) text.Width -= 16 * DeviceDpi / 96;
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, text, e.ColumnIndex == SortColumn ? UiTheme.Accent : UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (sortable)
        {
            float size = DeviceDpi / 96f, x = e.Bounds.Right - 16 * size, y = e.Bounds.Top + e.Bounds.Height / 2f;
            var saved = e.Graphics.Save(); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(e.ColumnIndex == SortColumn ? UiTheme.Accent : UiTheme.Muted, 1.5f * size) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            float direction = e.ColumnIndex == SortColumn && SortDescending ? 1 : -1;
            e.Graphics.DrawLines(pen, new PointF[] { new(x - 3 * size, y - 2 * size * direction), new(x, y + 2 * size * direction), new(x + 3 * size, y - 2 * size * direction) });
            e.Graphics.Restore(saved);
        }
    }
    Color Background(ListViewItem item) => item.Selected ? UiTheme.SelectedSurface : item == hovered ? UiTheme.Hover : View == View.Details && item.Index % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
    Color Foreground(ListViewItem item) => item.Tag is TrayEntry entry && entry.State == 1 ? UiTheme.Muted : UiTheme.Ink;
    void DrawSelectedSurface(Graphics graphics, Rectangle bounds, bool focused, int radius)
    {
        var saved = graphics.Save();
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = UiTheme.RoundedRectangle(bounds, radius);
        using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(bounds, UiTheme.SelectedSurface, UiTheme.SelectedSurfaceEnd, 90f);
        using var border = new Pen(focused ? UiTheme.Accent : UiTheme.SelectedOutline, (focused ? 1.5f : 1f) * DeviceDpi / 96);
        graphics.FillPath(fill, path); graphics.DrawPath(border, path);
        graphics.Restore(saved);
    }
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
            using var fill = new SolidBrush(Background(item)); using var border = new Pen(item == hovered ? UiTheme.Highlight : UiTheme.Border);
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = UiTheme.RoundedRectangle(bounds, 8 * DeviceDpi / 96))
            { if (!item.Selected) { graphics.FillPath(fill, path); graphics.DrawPath(border, path); } }
            graphics.SmoothingMode = smoothing;
            if (item.Selected) DrawSelectedSurface(graphics, bounds, item.Focused && Focused, 10 * DeviceDpi / 96);
            if (item.Selected)
                CheckBoxVisual.Draw(graphics, new Rectangle(bounds.Left + 4, bounds.Top + 4, 18 * DeviceDpi / 96, 18 * DeviceDpi / 96), CheckState.Checked, Enabled, selectedRow: true);
            if (LargeImageList != null && item.ImageIndex >= 0 && item.ImageIndex < LargeImageList.Images.Count)
            {
                var size = LargeImageList.ImageSize;
                int left = bounds.Left + (bounds.Width - size.Width) / 2;
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
            if (!item.Selected && item.Focused && Focused)
            {
                using var focusPen = new Pen(UiTheme.Accent, 1.5f * DeviceDpi / 96);
                using var focusPath = UiTheme.RoundedRectangle(Rectangle.Inflate(bounds, -2, -2), 8 * DeviceDpi / 96);
                graphics.DrawPath(focusPen, focusPath);
            }
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
        if (CheckBoxes && View == View.Details && message.Msg is 0x0201 or 0x0203)
        {
            long position = message.LParam.ToInt64();
            var point = new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff));
            var item = ItemAt(point);
            if (item != null)
            {
                int size = 18 * DeviceDpi / 96;
                var hit = new Rectangle(8 * DeviceDpi / 96, item.Bounds.Top + (item.Bounds.Height - size) / 2, size, size);
                hit.Inflate(4 * DeviceDpi / 96, 4 * DeviceDpi / 96);
                if (hit.Contains(point))
                {
                    // The native checkbox hit area is smaller than our painted box.
                    // Own this hit so its full visible area toggles once and never
                    // reaches the application's double-click hide/show action.
                    Focus(); item.Focused = true; item.Checked = !item.Checked;
                    InvalidateItem(item); message.Result = 0; return;
                }
            }
        }
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
        using var fill = new SolidBrush(e.Item.Selected ? UiTheme.Surface : Background(e.Item)); e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Item.Selected || e.Item.Focused && Focused)
        {
            // Draw the same inset row in each column's clip. Native incremental
            // subitem repainting must preserve both the selection and other text.
            var saved = e.Graphics.Save(); e.Graphics.IntersectClip(e.Bounds);
            var row = new Rectangle(e.Item.Bounds.Left + 5 * DeviceDpi / 96, e.Bounds.Top + 4 * DeviceDpi / 96,
                Math.Max(1, e.Item.Bounds.Width - 10 * DeviceDpi / 96), e.Bounds.Height - 8 * DeviceDpi / 96);
            if (e.Item.Selected) DrawSelectedSurface(e.Graphics, row, e.Item.Focused && Focused, 9 * DeviceDpi / 96);
            else
            {
                using var focus = UiTheme.RoundedRectangle(row, 9 * DeviceDpi / 96);
                using var pen = new Pen(UiTheme.Accent, 1.5f * DeviceDpi / 96); e.Graphics.DrawPath(pen, focus);
            }
            e.Graphics.Restore(saved);
        }
        var text = Rectangle.Inflate(e.Bounds, -8, 0);
        if (e.ColumnIndex == 0 && CheckBoxes)
        {
            var check = new Rectangle(text.Left, text.Top + (text.Height - 18 * DeviceDpi / 96) / 2, 18 * DeviceDpi / 96, 18 * DeviceDpi / 96);
            CheckBoxVisual.Draw(e.Graphics, check, e.Item.Checked ? CheckState.Checked : CheckState.Unchecked, Enabled, hovered == e.Item, selectedRow: e.Item.Selected);
            text.X += 24 * DeviceDpi / 96; text.Width -= 24 * DeviceDpi / 96;
        }
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
                SmallImageList.Draw(e.Graphics, text.Left, text.Top + (text.Height - size.Height) / 2, e.Item.ImageIndex);
            }
            text.X += size.Width + 8; text.Width -= size.Width + 8;
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, e.ColumnIndex == 4 && e.Item.Tag is TrayEntry ? UiTheme.Accent : Foreground(e.Item), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

internal sealed class TrayListItem(string text, bool matchesRule) : ListViewItem(text)
{
    internal bool MatchesRule { get; set; } = matchesRule;
    internal int RuleRank { get; set; }
}
