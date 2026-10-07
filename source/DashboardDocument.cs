namespace TrayPilot;

// A single top-docked document gives scrolling an extent determined by its content.
internal sealed class DashboardDocument : Panel
{
    internal readonly TableLayoutPanel Content = new() { Dock = DockStyle.Top, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
    internal DashboardDocument()
    {
        Dock = DockStyle.Fill; AutoScroll = true; Margin = Padding.Empty;
        Content.ColumnStyles.Add(new(SizeType.Percent, 100)); Controls.Add(Content);
    }
    internal void Add(Control control)
    {
        int row = Content.RowCount++; Content.RowStyles.Add(new(SizeType.AutoSize));
        control.Dock = DockStyle.Top; control.Margin = new(0, 0, 0, 14); Content.Controls.Add(control, 0, row);
    }
}
