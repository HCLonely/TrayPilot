namespace TrayPilot;
internal sealed partial class MainForm : Form
{
    readonly Controller controller;
    readonly StartupRegistration startup;
    readonly Func<List<TrayEntry>> visibilityScanner;
    readonly TextBox search = new() { PlaceholderText = "searchPlaceholder", Width = 280 };
    readonly ListView list = new TrayListView() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    readonly CheckBox autoRefresh = new ThemeCheckBox() { Text = "autoRefreshOption", Checked = true, AutoSize = true, Margin = new(20, 5, 0, 0) };
    readonly RadioButton layoutMode = new() { Text = "gridLayout", Appearance = Appearance.Button, AutoSize = true };
    readonly ContextMenuStrip itemMenu = new();
    List<TrayEntry> entries = new();
    readonly TrayImageCache imageCache = new();
    readonly ScanSession scanSession = new();
    List<RenderedRow> renderedRows = new();
    string? renderedLanguage, renderedSearch;
    readonly bool initialized;
    bool ListVisible => Visible && WindowState != FormWindowState.Minimized;
    bool RulesActive => !controller.Saved.RulesPaused && (controller.Saved.HiddenPaths.Count != 0 || controller.Saved.HiddenIcons.Count != 0);
    bool busy, closing;
    internal MainForm(Controller controller, bool initialize = true, Func<List<TrayEntry>>? visibilityScanner = null, StartupRegistration? startup = null, bool startInTray = false, bool restoreOnStartup = false)
    {
        this.controller = controller;
        initialized = initialize;
        busy = restoreOnStartup;
        systemIconsRequested = controller.Saved.HiddenSystemIcons & SystemIconCatalog.All;
        this.startup = startup ?? new StartupRegistration();
        this.visibilityScanner = visibilityScanner ?? Scanner.Scan;
        L.Set(controller.Saved.Language); UiTheme.Set(controller.Saved.Theme);
        Text = "mainWindowTitle"; Width = 1180; Height = 760; MinimumSize = new(1020, 620);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 9.5f);
        BackColor = UiTheme.Canvas; ForeColor = UiTheme.Ink;
        BuildDashboard();
        SetupShell(initialize); ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        search.TextChanged += (_, _) => RenderList();
        timer.Tick += async (_, _) => await RefreshSnapshotAsync(false);
        VisibleChanged += (_, _) => { if (initialize) { UpdateSystemPageActivation(); UpdateTimer(); if (ListVisible) { RenderList(); _ = RefreshAsync(); } } };
        Resize += (_, _) => { if (initialize) { UpdateSystemPageActivation(); UpdateTimer(); if (ListVisible) RenderList(); } };
        Shown += async (_, _) =>
        {
            if (startInTray && trayIcon?.Visible == true) Hide();
            if (restoreOnStartup)
            {
                EnsureUiContext();
                status.Text = L.T("restoringStartupIcons");
                await Task.Yield();
                try { await controller.RestoreManagedAsync(); }
                catch (OperationCanceledException) when (closing) { }
                catch (Exception ex)
                {
                    if (!closing && !IsDisposed)
                        MessageBox.Show(this, ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage"), L.T("restoreIncomplete"));
                }
                finally { CompleteOperation(); }
                if (!closing && !IsDisposed && controller.LoadWarning is string warning)
                    MessageBox.Show(this, warning, L.T("mainWindowTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (initialize && !closing && !IsDisposed) { await RefreshAsync(); UpdateTimer(); }
        };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            // WM_QUERYENDSESSION asks for consent, not for an immediate exit.
            // Leave the window and ongoing work intact in case shutdown is canceled.
            if (e.CloseReason == CloseReason.WindowsShutDown) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon != null)
            { e.Cancel = true; Hide(); return; }
            if (e.CloseReason == CloseReason.UserClosing && !TryLeaveSettings()) { e.Cancel = true; exitRequested = false; return; }
            if (!controller.Saved.RestoreIconsOnExit)
            {
                closing = true; controller.StopOperations(); timer.Stop(); systemIconWatch.Stop();
                return;
            }
            e.Cancel = true; exitRequested = true;
            if (!busy) _ = ExitAsync();
        };
        FormClosed += (_, _) => timer.Stop();
        if (initialize) SetupSystemIconWatch();
    }
    void UpdateTimer()
    {
        bool rulesVisible = ListVisible && dashboardRulesPage?.Visible == true;
        timer.Interval = (ListVisible && (autoRefresh.Checked || rulesVisible)) || RulesActive || controller.Saved.Recovery.Count != 0 ? 2500 : 15000;
        timer.Enabled = !closing && !IsDisposed && (autoRefresh.Checked || rulesVisible || RulesActive || controller.Saved.Recovery.Count != 0);
    }
    void EnsureUiContext()
    {
        if (InvokeRequired) throw new InvalidOperationException("UI operations must start on the window thread.");
        // Diagnostics also invoke handlers outside Application.Run. Install the UI
        // context there so asynchronous controller mutations remain serialized.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    async Task ExitAsync()
    {
        EnsureUiContext();
        busy = true; timer.Stop();
        try { await controller.RestoreManagedAsync(); closing = true; Close(); }
        catch (Exception ex)
        {
            exitRequested = false; OpenMainWindow();
            MessageBox.Show(this, ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage"), L.T("restoreIncomplete"));
        }
        finally { CompleteOperation(); UpdateTimer(); }
    }
    void EndWindowsSession()
    {
        closing = true;
        timer.Stop(); systemIconWatch.Stop();
        // WM_ENDSESSION(TRUE) is the final notification: do not schedule an async
        // continuation or display a dialog. Failed recovery keeps its journal.
        controller.StopOperations();
        try { if (controller.Saved.RestoreIconsOnExit) controller.RestoreManaged(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
    void UpdateStatus() => status.Text = L.F("iconStatisticsStatus", entries.Count, controller.Saved.HiddenPaths.Count + controller.Saved.HiddenIcons.Count,
        L.T(autoRefresh.Checked ? "autoRefreshStatus" : "manualRefreshStatus"))
        + (controller.Saved.RulesPaused ? " · " + L.T("autoHidePaused") : "")
        + (hotkeyWarning ? " · " + L.T("hotkeyRegistrationFailedMessage") : "");
    void ChangeLayout()
    {
        list.View = layoutMode.Checked ? View.LargeIcon : View.Details;
        foreach (ListViewItem item in list.Items) item.BackColor = list.View == View.Details && item.Index % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
        if (list.View == View.LargeIcon)
        {
            int width = 120 * DeviceDpi / 96, height = 100 * DeviceDpi / 96;
            Native.SendMessageW(list.Handle, 0x1035, 0, (nint)((height << 16) | width)); // LVM_SETICONSPACING
            list.ArrangeIcons(ListViewAlignment.Top);
        }
    }
    void AddButton(string text, Action handler)
    {
        var button = UiTheme.Button(text, primary: text == "hideSelected");
        button.Click += (_, _) => { if (!busy && !closing) handler(); }; actions.Controls.Add(button);
    }
    Task RefreshAsync() => RefreshSnapshotAsync(true);
    async Task RefreshSnapshotAsync(bool forceFull, bool operationOwned = false)
    {
        if ((!operationOwned && busy) || closing || itemMenu.Visible || trayMenu.Visible) return;
        EnsureUiContext();
        busy = true;
        try
        {
            bool full = forceFull || (ListVisible && (autoRefresh.Checked || dashboardRulesPage?.Visible == true));
            var scanned = await Task.Run(() => scanSession.Scan(full));
            if (closing || IsDisposed) return;
            entries = scanned.Where(x => x.Pid != Environment.ProcessId).ToList();
            var matches = controller.RuleMatcher();
            var affected = controller.Saved.RulesPaused ? new HashSet<string>() : entries
                .Where(x => x.State != 1 && matches(x) && !controller.IsTemporarilyShown(x)).Select(x => x.Key).ToHashSet();
            try { await controller.ApplyAsync(entries, freshSnapshot: true); }
            finally
            {
                if (!closing && !IsDisposed)
                {
                    entries = entries.Select(x => affected.Contains(x.Key) ? x with { State = Native.State(x) } : x)
                        .Where(x => x.State is 0 or 1).ToList();
                    if (forceFull || autoRefresh.Checked) RenderList();
                    else if (ListVisible && dashboardRulesPage?.Visible == true) dashboardRulesPage.UpdateEntries(entries.ToList());
                }
            }
            UpdateStatus();
        }
        catch (Exception ex) { status.Text = L.T("refreshFailedPrefix") + ex.Message; }
        finally { if (!operationOwned) { UpdateTimer(); CompleteOperation(); } }
    }
    void UpdateEntryStates() => UpdateEntryStatesCore(true);
    void UpdateEntryStatesCore(bool render)
    {
        if (closing || IsDisposed) return;
        entries = entries.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
        if (render) RenderList();
    }
    void RenderList()
    {
        if (initialized && !ListVisible) return;
        dashboardRulesPage?.UpdateEntries(entries.ToList());
        var matches = controller.RuleMatcher();
        var rows = entries.Select(x => new RenderedRow(x, matches(x), controller.IsTemporarilyShown(x))).ToList();
        if (renderedLanguage == L.Current && renderedSearch == search.Text && rows.Count == renderedRows.Count &&
            rows.Zip(renderedRows).All(x => x.First.SameContent(x.Second))) { UpdateDashboard(); return; }
        string filter = search.Text.Trim();
        if (dashboardFilter == 0 && renderedLanguage == L.Current && renderedSearch == search.Text && rows.Count == renderedRows.Count &&
            rows.Zip(renderedRows).All(x => x.First.Entry.Key == x.Second.Entry.Key && x.First.Entry.Name == x.Second.Entry.Name &&
                x.First.Entry.Path == x.Second.Entry.Path && x.First.Entry.Tooltip == x.Second.Entry.Tooltip &&
                x.First.Entry.Pid == x.Second.Entry.Pid) &&
            list.SmallImageList is { } small && list.LargeImageList is { } large)
        {
            var changed = rows.Zip(renderedRows).Where(x => !x.First.SameContent(x.Second)).ToDictionary(x => x.First.Entry.Key, x => x.First);
            list.BeginUpdate();
            try
            {
                foreach (TrayListItem item in list.Items)
                {
                    if (!changed.TryGetValue(((TrayEntry)item.Tag!).Key, out var row)) continue;
                    var entry = row.Entry;
                    using var icon = DashboardListImage(entry);
                    using var largeIcon = imageCache.Create(entry with { State = 0 }, large.ImageSize.Width);
                    small.Images[item.ImageIndex] = icon; large.Images[item.ImageIndex] = largeIcon;
                    item.Tag = entry; item.ToolTipText = Details(entry);
                    item.SubItems[1].Text = L.T(entry.State == 1 ? "fullyHidden" : "normal");
                    item.SubItems[2].Text = DashboardRuleName(entry);
                    item.SubItems[4].Text = L.T(entry.State == 1 ? "dashboardShowAction" : "dashboardHideAction");
                    item.ForeColor = entry.State == 1 ? Color.FromArgb(140, 145, 155) : UiTheme.Ink;
                    item.MatchesRule = row.Rule;
                    item.RuleRank = DashboardRuleRank(entry);
                }
                renderedRows = rows;
                ApplyDashboardSort();
            }
            finally { list.EndUpdate(); list.Invalidate(); UpdateDashboard(); }
            return;
        }
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => ((TrayEntry)x.Tag!).Key).ToHashSet();
        var focusedKey = (list.FocusedItem?.Tag as TrayEntry)?.Key ?? dashboardEntry?.Key;
        var topKey = list.View == View.Details ? (list.TopItem?.Tag as TrayEntry)?.Key : null;
        var images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(40, 56) };
        var largeImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(40, 40) };
        _ = images.Handle; _ = largeImages.Handle;
        var oldImages = list.SmallImageList;
        var oldLargeImages = list.LargeImageList;
        list.BeginUpdate(); ((TrayListView)list).ClearHover(); list.Items.Clear();
        list.SmallImageList = images;
        list.LargeImageList = largeImages;
        try
        {
        foreach (var row in rows)
        {
            var entry = row.Entry;
            if (!DashboardMatches(entry) || !(entry.Name + entry.Path + entry.Tooltip).Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            using var icon = DashboardListImage(entry);
            images.Images.Add(icon);
            using var largeIcon = imageCache.Create(entry with { State = 0 }, largeImages.ImageSize.Width);
            largeImages.Images.Add(largeIcon);
            var label = entry.Name;
            var item = new TrayListItem(label, row.Rule) { Tag = entry, RuleRank = DashboardRuleRank(entry), ImageIndex = images.Images.Count - 1,
                ToolTipText = Details(entry), Selected = selected.Contains(entry.Key) };
            item.SubItems.Add(L.T(entry.State == 1 ? "fullyHidden" : "normal"));
            item.SubItems.Add(DashboardRuleName(entry));
            item.SubItems.Add(System.IO.Path.GetFileName(entry.Path)); item.SubItems.Add(L.T(entry.State == 1 ? "dashboardShowAction" : "dashboardHideAction"));
            item.BackColor = list.View == View.Details && list.Items.Count % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
            if (entry.State == 1) item.ForeColor = Color.FromArgb(140, 145, 155);
            list.Items.Add(item);
        }
        var focusedItem = list.Items.Cast<ListViewItem>().FirstOrDefault(x => ((TrayEntry)x.Tag!).Key == focusedKey);
        if (focusedItem != null) focusedItem.Focused = true;
        ApplyDashboardSort();
        var top = list.Items.Cast<ListViewItem>().FirstOrDefault(x => ((TrayEntry)x.Tag!).Key == topKey);
        if (top != null) list.TopItem = top;
        renderedRows = rows; renderedLanguage = L.Current; renderedSearch = search.Text;
        }
        finally { list.EndUpdate(); oldImages?.Dispose(); oldLargeImages?.Dispose(); UpdateDashboard(); }
    }
    string Details(TrayEntry entry) => L.F("trayIconDetails",
        entry.Name, L.T(entry.State == 1 ? "hidden" : "shown"), L.T(controller.HasRule(entry) ? "yes" : "no"),
        string.IsNullOrWhiteSpace(entry.Tooltip) ? L.T("none") : entry.Tooltip, System.IO.Path.GetFileName(entry.Path), entry.Pid,
        entry.Path, entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString())
        + (controller.IsTemporarilyShown(entry) ? "\n" + L.T("temporarilyShownHelp") : "");

    void ToggleEntry(TrayEntry entry)
    {
        RunAsyncAction(async () =>
        {
            var state = Scanner.SameOwner(entry) ? Native.State(entry) : -1;
            if (state is not (0 or 1)) throw new IOException(L.T("iconUnavailableMessage"));
            await ChangeEntriesAsync(new[] { entry }, state == 0);
        });
    }

    void ShowItemMenu(TrayEntry entry, Point location)
    {
        itemMenu.Close();
        while (itemMenu.Items.Count > 0) { var old = itemMenu.Items[0]; itemMenu.Items.RemoveAt(0); old.Dispose(); }
        int state = entries.FirstOrDefault(x => x.Key == entry.Key)?.State ?? entry.State;
        itemMenu.Items.Add(L.T("properties"), null, (_, _) => { itemMenu.Close(); ShowProperties(entry); });
        var toggle = itemMenu.Items.Add(L.T(state == 1 ? "showIcons" : "hideIcons"), null, (_, _) => { itemMenu.Close(); ToggleEntry(entry); });
        toggle.Enabled = !busy && state is 0 or 1;
        var rule = itemMenu.Items.Add(L.T(controller.HasRule(entry) ? "alreadyInMatchingRules" : "addIconRule"), null, (_, _) =>
        {
            itemMenu.Close(); RunAction(() => controller.AddIconRule(entry));
        });
        rule.Enabled = !busy && !controller.HasRule(entry) && state is 0 or 1;
        var end = itemMenu.Items.Add(L.T("endTask"), null, async (_, _) => { itemMenu.Close(); await EndTaskAsync(entry); });
        end.Enabled = !busy && state is 0 or 1 && entry.Pid != Environment.ProcessId && !Scanner.IsShellEntry(entry);
        var program = new ToolStripMenuItem(L.T("allApplicationIcons")) { Enabled = !busy && state is 0 or 1 };
        program.DropDownItems.Add(L.T("hideIcons"), null, (_, _) => { itemMenu.Close(); RunAsyncAction(() => ChangePathsAsync(new[] { entry.Path }, true)); });
        program.DropDownItems.Add(L.T("showIcons"), null, (_, _) => { itemMenu.Close(); RunAsyncAction(() => ChangePathsAsync(new[] { entry.Path }, false)); });
        program.DropDownItems.Add(L.T("addToMatchingRules"), null, (_, _) => { itemMenu.Close(); RunAction(() => controller.AddRule(entry.Path)); }).Enabled = !controller.HasRule(entry.Path);
        itemMenu.Items.Add(program);
        UiTheme.Apply(itemMenu); itemMenu.Show(list, location);
    }

    void ShowProperties(TrayEntry entry)
    {
        using var dialog = InfoDialog.Create(entry.Name + " · " + L.T("properties"), ProgramActions.Properties(entry, controller.HasRule(entry)), Font);
        timer.Stop();
        try { dialog.ShowDialog(this); } finally { UpdateTimer(); }
    }

    async Task EndTaskAsync(TrayEntry entry)
    {
        if (busy || closing) return;
        EnsureUiContext();
        busy = true;
        try
        {
            await ProgramActions.EndAsync(entry);
            controller.Saved.Recovery.RemoveAll(x => x.Pid == entry.Pid && x.Started == entry.Started);
            controller.Save();
            if (IsDisposed || closing) return;
            entries.RemoveAll(x => x.Pid == entry.Pid && x.Started == entry.Started);
            RenderList();
            status.Text = L.F("processTerminatedMessage", entry.Name, entry.Pid);
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = L.T("endTaskFailedPrefix") + ex.Message; }
        finally { CompleteOperation(); }
    }

    void ChangePaths(IEnumerable<string> paths, bool hide)
        => ChangePathsAsync(paths, hide, false).GetAwaiter().GetResult();
    async Task ChangePathsAsync(IEnumerable<string> paths, bool hide, bool asynchronous = true)
    {
        var selectedPaths = paths.Select(Controller.NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in selectedPaths) controller.SetManualVisibility(path, hide);
        await controller.ChangeManyAsync(entries.Where(x => selectedPaths.Contains(Controller.NormalizePath(x.Path))), hide, asynchronous);
    }
    void ChangeSelected(bool hide)
    {
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => (TrayEntry)x.Tag!).ToList();
        if (selected.Count == 0) { status.Text = L.T("noApplicationSelectedMessage"); return; }
        RunAsyncAction(() => ChangeEntriesAsync(selected, hide));
    }
    void ChangeEntries(IEnumerable<TrayEntry> selected, bool hide)
        => ChangeEntriesAsync(selected, hide, false).GetAwaiter().GetResult();
    async Task ChangeEntriesAsync(IEnumerable<TrayEntry> selected, bool hide, bool asynchronous = true)
    {
        var targets = selected.DistinctBy(x => x.Key).ToList();
        foreach (var entry in targets) controller.SetManualVisibility(entry, hide);
        await controller.ChangeManyAsync(targets, hide, asynchronous);
    }
    void RunAction(Action action) => RunAsyncAction(() => { action(); return Task.CompletedTask; });
    async void RunAsyncAction(Func<Task> action)
    {
        if (busy || closing) return;
        EnsureUiContext();
        busy = true;
        try { await action(); await RefreshSnapshotAsync(true, operationOwned: true); }
        catch (Exception ex)
        {
            if (!closing && !IsDisposed)
            { status.Text = ex.Message; OpenMainWindow(); MessageBox.Show(this, ex.Message, L.T("operationIncomplete"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        finally { UpdateTimer(); CompleteOperation(); }
    }
    void EditRules()
    {
        ShowRulesPage();
    }

    internal Form CreateRulesDialog() => new RulesDialog(controller, entries.ToList(), RefreshRuleDataAsync, RunRuleOperationAsync, Font, dashboardEntry);

    async Task<List<TrayEntry>> RefreshRuleDataAsync()
    {
        if (!busy && !closing) await RefreshAsync();
        return entries.ToList();
    }
    async Task RunRuleOperationAsync(Func<Task> action)
    {
        if (busy || closing) throw new IOException(L.T("ruleOperationBusy"));
        EnsureUiContext(); busy = true; timer.Stop();
        try { await action(); }
        finally
        {
            if (!closing && !IsDisposed)
            {
                try { entries = (await Task.Run(Scanner.Scan)).Where(e => e.Pid != Environment.ProcessId).ToList(); }
                catch (Exception ex) { status.Text = L.T("refreshFailedPrefix") + ex.Message; }
                RenderList(); UpdateStatus();
            }
            CompleteOperation(); UpdateTimer();
        }
    }
    void OpenRuleEditor(TrayEntry entry)
    {
        if (busy || closing) return;
        ShowRulesPage(); dashboardRulesPage?.OpenEditor(target: entry);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (closing) systemIconSession?.Stop(controller.Saved.RestoreIconsOnExit);
            else systemIconSession?.Dispose();
            systemIconSession = null;
            systemIconWatch.Dispose();
            timer.Dispose();
            imageCache.Dispose();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            showAllHotkey?.Dispose(); hideRulesHotkey?.Dispose(); mainHotkey?.Dispose();
            if (trayIcon != null) { trayIcon.Visible = false; var icon = trayIcon.Icon; trayIcon.Dispose(); icon?.Dispose(); }
            ClearTrayMenu(); trayMenu.Dispose();
            itemMenu.Dispose();
            var images = list.SmallImageList;
            list.SmallImageList = null;
            images?.Dispose();
            var largeImages = list.LargeImageList;
            list.LargeImageList = null;
            largeImages?.Dispose();
        }
        base.Dispose(disposing);
    }
}
