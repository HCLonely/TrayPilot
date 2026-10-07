namespace TrayPilot;

internal sealed class RulesDialog : Form
{
    readonly Controller controller;
    readonly Func<Task<List<TrayEntry>>> refresh;
    readonly Func<Func<Task>, Task> execute;
    readonly ListView rules = new TrayListView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowItemToolTips = true };
    readonly TextBox search = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    readonly Label banner = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label feedback = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly FlowLayoutPanel details = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly Button pause, apply, edit, remove, temporary, undo;
    readonly ImageList images = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new(40, 56) };
    readonly TrayImageCache cache = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    List<TrayEntry> entries;
    RuleSnapshot? previous;
    bool working, refreshing;
    int scope;
    RuleReference? current;
    string? detailKey;
    string? renderSignature;
    readonly bool embedded;
    int sortColumn;
    bool sortDescending;
    readonly List<Button> commands = new();
    readonly ContextMenuStrip rowMenu = new();
    internal ListView RuleList => rules;
    internal bool Working => working;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnlyPreview { get; set; }

    internal RulesDialog(Controller controller, List<TrayEntry> entries, Func<Task<List<TrayEntry>>> refresh,
        Func<Func<Task>, Task> execute, Font font, TrayEntry? selected = null, bool embedded = false, Action? returnToIcons = null)
    {
        this.controller = controller; this.entries = entries; this.refresh = refresh; this.execute = execute; this.embedded = embedded;
        Text = L.T("hideRules"); Size = new(1220, 800); MinimumSize = new(1020, 700); AutoScaleMode = AutoScaleMode.Dpi;
        Font = font; StartPosition = FormStartPosition.CenterParent;
        if (embedded) { TopLevel = false; FormBorderStyle = FormBorderStyle.None; Dock = DockStyle.Fill; MinimumSize = Size.Empty; ShowInTaskbar = false; }
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 22, 28, 14), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 82, 66, 42 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 44));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100)); heading.ColumnStyles.Add(new(SizeType.AutoSize)); heading.RowStyles.Add(new(SizeType.Percent, 100));
        var title = new Panel { Dock = DockStyle.Fill };
        title.Controls.Add(new Label { Text = L.T("rulesSubtitle"), AutoSize = true, Location = new(0, 46), ForeColor = UiTheme.Muted });
        title.Controls.Add(new Label { Text = L.T("hideRules"), AutoSize = true, Location = new(0, 0), Font = new(font.FontFamily, 21, FontStyle.Bold) });
        heading.Controls.Add(title, 0, 0);
        var add = Command("ruleCreate", () => OpenEditor(), true); add.Margin = new(0, 6, 0, 0); heading.Controls.Add(add, 1, 0); root.Controls.Add(heading, 0, 0);
        var state = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14), Margin = new(0, 0, 0, 8) };
        pause = Command("rulePause", async () => await PauseAsync()); pause.Dock = DockStyle.Right;
        state.Controls.Add(banner); state.Controls.Add(pause); root.Controls.Add(state, 0, 1); root.Controls.Add(summary, 0, 2);
        var work = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        work.ColumnStyles.Add(new(SizeType.Percent, 100)); work.ColumnStyles.Add(new(SizeType.Absolute, 280)); work.RowStyles.Add(new(SizeType.Percent, 100));
        var listPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(8), Margin = Padding.Empty };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 60, 44 }) table.RowStyles.Add(new(SizeType.Absolute, height));
        table.RowStyles.Add(new(SizeType.Percent, 100)); table.RowStyles.Add(new(SizeType.Absolute, 60));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new(8, 10, 8, 8) };
        searchRow.ColumnStyles.Add(new(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.RowStyles.Add(new(SizeType.Percent, 100));
        var searchBox = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(12, 9, 12, 5), Margin = new(0, 0, 10, 0), FocusBorder = true };
        search.PlaceholderText = L.T("ruleSearch"); search.AccessibleName = L.T("ruleSearch");
        search.GotFocus += (_, _) => searchBox.Invalidate(); search.LostFocus += (_, _) => searchBox.Invalidate(); search.TextChanged += (_, _) => Render();
        searchBox.Controls.Add(search); searchRow.Controls.Add(searchBox, 0, 0);
        apply = Command("ruleApply", async () => await ApplyAsync()); searchRow.Controls.Add(apply, 1, 0); table.Controls.Add(searchRow, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new(8, 0, 0, 0) };
        foreach (var (key, value) in new[] { ("ruleAll", 0), ("rulePrograms", 1), ("ruleSingles", 2) })
        {
            var radio = new RadioButton { Text = L.T(key), AutoSize = true, Appearance = Appearance.Button, Checked = value == 0 };
            UiTheme.Toggle(radio); radio.CheckedChanged += (_, _) => { if (radio.Checked) { scope = value; Render(); } }; filters.Controls.Add(radio);
        }
        table.Controls.Add(filters, 0, 1);
        rules.Columns.Add(L.T("softwareName"), 225); rules.Columns.Add(L.T("ruleScope"), L.Current == "en-US" ? 158 : 140); rules.Columns.Add(L.T("ruleCurrentState"), L.Current == "en-US" ? 200 : 160);
        rules.SmallImageList = images; _ = images.Handle;
        ((TrayListView)rules).SortableColumns = [0, 2];
        rules.ColumnClick += (_, e) => SortRules(e.Column);
        rules.Resize += (_, _) => rules.Columns[0].Width = Math.Max(170 * DeviceDpi / 96, rules.ClientSize.Width - rules.Columns[1].Width - rules.Columns[2].Width - 24);
        rules.SelectedIndexChanged += (_, _) => UpdateDetails(); rules.DoubleClick += (_, _) => { if (current != null) OpenEditor(current); };
        rowMenu.Items.Add(L.T("ruleTemporary"), null, async (_, _) => await TemporarilyShowAsync());
        rowMenu.Items.Add(L.T("ruleEdit"), null, (_, _) => { if (current != null) OpenEditor(current); });
        rowMenu.Items.Add(L.T("ruleDelete"), null, (_, _) => OpenDelete()); rules.ContextMenuStrip = rowMenu;
        rowMenu.Opening += (_, e) =>
        {
            UpdateDetails(); e.Cancel = current == null || working;
            rowMenu.Items[0].Enabled = current != null && Live(current).Count > 0;
        };
        rules.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || ((TrayListView)rules).ItemAt(e.Location) is not { } item) return;
            if (!item.Selected) { foreach (ListViewItem row in rules.SelectedItems.Cast<ListViewItem>().ToArray()) row.Selected = false; item.Selected = true; }
            item.Focused = true;
        };
        table.Controls.Add(rules, 0, 2);
        table.Controls.Add(new Label { Text = L.T("rulesBehaviorHint"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new(8, 8, 4, 0) }, 0, 3);
        listPanel.Controls.Add(table); work.Controls.Add(listPanel, 0, 0);
        var detailPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14), Margin = new(18, 0, 0, 0) };
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        detailLayout.ColumnStyles.Add(new(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new(SizeType.Absolute, 36)); detailLayout.RowStyles.Add(new(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new(SizeType.Absolute, 144));
        detailLayout.Controls.Add(new Label { Text = L.T("ruleDetails"), Dock = DockStyle.Fill, Font = new(font.FontFamily, 10, FontStyle.Bold) }, 0, 0); detailLayout.Controls.Add(details, 0, 1);
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        temporary = Command("ruleTemporary", async () => await TemporarilyShowAsync()); edit = Command("ruleEdit", () => { if (current != null) OpenEditor(current); }); remove = Command("ruleDelete", () => OpenDelete());
        foreach (var button in new[] { temporary, edit, remove }) { button.AutoSize = false; button.Size = new(224, 38); button.Margin = new(0, 4, 0, 4); detailActions.Controls.Add(button); }
        detailLayout.Controls.Add(detailActions, 0, 2); detailPanel.Controls.Add(detailLayout); work.Controls.Add(detailPanel, 1, 0); root.Controls.Add(work, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        footer.Controls.Add(feedback, 0, 0); undo = Command("ruleUndo", async () => await UndoAsync()); footer.Controls.Add(undo, 1, 0);
        var close = Command(embedded ? "iconManagement" : "close", embedded ? () => returnToIcons?.Invoke() : Close);
        footer.Controls.Add(close, 2, 0); CancelButton = close; root.Controls.Add(footer, 0, 4); Controls.Add(root);
        if (embedded) SizeChanged += (_, _) => { bool showDetails = ClientSize.Width >= 920 * DeviceDpi / 96; detailPanel.Visible = showDetails; work.ColumnStyles[1].Width = showDetails ? 280 * DeviceDpi / 96 : 0; };
        details.SizeChanged += (_, _) => { foreach (Control child in details.Controls) if (child is Label) child.Width = Math.Max(140, details.ClientSize.Width - 20); };
        UiTheme.Apply(this); UiTheme.Apply(rowMenu); Render();
        if (selected != null && rules.Items.Cast<ListViewItem>().FirstOrDefault(i => ((RuleReference)i.Tag!).Matches(selected)) is { } selectedRule)
        { selectedRule.Selected = true; selectedRule.Focused = true; selectedRule.EnsureVisible(); }
        Shown += (_, _) => { UiTheme.Apply(this); if (!embedded) timer.Start(); };
        timer.Tick += async (_, _) => await RefreshAsync(); FormClosing += (_, e) => { if (working || refreshing) e.Cancel = true; };
    }

    internal void UpdateEntries(List<TrayEntry> snapshot) { if (IsDisposed) return; entries = snapshot; Render(); }
    internal void FocusSearch() => search.Focus();

    Button Command(string key, Action handler, bool primary = false)
    {
        var button = UiTheme.Button(L.T(key), primary); button.Click += (_, _) => { if (!working) handler(); }; commands.Add(button); return button;
    }
    TrayEntry? Entry(RuleReference rule) => entries.FirstOrDefault(rule.Matches) ?? controller.Saved.Recovery.FirstOrDefault(rule.Matches);
    List<TrayEntry> Live(RuleReference rule) => entries.Where(rule.Matches).ToList();
    int StateRank(RuleReference rule)
    {
        var live = Live(rule);
        if (live.Count == 0) return 3;
        if (controller.Saved.RulesPaused) return 4;
        if (live.Any(controller.IsTemporarilyShown)) return 1;
        return live.All(e => e.State == 1) ? 0 : 2;
    }
    string State(RuleReference rule) => L.T(StateRank(rule) switch { 0 => "ruleAutoHidden", 1 => "ruleTemporarilyShown", 2 => "ruleWaitingApply", 3 => "ruleWaiting", _ => "autoHidePaused" });
    internal void SortRules(int column)
    {
        if (column is not (0 or 2)) return;
        sortDescending = sortColumn == column && !sortDescending; sortColumn = column;
        ApplySort(); UpdateDetails();
    }
    void ApplySort()
    {
        string? focused = (rules.FocusedItem?.Tag as RuleReference)?.Key;
        var view = (TrayListView)rules; view.SortColumn = sortColumn; view.SortDescending = sortDescending;
        rules.ListViewItemSorter = new RuleItemComparer(sortColumn, sortDescending, L.Current);
        if (focused != null && rules.Items.Cast<ListViewItem>().FirstOrDefault(row => ((RuleReference)row.Tag!).Key == focused) is { } item) item.Focused = true;
        rules.Invalidate();
    }
    internal void Render()
    {
        if (rowMenu.BackColor != UiTheme.Surface) UiTheme.Apply(rowMenu);
        var catalog = RuleEditing.Catalog(controller); var selected = rules.SelectedItems.Cast<ListViewItem>().Select(i => ((RuleReference)i.Tag!).Key).ToHashSet();
        banner.Text = L.T(controller.Saved.RulesPaused ? "autoHidePaused" : "dashboardRulesRunning") + " · " + L.T("rulePauseHint");
        pause.Text = L.T(controller.Saved.RulesPaused ? "ruleResume" : "rulePause");
        summary.Text = L.F("ruleSummary", catalog.Count, catalog.Count(r => r.Program), catalog.Count(r => !r.Program), catalog.Count(r => Live(r).Count == 0));
        string query = search.Text.Trim();
        var visible = catalog.Where(rule => (scope != 1 || rule.Program) && (scope != 2 || !rule.Program))
            .Select(rule => (Rule: rule, Entry: Entry(rule) ?? new TrayEntry { Name = Path.GetFileNameWithoutExtension(rule.Path), Path = rule.Path }))
            .Where(row => (row.Entry.Name + row.Rule.Path + row.Entry.Tooltip).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (visible.Count > 0 && (feedback.Text == L.T("noHideRulesMessage").Replace('\n', ' ') || feedback.Text == L.T("dashboardNoResults"))) feedback.Text = "";
        string signature = L.Current + UiTheme.Dark + ":" + catalog.Count + ":" + scope + string.Join("\n", visible.Select(row => row.Rule.Key + "|" + row.Entry.Name + "|" + row.Entry.Tooltip + "|" + State(row.Rule)
            + "|" + (row.Entry.IconSnapshot is { Length: > 0 } image ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)) : "")));
        if (signature == renderSignature) { undo.Enabled = previous != null && !working; UpdateDetails(); return; }
        renderSignature = signature;
        string? top = rules.TopItem?.Tag is RuleReference topRule ? topRule.Key : null;
        string? focused = (rules.FocusedItem?.Tag as RuleReference)?.Key;
        rules.BeginUpdate(); rules.Items.Clear(); images.Images.Clear();
        foreach (var (rule, entry) in visible)
        {
            using var image = cache.Create(entry with { State = 0 }, 32); using var padded = new Bitmap(40, 56);
            using (var graphics = Graphics.FromImage(padded)) graphics.DrawImageUnscaled(image, 4, 12); images.Images.Add(padded);
            var row = new RuleListItem(entry.Name, StateRank(rule)) { Tag = rule, ImageIndex = images.Images.Count - 1, ToolTipText = rule.Path + "\n" + (entry.Tooltip.Length > 0 ? entry.Tooltip : rule.Icon?.Label), Selected = selected.Contains(rule.Key) };
            row.SubItems.Add(L.T(rule.Program ? "dashboardProgramRule" : "dashboardSingleRule")); row.SubItems.Add(State(rule)); rules.Items.Add(row);
            if (rule.Key == focused) row.Focused = true;
        }
        ApplySort(); rules.EndUpdate(); undo.Enabled = previous != null && !working; UpdateDetails();
        if (top != null && rules.Items.Cast<ListViewItem>().FirstOrDefault(row => ((RuleReference)row.Tag!).Key == top) is { } topRow) rules.TopItem = topRow;
        if (rules.Items.Count == 0) feedback.Text = L.T(catalog.Count == 0 ? "noHideRulesMessage" : "dashboardNoResults").Replace('\n', ' ');
    }
    void UpdateDetails()
    {
        current = (rules.FocusedItem?.Selected == true ? rules.FocusedItem : rules.SelectedItems.Cast<ListViewItem>().FirstOrDefault())?.Tag as RuleReference;
        edit.Enabled = remove.Enabled = current != null && !working; temporary.Enabled = current != null && Live(current).Count != 0 && !working;
        var currentEntry = current == null ? null : Entry(current);
        string key = current?.Key + ":" + (current == null ? "" : State(current)) + ":" + currentEntry?.Name + ":" + currentEntry?.Tooltip
            + ":" + (currentEntry?.IconSnapshot is { Length: > 0 } snapshot ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot)) : "");
        if (detailKey == key) return; detailKey = key;
        while (details.Controls.Count > 0) { var control = details.Controls[0]; details.Controls.RemoveAt(0); control.Dispose(); }
        void Line(string text, bool heading = false)
        {
            var label = new Label { Text = text, Width = Math.Max(140, details.ClientSize.Width - 20), Height = heading ? 30 : text.Length > 45 ? 66 : 38, ForeColor = heading ? UiTheme.Ink : UiTheme.Muted, Margin = new(0, 4, 0, 2), AutoEllipsis = true };
            if (heading) { label.Font = new(Font.FontFamily, 10, FontStyle.Bold); label.Disposed += (_, _) => label.Font.Dispose(); } details.Controls.Add(label);
        }
        if (current == null) { Line(L.T("ruleSelectHint")); return; }
        var entry = Entry(current) ?? new TrayEntry { Name = Path.GetFileNameWithoutExtension(current.Path), Path = current.Path };
        var icon = new PictureBox { Image = cache.Create(entry with { State = 0 }, 40), Size = new(40, 40), SizeMode = PictureBoxSizeMode.Zoom };
        icon.Disposed += (_, _) => icon.Image?.Dispose(); details.Controls.Add(icon);
        Line(entry.Name, true); Line(State(current)); Line(L.T("ruleScope"), true); Line(L.T(current.Program ? "dashboardProgramRule" : "dashboardSingleRule")); Line(L.T("fullPath"), true); Line(current.Path);
        if (current.Icon != null)
        {
            string identity = current.Icon.Label;
            var advanced = Command("ruleAdvanced", () => { using var info = InfoDialog.Create(L.T("ruleAdvanced"), new[] { new KeyValuePair<string, string>(L.T("iconId"), identity) }, Font); info.ShowDialog(this); });
            advanced.Disposed += (_, _) => commands.Remove(advanced); details.Controls.Add(advanced);
        }
    }
    async Task RefreshAsync()
    {
        if (working || refreshing || IsDisposed || OwnedForms.Length > 0) return; refreshing = true;
        try { entries = await refresh(); if (!IsDisposed) Render(); }
        catch (Exception ex) { if (!IsDisposed) feedback.Text = L.T("refreshFailedPrefix") + ex.Message; }
        finally { refreshing = false; }
    }
    internal async Task OperationAsync(Func<Task> action, string success)
    {
        if (ReadOnlyPreview) throw new InvalidOperationException("Read-only screenshot preview");
        if (working) return; working = true; timer.Stop(); foreach (var button in commands.ToArray()) button.Enabled = false;
        feedback.Text = L.T("ruleWorking");
        try
        {
            await execute(action);
            entries = entries.Select(e => e with { State = Native.State(e) }).Where(e => e.State is 0 or 1).ToList();
            feedback.Text = success;
        }
        finally { working = false; foreach (var button in commands.ToArray()) button.Enabled = true; Render(); if (!embedded) timer.Start(); }
    }
    async Task PauseAsync()
    {
        try { await OperationAsync(async () => { RuleEditing.Pause(controller, !controller.Saved.RulesPaused); if (!controller.Saved.RulesPaused) await controller.ApplyAsync(entries); }, L.T("rulePauseSuccess")); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    async Task ApplyAsync()
    {
        try { await OperationAsync(async () => { RuleEditing.Pause(controller, false); controller.ResetManualVisibility(); await controller.ApplyAsync(entries); }, L.T("ruleApplySuccess")); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    async Task TemporarilyShowAsync()
    {
        if (current == null) return; var rule = current;
        try { await OperationAsync(async () => { var targets = Live(rule); await controller.ChangeManyAsync(targets, false, completed: e => controller.SetManualVisibility(e, false)); }, L.T("ruleTemporarySuccess")); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    async Task UndoAsync()
    {
        if (previous == null) return; var snapshot = previous;
        try { await OperationAsync(async () => { RuleEditing.Commit(controller, snapshot, restoreManual: true); previous = null; if (!controller.Saved.RulesPaused) await controller.ApplyAsync(entries); }, L.T("ruleUndoSuccess")); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    internal void OpenEditor(RuleReference? original = null, TrayEntry? target = null)
    {
        if (working) return;
        using var editor = new RuleEditor(controller, original, entries, target, async (rule, entry, program) =>
        {
            var snapshot = RuleEditing.Capture(controller); RuleReference? saved = null;
            await OperationAsync(async () =>
            {
                saved = RuleEditing.Save(controller, rule, entry, program); previous = snapshot;
                try { if (!controller.Saved.RulesPaused) await controller.ApplyAsync(entries); }
                catch (Exception ex) { throw new RuleSavedException(saved, ex); }
            }, L.T("ruleSaveSuccess"));
            return saved!;
        }, Font);
        timer.Stop(); try { editor.ShowDialog(embedded ? FindFormOwner() : this); } finally { if (!embedded) timer.Start(); Render(); }
    }
    void OpenDelete()
    {
        if (working) return;
        var targets = rules.SelectedItems.Cast<ListViewItem>().Select(i => (RuleReference)i.Tag!).ToList(); if (targets.Count == 0) return;
        using var dialog = new Form { Text = L.T("ruleDeleteTitle"), Size = new(540, 310), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font, StartPosition = FormStartPosition.CenterParent, Padding = new(24) };
        var restore = new ThemeCheckBox { Text = L.T("ruleDeleteRestore"), Dock = DockStyle.Top, Height = 52, Checked = false };
        var text = new Label { Text = L.F("ruleDeleteMessage", targets.Count), Dock = DockStyle.Top, Height = 96 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        var confirm = UiTheme.Button(L.T("ruleDelete")); var cancel = UiTheme.Button(L.T("cancel")); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel); dialog.Controls.Add(restore); dialog.Controls.Add(text); dialog.Controls.Add(buttons); dialog.CancelButton = cancel;
        confirm.Click += async (_, _) =>
        {
            confirm.Enabled = cancel.Enabled = false;
            try { await DeleteAsync(targets, restore.Checked); dialog.Close(); }
            catch (Exception ex) { text.Text = ex.Message; }
            finally { confirm.Enabled = cancel.Enabled = true; }
        };
        dialog.FormClosing += (_, e) => { if (working) e.Cancel = true; };
        UiTheme.Apply(dialog); timer.Stop(); try { dialog.ShowDialog(embedded ? FindFormOwner() : this); } finally { if (!embedded) timer.Start(); Render(); }
    }
    Form FindFormOwner() => Parent?.FindForm() ?? this;
    internal async Task DeleteAsync(List<RuleReference> targets, bool restore)
    {
        var snapshot = RuleEditing.Capture(controller);
        await OperationAsync(async () =>
        {
            if (restore) await controller.ChangeManyAsync(entries.Concat(controller.Saved.Recovery).DistinctBy(e => e.Key).Where(e => targets.Any(r => r.Matches(e))), false);
            RuleEditing.Remove(controller, targets); previous = snapshot;
        }, L.T("ruleDeleteSuccess"));
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleShortcut(keyData)) return true; return base.ProcessCmdKey(ref msg, keyData);
    }
    internal bool HandleShortcut(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K)) { search.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D1)) { SortRules(0); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D2)) { SortRules(2); return true; }
        if (keyData == Keys.Escape && search.Focused && search.Text.Length > 0) { search.Clear(); return true; }
        if (!working && rules.Focused && keyData == Keys.Delete) { OpenDelete(); return true; }
        if (!working && rules.Focused && keyData == Keys.Enter && current != null) { OpenEditor(current); return true; }
        return false;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); rowMenu.Dispose(); cache.Dispose(); rules.SmallImageList = null; images.Dispose(); } base.Dispose(disposing);
    }
}
