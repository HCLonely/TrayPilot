using System.Collections;
using System.Globalization;

namespace TrayPilot;

internal sealed class RuleListItem(string name, int stateRank) : ListViewItem(name)
{
    internal int StateRank { get; } = stateRank;
}

internal sealed class RuleItemComparer(int column, bool descending, string language) : IComparer
{
    readonly StringComparer names = StringComparer.Create(CultureInfo.GetCultureInfo(language), ignoreCase: true);
    public int Compare(object? x, object? y)
    {
        if (x is not RuleListItem { Tag: RuleReference a } left || y is not RuleListItem { Tag: RuleReference b } right) return 0;
        int result = column == 2 ? left.StateRank.CompareTo(right.StateRank) : names.Compare(left.Text, right.Text);
        if (result != 0) return descending ? -result : result;
        result = names.Compare(left.Text, right.Text);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.Key, b.Key);
    }
}
