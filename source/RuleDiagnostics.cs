using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int PreviewRules(string output, bool dark, bool editor, bool english, bool stateSort = false, bool small = false)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "rules-preview-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Language = english ? "en-US" : "zh-CN"; controller.Saved.Theme = dark ? "dark" : "light";
        L.Set(controller.Saved.Language); UiTheme.Set(controller.Saved.Theme);
        var entries = Scanner.Scan().Where(e => e.Pid != Environment.ProcessId).ToList();
        var samples = entries.Where(e => !Scanner.IsShellEntry(e)).GroupBy(e => Controller.NormalizePath(e.Path)).Select(g => g.First()).Take(3).ToList();
        if (samples.Count > 0) controller.Saved.HiddenPaths.Add(samples[0].Path);
        if (samples.Count > 1) { controller.Saved.HiddenIcons.Add(IconRule.From(samples[1])); controller.SetManualVisibility(samples[1], false); }
        if (samples.Count > 2)
        {
            controller.Saved.HiddenPaths.Add(samples[2].Path); controller.Saved.Recovery.Add(samples[2]); entries.RemoveAll(e => Controller.SamePath(e.Path, samples[2].Path));
        }
        using var main = new MainForm(controller, initialize: false);
        if (small) main.Size = main.MinimumSize;
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(main, entries);
        DashboardCall(main, "RenderList"); DashboardCall(main, "ShowRulesPage");
        var dialog = DashboardField<RulesDialog>(main, "dashboardRulesPage"); dialog.ReadOnlyPreview = true;
        if (stateSort) dialog.SortRules(2);
        if (dialog.RuleList.Items.Count > 0) dialog.RuleList.Items[0].Selected = true;
        using var capture = new System.Windows.Forms.Timer { Interval = 600 };
        capture.Tick += (_, _) =>
        {
            var target = editor ? Application.OpenForms.Cast<Form>().LastOrDefault(f => f is RuleEditor) : main;
            if (target == null) return; capture.Stop();
            using var image = new Bitmap(target.Width, target.Height); target.DrawToBitmap(image, new Rectangle(Point.Empty, target.Size)); image.Save(output);
            if (target != main) target.Close(); main.Close();
        };
        main.Shown += (_, _) => { capture.Start(); if (editor) dialog.OpenEditor(target: entries.FirstOrDefault(e => !Scanner.IsShellEntry(e) && !controller.HasRule(e))); };
        Application.Run(main); return 0;
    }
    static int TestRuleEditing(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string folder = Path.Combine(Path.GetDirectoryName(output)!, "rule-editing-test-" + Guid.NewGuid().ToString("N"));
        var controller = new Controller(folder); var checks = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks.Add(message); }
        void Reject(Action action, string message) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, message); }
        var first = new TrayEntry { Path = Environment.ProcessPath!, Name = "First", Guid = Guid.NewGuid(), Id = 1 };
        var second = first with { Name = "Second", Guid = Guid.NewGuid(), Id = 2 };
        var one = RuleEditing.Save(controller, null, first, false);
        Check(controller.Saved.HiddenIcons.Count == 1 && controller.Saved.HiddenPaths.Count == 0, "Single-icon scope stores an actual icon identity.");
        Reject(() => RuleEditing.Save(controller, null, first, false), "Duplicate single-icon rules are rejected.");
        Reject(() => RuleEditing.Save(controller, null, first with { Id = 77 }, false), "A GUID rule remains unique when its transient numeric icon ID changes.");
        var two = RuleEditing.Save(controller, null, second, false);
        Check(controller.Saved.HiddenIcons.Count == 2, "Different icons of the same program remain independently configurable.");
        Reject(() => RuleEditing.Save(controller, null, first, true), "A program rule cannot silently cover sibling single-icon rules.");
        Reject(() => RuleEditing.Save(controller, one, second with { Path = "C:\\other.exe" }, true), "Editing cannot change the target program.");
        RuleEditing.Remove(controller, new[] { two });
        var program = RuleEditing.Save(controller, one, first, true);
        Check(controller.Saved.HiddenPaths.Count == 1 && controller.Saved.HiddenIcons.Count == 0, "Editing scope atomically replaces the original rule.");
        RuleEditing.Pause(controller, true); Check(controller.Saved.RulesPaused, "Pause persists without changing recovery records.");
        controller.SetManualVisibility(first.Path, false); var beforeDelete = RuleEditing.Capture(controller);
        RuleEditing.Remove(controller, new[] { program }); Check(!controller.HasRule(first.Path), "Deleting a rule removes only its configuration.");
        RuleEditing.Commit(controller, beforeDelete, restoreManual: true);
        Check(controller.HasRule(first.Path) && controller.IsTemporarilyShown(first), "Undo restores both rules and temporary visibility exceptions.");
        var single = RuleEditing.Save(controller, program, first, false);
        var retained = RuleEditing.Save(controller, single, null, false);
        Check(retained.Icon == single.Icon, "An offline single-icon rule retains its matching identity.");
        RuleEditing.Remove(controller, new[] { retained }); program = RuleEditing.Save(controller, null, first, true);
        Reject(() => RuleEditing.Save(controller, program, null, false), "An offline program cannot be converted without an actual target icon.");
        var beforeFailure = RuleEditing.Capture(controller);
        string temporary = Path.Combine(folder, "settings.json.tmp");
        // Controller's persistence path is discovered without assuming a filename.
        string file = (string)typeof(Controller).GetField("file", DashboardFlags)!.GetValue(controller)!; temporary = file + ".tmp";
        Directory.CreateDirectory(temporary);
        bool rejected = false;
        try { RuleEditing.Save(controller, program, first, false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
        finally { Directory.Delete(temporary); }
        Check(rejected && controller.Saved.HiddenPaths.SequenceEqual(beforeFailure.Paths) && controller.Saved.HiddenIcons.SequenceEqual(beforeFailure.Icons), "Failed persistence restores the original rule collections.");
        var loaded = new Controller(folder); Check(loaded.HasRule(first.Path) && loaded.Saved.HiddenIcons.Count == 0, "Failed edits leave the last saved file intact.");
        using var font = new Font("Microsoft YaHei UI", 9.5f);
        using var window = new RulesDialog(controller, new List<TrayEntry> { first, second }, () => Task.FromResult(new List<TrayEntry> { first, second }), action => action(), font);
        Check(window.RuleList.Items.Count == 1 && window.RuleList.Items[0].Tag is RuleReference, "The rule window renders typed rule references.");
        _ = window.RuleList.Handle;
        window.RuleList.Items[0].Selected = true;
        var originalRow = window.RuleList.Items[0];
        window.Render(); Check(window.RuleList.SelectedItems.Count == 1, "Rule refresh preserves the selected rule identity.");
        Check(ReferenceEquals(originalRow, window.RuleList.Items[0]), "Unchanged rule refreshes retain rows and icon images.");
        var search = (TextBox)typeof(RulesDialog).GetField("search", DashboardFlags)!.GetValue(window)!;
        search.Text = "missing application"; Check(window.RuleList.Items.Count == 0, "Rule search has an empty-result state.");
        search.Clear(); Check(window.RuleList.Items.Count == 1, "Clearing rule search restores the catalog.");
        typeof(RulesDialog).GetField("scope", DashboardFlags)!.SetValue(window, 2); window.Render();
        Check(window.RuleList.Items.Count == 0, "Single-icon filtering excludes whole-program rules.");
        typeof(RulesDialog).GetField("scope", DashboardFlags)!.SetValue(window, 1); window.Render();
        Check(window.RuleList.Items.Count == 1, "Whole-program filtering includes matching scope.");
        using var editor = new RuleEditor(controller, null, new List<TrayEntry> { first, second }, first,
            (_, _, _) => Task.FromException<RuleReference>(new InvalidOperationException("Read-only UI test")), font);
        var saveButton = (Button)typeof(RuleEditor).GetField("save", DashboardFlags)!.GetValue(editor)!;
        var errorLabel = (Label)typeof(RuleEditor).GetField("error", DashboardFlags)!.GetValue(editor)!;
        Check(!saveButton.Enabled && errorLabel.Text == L.T("ruleOverlapError"), "The editor explains overlapping rules before submission.");
        using var offline = new RuleEditor(controller, program, new List<TrayEntry>(), null,
            (_, _, _) => Task.FromException<RuleReference>(new InvalidOperationException("Read-only UI test")), font);
        Check(!((RadioButton)typeof(RuleEditor).GetField("single", DashboardFlags)!.GetValue(offline)!).Enabled,
            "Offline program editing prevents choosing an unavailable single-icon identity.");
        var sortController = new Controller(Path.Combine(folder, "sorting")); sortController.Saved.RulesPaused = false;
        var sortEntries = new List<TrayEntry>
        {
            new() { Path = @"C:\TrayPilot.UI.Tests\zeta.exe", Name = "Zeta", Guid = Guid.NewGuid(), State = 1 },
            new() { Path = @"C:\TrayPilot.UI.Tests\alpha.exe", Name = "Alpha", Guid = Guid.NewGuid(), State = 0 },
            new() { Path = @"C:\TrayPilot.UI.Tests\beta.exe", Name = "Beta", Guid = Guid.NewGuid(), State = 0 }
        };
        sortController.Saved.HiddenPaths = sortEntries.Select(e => e.Path).Append(@"C:\TrayPilot.UI.Tests\offline.exe").ToList();
        sortController.SetManualVisibility(sortEntries[1], false);
        using var sorted = new RulesDialog(sortController, sortEntries, () => Task.FromResult(sortEntries), action => action(), font);
        _ = sorted.RuleList.Handle;
        void Header(int column) => typeof(ListView).GetMethod("OnColumnClick", DashboardFlags)!.Invoke(sorted.RuleList, [new ColumnClickEventArgs(column)]);
        Check(sorted.RuleList.Items.Cast<ListViewItem>().Select(i => i.Text).SequenceEqual(new[] { "Alpha", "Beta", "offline", "Zeta" }), "Rules default to culture-aware software-name ascending order.");
        var selectedRule = sorted.RuleList.Items.Cast<ListViewItem>().Single(row => row.Text == "Beta"); selectedRule.Selected = true; selectedRule.Focused = true;
        Header(0); Check(sorted.RuleList.Items[0].Text == "Zeta", "Clicking the software-name header switches to descending order.");
        Header(0); Check(sorted.RuleList.Items[0].Text == "Alpha", "Clicking software-name again restores ascending order.");
        Header(2); Check(sorted.RuleList.Items.Cast<RuleListItem>().Select(row => row.StateRank).SequenceEqual(new[] { 0, 1, 2, 3 }), "State ascending groups hidden, temporary, waiting-to-apply and offline rules.");
        Header(2); Check(sorted.RuleList.Items.Cast<RuleListItem>().Select(row => row.StateRank).SequenceEqual(new[] { 3, 2, 1, 0 }), "The current-state header supports descending order.");
        Check(sorted.RuleList.SelectedItems.Count == 1 && sorted.RuleList.SelectedItems[0].Text == "Beta" && sorted.RuleList.FocusedItem?.Text == "Beta", "Sorting preserves selected and focused rule identities.");
        sortEntries[2] = sortEntries[2] with { State = 1 }; sorted.UpdateEntries(sortEntries);
        Check(sorted.RuleList.Items.Cast<RuleListItem>().Select(row => row.StateRank).SequenceEqual(new[] { 3, 1, 0, 0 }), "Changed tray state re-sorts while retaining the chosen direction.");
        Check(sorted.RuleList.SelectedItems.Count == 1 && sorted.RuleList.SelectedItems[0].Text == "Beta" && sorted.RuleList.FocusedItem?.Text == "Beta", "State refresh keeps selected and focused rules.");
        var sortingView = (TrayListView)sorted.RuleList;
        Check(sortingView.SortColumn == 2 && sortingView.SortDescending && sortingView.SortableColumns.SequenceEqual(new[] { 0, 2 }), "Only software name and current state advertise sortable headers.");
        sortController.Saved.RulesPaused = true; sorted.UpdateEntries(sortEntries);
        Check(sorted.RuleList.Items.Cast<RuleListItem>().Select(row => row.StateRank).SequenceEqual(new[] { 4, 4, 4, 3 }), "Paused live rules and offline rules retain distinct state groups.");
        using var main = new MainForm(sortController, initialize: false);
        typeof(MainForm).GetField("entries", DashboardFlags)!.SetValue(main, sortEntries); DashboardCall(main, "RenderList"); main.Show();
        int topLevelCount = Application.OpenForms.Cast<Form>().Count(form => form.TopLevel);
        DashboardCall(main, "EditRules"); var page = DashboardField<RulesDialog>(main, "dashboardRulesPage");
        Check(!page.TopLevel && page.Visible && page.Parent == DashboardField<Panel>(main, "dashboardPageHost"), "Rules are embedded in the existing main-window content host.");
        Check(Application.OpenForms.Cast<Form>().Count(form => form.TopLevel) == topLevelCount && main.OwnedForms.Length == 0, "Opening rules creates no additional top-level window or modal dialog.");
        var embeddedSearch = (TextBox)typeof(RulesDialog).GetField("search", DashboardFlags)!.GetValue(page)!;
        embeddedSearch.Text = "Beta"; page.SortRules(2);
        DashboardCall(main, "ShowIconsPage"); DashboardCall(main, "EditRules");
        Check(ReferenceEquals(page, DashboardField<RulesDialog>(main, "dashboardRulesPage")) && embeddedSearch.Text == "Beta" && ((TrayListView)page.RuleList).SortColumn == 2,
            "Sidebar navigation retains rule search and sort state.");
        Check(DashboardField<ThemeButton>(main, "dashboardRulesNav").Selected && !DashboardField<ThemeButton>(main, "dashboardIconNav").Selected,
            "Sidebar selection follows the active content page.");
        Check(!((System.Windows.Forms.Timer)typeof(RulesDialog).GetField("timer", DashboardFlags)!.GetValue(page)!).Enabled,
            "Embedded rules share the main snapshot instead of starting another scanner timer.");
        main.Close();
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
}
