namespace TrayPilot;

internal static class InfoDialog
{
    internal static Form Create(string title, IEnumerable<KeyValuePair<string, string>> properties, Font font)
    {
        var dialog = new Form { Text = title, Size = new(820, 560), MinimumSize = new(600, 380),
            StartPosition = FormStartPosition.CenterParent, Font = font, Padding = new(14) };
        var info = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            GridLines = true, ShowItemToolTips = true, LabelEdit = false, HideSelection = false };
        info.Columns.Add(L.T("项目"), 220); info.Columns.Add(L.T("信息"), 515);
        foreach (var property in properties)
        {
            var row = new ListViewItem(property.Key) { ToolTipText = property.Key + ": " + property.Value };
            row.SubItems.Add(property.Value); info.Items.Add(row);
        }
        info.Resize += (_, _) => info.Columns[1].Width = Math.Max(300, info.ClientSize.Width - info.Columns[0].Width - 24);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new(0, 8, 0, 0) };
        var close = new Button { Text = L.T("关闭"), AutoSize = true, DialogResult = DialogResult.OK };
        var copy = new Button { Text = L.T("复制信息"), AutoSize = true };
        copy.Click += (_, _) =>
        {
            var rows = info.SelectedItems.Count > 0 ? info.SelectedItems.Cast<ListViewItem>() : info.Items.Cast<ListViewItem>();
            try { Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(x => x.Text + ": " + x.SubItems[1].Text))); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(dialog, L.T("剪贴板暂不可用，请重试。")); }
        };
        buttons.Controls.Add(close); buttons.Controls.Add(copy);
        dialog.Controls.Add(info); dialog.Controls.Add(buttons); dialog.AcceptButton = close; dialog.CancelButton = close;
        return dialog;
    }
}
