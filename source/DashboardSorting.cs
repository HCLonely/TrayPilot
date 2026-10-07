using System.Collections;
using System.Globalization;

namespace TrayPilot;

internal sealed class DashboardItemComparer(int column, bool descending, string language) : IComparer
{
    readonly StringComparer names = StringComparer.Create(CultureInfo.GetCultureInfo(language), ignoreCase: true);
    public int Compare(object? x, object? y)
    {
        if (x is not TrayListItem { Tag: TrayEntry a } left || y is not TrayListItem { Tag: TrayEntry b } right) return 0;
        int result = column switch
        {
            1 => a.State.CompareTo(b.State),
            2 => left.RuleRank.CompareTo(right.RuleRank),
            _ => names.Compare(a.Name, b.Name)
        };
        if (result != 0) return descending ? -result : result;
        // Ties retain a deterministic name/identity order during periodic refresh.
        result = names.Compare(a.Name, b.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
}

internal sealed partial class MainForm
{
    int dashboardSortColumn;
    bool dashboardSortDescending;

    void SortDashboard(int column)
    {
        if (column is < 0 or > 2) return;
        dashboardSortDescending = dashboardSortColumn == column && !dashboardSortDescending;
        dashboardSortColumn = column;
        ApplyDashboardSort(); UpdateDashboard();
    }

    void ApplyDashboardSort()
    {
        var focused = (list.FocusedItem?.Tag as TrayEntry)?.Key ?? dashboardEntry?.Key;
        ((TrayListView)list).SortColumn = dashboardSortColumn;
        ((TrayListView)list).SortDescending = dashboardSortDescending;
        list.ListViewItemSorter = new DashboardItemComparer(dashboardSortColumn, dashboardSortDescending, L.Current);
        var item = list.Items.Cast<ListViewItem>().FirstOrDefault(x => (x.Tag as TrayEntry)?.Key == focused);
        if (item != null) item.Focused = true;
        list.Invalidate();
    }

    int DashboardRuleRank(TrayEntry entry) => !controller.HasRule(entry) ? 0 : controller.HasRule(entry.Path) ? 2 : 1;
}
