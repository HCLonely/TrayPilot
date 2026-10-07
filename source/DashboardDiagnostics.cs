using System.Reflection;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static readonly BindingFlags DashboardFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T DashboardField<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, DashboardFlags)!.GetValue(form)!;
    static void DashboardCall(MainForm form, string name) => typeof(MainForm).GetMethod(name, DashboardFlags)!.Invoke(form, null);

    // This preview uses a read-only scanner and an isolated settings directory.
    // It never initializes the tray watcher, registers hotkeys or applies rules.
    static int PreviewDashboard(string output, bool dark, bool grid, bool small, bool english)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "dashboard-preview-state"));
        controller.Saved.Language = english ? "en-US" : "zh-CN"; controller.Saved.Theme = dark ? "dark" : "light";
        using var form = new MainForm(controller, initialize: false);
        if (small) form.Size = form.MinimumSize;
        var snapshot = Scanner.Scan().Where(x => x.Pid != Environment.ProcessId).ToList();
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, snapshot);
        DashboardCall(form, "RenderList");
        if (grid) DashboardField<RadioButton>(form, "layoutMode").Checked = true;
        var previewList = DashboardField<ListView>(form, "list");
        var selected = previewList.Items.Cast<ListViewItem>().FirstOrDefault(x => x.Tag is TrayEntry { State: 0 } entry && !Scanner.IsShellEntry(entry));
        if (selected != null) selected.Selected = true;
        DashboardCall(form, "UpdateStatus");
        using var capture = new System.Windows.Forms.Timer { Interval = 600 };
        capture.Tick += (_, _) =>
        {
            capture.Stop(); form.PerformLayout();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(output); form.Close();
        };
        form.Shown += (_, _) => capture.Start(); Application.Run(form); return 0;
    }

    static int TestDashboard(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var checks = new List<string>();
        void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); checks.Add(description); }
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "dashboard-test-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Theme = "light"; controller.Saved.Language = "zh-CN";
        using var form = new MainForm(controller, initialize: false);
        var snapshot = new List<TrayEntry>
        {
            new() { Name = "Visible application", Path = Environment.ProcessPath!, Guid = Guid.NewGuid(), State = 0 },
            new() { Name = "Hidden application", Path = Environment.ProcessPath!, Guid = Guid.NewGuid(), State = 1 }
        };
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, snapshot);
        DashboardCall(form, "RenderList");
        var list = DashboardField<ListView>(form, "list");
        Check(list.Items.Count == 2, "All filter includes both visibility states.");
        var filters = DashboardField<List<RadioButton>>(form, "dashboardFilters"); filters[1].Checked = true;
        Check(list.Items.Count == 1 && ((TrayEntry)list.Items[0].Tag!).State == 0, "Visible filter uses actual tray state.");
        filters[2].Checked = true;
        Check(list.Items.Count == 1 && ((TrayEntry)list.Items[0].Tag!).State == 1, "Hidden filter uses actual tray state.");
        snapshot[1] = snapshot[1] with { State = 0 }; DashboardCall(form, "RenderList");
        Check(list.Items.Count == 0, "A state change updates filtered membership.");
        filters[0].Checked = true;
        var selectedEntry = list.Items.Cast<ListViewItem>().Single(i => ((TrayEntry)i.Tag!).Key == snapshot[0].Key);
        selectedEntry.Selected = true; DashboardCall(form, "UpdateDashboard");
        Check(DashboardField<TrayEntry>(form, "dashboardEntry").Key == snapshot[0].Key, "Details use the selected actual entry.");
        Check(DashboardField<PictureBox>(form, "dashboardProgramIcon").Image != null, "Details render a program icon rather than initials.");
        list.Items.Cast<ListViewItem>().Single(i => i != selectedEntry).Checked = true; Check(list.SelectedItems.Count == 2, "Checkbox selection participates in batch actions.");
        var all = DashboardField<CheckBox>(form, "dashboardSelectAll"); all.Checked = false;
        Check(list.SelectedItems.Count == 0 && list.Items.Cast<ListViewItem>().All(i => !i.Checked), "Select-all can clear all checked selections.");
        all.Checked = true; Check(list.SelectedItems.Count == 2, "Select-all includes every filtered row.");
        var search = DashboardField<TextBox>(form, "search"); search.Text = "no such application";
        Check(list.Items.Count == 0 && DashboardField<Label>(form, "dashboardEmpty").Text == L.T("dashboardNoResults"),
            "Unmatched search shows a useful empty-result message.");
        search.Clear(); Check(list.Items.Count == 2, "Clearing search restores rows.");
        controller.Saved.Theme = "dark"; DashboardCall(form, "ApplyTheme"); Check(UiTheme.Dark && list.BackColor == UiTheme.Surface, "Theme applies to the live list.");
        var shell = DashboardField<MenuStrip>(form, "menuBar"); Check(!shell.Visible, "Sidebar replaces the legacy top navigation.");
        Check(controller.Saved.HiddenPaths.Count == 0 && controller.Saved.HiddenIcons.Count == 0, "Presentation tests do not change hiding rules.");
        Check(all is ThemeCheckBox && DashboardField<CheckBox>(form, "autoRefresh") is ThemeCheckBox, "Select-all and auto-refresh use themed checkboxes with native semantics.");

        var sortingEntries = new List<TrayEntry>
        {
            new() { Name = "Zulu", Path = @"C:\DashboardTests\Zulu.exe", Guid = Guid.NewGuid(), State = 1 },
            new() { Name = "alpha", Path = @"C:\DashboardTests\Alpha.exe", Guid = Guid.NewGuid(), State = 0 },
            new() { Name = "Bravo", Path = @"C:\DashboardTests\Bravo.exe", Guid = Guid.NewGuid(), State = 1 }
        };
        controller.Saved.HiddenPaths.Add(sortingEntries[1].Path);
        controller.Saved.HiddenIcons.Add(IconRule.From(sortingEntries[2]));
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(form, sortingEntries); DashboardCall(form, "RenderList");
        string Order() => string.Join(",", list.Items.Cast<ListViewItem>().Select(i => ((TrayEntry)i.Tag!).Name));
        void Header(int column) => typeof(ListView).GetMethod("OnColumnClick", DashboardFlags)!.Invoke(list, new object[] { new ColumnClickEventArgs(column) });
        Check(Order() == "alpha,Bravo,Zulu", "Default name sorting ignores letter case.");
        var alpha = list.Items[0]; alpha.Selected = true; alpha.Focused = true; DashboardCall(form, "UpdateDashboard");
        Header(0); Check(Order() == "Zulu,Bravo,alpha", "Name header switches to descending order.");
        Check(alpha.Selected && alpha.Checked && DashboardField<TrayEntry>(form, "dashboardEntry").Key == sortingEntries[1].Key,
            "Sorting preserves checked selection and the current program details.");
        Header(0); Check(Order() == "alpha,Bravo,Zulu", "Name header switches back to ascending order.");
        Header(1); Check(Order() == "alpha,Bravo,Zulu", "State ascending places visible icons first with stable name ties.");
        Header(1); Check(Order() == "Bravo,Zulu,alpha", "State descending places hidden icons first.");
        Header(2); Check(Order() == "Zulu,Bravo,alpha", "Rule ascending orders no rule, single-icon rule, then program rule.");
        Header(2); Check(Order() == "alpha,Bravo,Zulu", "Rule descending reverses rule scopes.");
        controller.Saved.HiddenPaths.Add(sortingEntries[0].Path); DashboardCall(form, "RenderList");
        Check(Order() == "alpha,Zulu,Bravo" && ((TrayListView)list).SortDescending, "A rule change re-sorts incrementally and preserves the sort direction.");
        var unchangedImages = list.SmallImageList; DashboardCall(form, "RenderList");
        Check(ReferenceEquals(unchangedImages, list.SmallImageList), "Unchanged sorted snapshots retain their image list.");
        filters[1].Checked = true; Check(Order() == "alpha", "Sorting combines with visibility filters.");
        sortingEntries[0] = sortingEntries[0] with { State = 0 }; DashboardCall(form, "RenderList");
        Check(Order() == "alpha,Zulu", "Refresh keeps both filtered membership and rule order correct.");
        filters[0].Checked = true;
        var modernList = (TrayListView)list;
        var clicked = list.Items[0]; clicked.Checked = false;
        int px = 20 * list.DeviceDpi / 96, py = clicked.Bounds.Top + clicked.Bounds.Height / 2;
        Native.SendMessageW(list.Handle, 0x0201, 1, (nint)((py << 16) | px));
        Check(clicked.Checked && clicked.Selected, "The outer edge of the painted checkbox toggles its actual checked state.");
        Native.SendMessageW(list.Handle, 0x0203, 1, (nint)((py << 16) | px));
        Check(!clicked.Checked, "Checkbox double-clicks toggle selection without hiding the program.");
        controller.Saved.HiddenPaths.Clear(); controller.Saved.HiddenIcons.Clear();
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
