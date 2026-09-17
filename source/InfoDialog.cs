namespace TrayPilot;

internal static class InfoDialog
{
    internal static Form Create(string title, IEnumerable<KeyValuePair<string, string>> properties, Font font, bool about = false)
    {
        var dialog = new Form { Text = title, Size = new(880, 600), MinimumSize = new(600, 380),
            StartPosition = FormStartPosition.CenterParent, Font = font, Padding = new(24), BackColor = UiTheme.Canvas, ForeColor = UiTheme.Ink };
        var info = new SmoothListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            GridLines = true, ShowItemToolTips = true, LabelEdit = false, HideSelection = false };
        UiTheme.StyleList(info);
        info.Columns.Add(L.T("property"), 220); info.Columns.Add(L.T("information"), 515);
        foreach (var property in properties)
        {
            var row = new ListViewItem(property.Key) { ToolTipText = property.Key + ": " + property.Value, BackColor = info.Items.Count % 2 == 0 ? UiTheme.Surface : UiTheme.Stripe };
            row.SubItems.Add(property.Value); info.Items.Add(row);
        }
        info.Resize += (_, _) => info.Columns[1].Width = Math.Max(300, info.ClientSize.Width - info.Columns[0].Width - 24);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new(0, 16, 0, 0) };
        var close = UiTheme.Button(L.T("close")); close.DialogResult = DialogResult.OK;
        var copy = UiTheme.Button(L.T("copyInformation"));
        copy.Click += (_, _) =>
        {
            var rows = info.SelectedItems.Count > 0 ? info.SelectedItems.Cast<ListViewItem>() : info.Items.Cast<ListViewItem>();
            try { Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(x => x.Text + ": " + x.SubItems[1].Text))); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(dialog, L.T("clipboardUnavailableMessage")); }
        };
        buttons.Controls.Add(close); buttons.Controls.Add(copy);
        dialog.Controls.Add(info); dialog.Controls.Add(buttons); dialog.AcceptButton = close; dialog.CancelButton = close;
        var spacing = new ImageList { ImageSize = new(1, 32) }; info.SmallImageList = spacing;
        dialog.Disposed += (_, _) => spacing.Dispose();
        dialog.Controls.Add(UiTheme.Heading(about ? "TrayPilot" : title,
            about ? L.T("applicationDescription") : L.T("copyPropertiesHelp"), font));
        UiTheme.Apply(dialog); dialog.Shown += (_, _) => UiTheme.Apply(dialog);
        return dialog;
    }
}
