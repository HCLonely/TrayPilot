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
    readonly CheckBox autoRefresh = new() { Text = "autoRefreshOption", Checked = true, AutoSize = true, Margin = new(20, 5, 0, 0) };
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
    internal MainForm(Controller controller, bool initialize = true, Func<List<TrayEntry>>? visibilityScanner = null, StartupRegistration? startup = null, bool startInTray = false)
    {
        this.controller = controller;
        initialized = initialize;
        systemIconsRequested = controller.Saved.HiddenSystemIcons & SystemIconCatalog.All;
        this.startup = startup ?? new StartupRegistration();
        this.visibilityScanner = visibilityScanner ?? Scanner.Scan;
        L.Set(controller.Saved.Language); UiTheme.Set(controller.Saved.Theme);
        Text = "mainWindowTitle"; Width = 1180; Height = 760; MinimumSize = new(1020, 620);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 9.5f);
        BackColor = UiTheme.Canvas; ForeColor = UiTheme.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 20, 28, 12), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 58)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 26));
        var heading = new Label { Text = "mainWindowSubtitle", Font = new(Font.FontFamily, 22, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new Label { Text = "iconListHelp",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = Padding.Empty }, 0, 1);
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new(0, 4, 0, 10) };
        searchRow.RowStyles.Add(new(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var searchBox = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14, 10, 14, 8), Margin = new(0, 0, 12, 0) };
        search.BorderStyle = BorderStyle.None; search.Dock = DockStyle.Fill; search.BackColor = UiTheme.Surface; search.ForeColor = UiTheme.Ink;
        searchBox.Controls.Add(search);
        searchBox.Controls.Add(new Label { Text = "search", Dock = DockStyle.Left, Width = 62, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface });
        searchRow.Controls.Add(searchBox, 0, 0);
        var refresh = UiTheme.Button("refresh"); refresh.Click += async (_, _) => { if (!busy && !closing) await RefreshAsync(); };
        searchRow.Controls.Add(refresh, 1, 0);
        autoRefresh.Margin = new(18, 10, 0, 0); autoRefresh.ForeColor = UiTheme.Muted;
        searchRow.Controls.Add(autoRefresh, 2, 0); layout.Controls.Add(searchRow, 0, 2);
        AddButton("hideSelected", () => ChangeSelected(true)); AddButton("restoreSelected", () => ChangeSelected(false));
        var hideRules = UiTheme.Button("hideMatchingIcons"); hideRules.Click += async (_, _) => await ApplyVisibilityPresetAsync(true); actions.Controls.Add(hideRules);
        AddButton("restoreAll", () => RunAsyncAction(async () => { await controller.RestoreManagedAsync(temporarilyShow: true); RestoreLiveSystemIcons(); }));
        var commandRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        commandRow.ColumnStyles.Add(new(SizeType.Percent, 100)); commandRow.ColumnStyles.Add(new(SizeType.AutoSize));
        actions.Margin = Padding.Empty; commandRow.Controls.Add(actions, 0, 0);
        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        options.Controls.Add(new Label { Text = "layout", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new(0, 10, 10, 0) });
        var listMode = new RadioButton { Text = "listLayout", Appearance = Appearance.Button, AutoSize = true, Checked = true };
        UiTheme.Toggle(listMode); UiTheme.Toggle(layoutMode);
        options.Controls.Add(listMode); options.Controls.Add(layoutMode);
        commandRow.Controls.Add(options, 1, 0); layout.Controls.Add(commandRow, 0, 3);
        layoutMode.CheckedChanged += (_, _) => ChangeLayout();
        autoRefresh.CheckedChanged += (_, _) => { UpdateTimer(); UpdateStatus(); };
        list.BorderStyle = BorderStyle.None; list.BackColor = UiTheme.Surface; list.ForeColor = UiTheme.Ink;
        list.Columns.Add("softwareName", 215); list.Columns.Add("iconState", 125); list.Columns.Add("matchingRules", 140);
        list.Columns.Add("process", 155); list.Columns.Add("path", 430);
        list.SizeChanged += (_, _) =>
        {
            if (list.Columns.Count == 5)
                list.Columns[4].Width = Math.Max(260 * DeviceDpi / 96, list.ClientSize.Width - list.Columns.Cast<ColumnHeader>().Take(4).Sum(x => x.Width) - SystemInformation.VerticalScrollBarWidth - 4);
        };
        list.ShowItemToolTips = true;
        list.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || closing) return;
            if (((TrayListView)list).ItemAt(e.Location)?.Tag is TrayEntry entry) ShowItemMenu(entry, e.Location);
        };
        list.MouseDoubleClick += (_, e) =>
        {
            if (busy || closing || ((TrayListView)list).ItemAt(e.Location)?.Tag is not TrayEntry entry) return;
            if (e.Button == MouseButtons.Left && ModifierKeys == Keys.None) ToggleEntry(entry);
        };
        var contentPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(8), Margin = Padding.Empty };
        contentPanel.Controls.Add(list); layout.Controls.Add(contentPanel, 0, 4);
        status.ForeColor = UiTheme.Accent; status.Margin = Padding.Empty;
        layout.Controls.Add(status, 0, 5);
        layout.Controls.Add(new Label { Text = "closeAndExitHelp", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new(Font.FontFamily, 8.5f), Margin = Padding.Empty }, 0, 6);
        Controls.Add(layout);
        SetupShell(initialize); ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        search.TextChanged += (_, _) => RenderList();
        timer.Tick += async (_, _) => await RefreshSnapshotAsync(false);
        VisibleChanged += (_, _) => { if (initialize) { UpdateTimer(); if (ListVisible) { RenderList(); _ = RefreshAsync(); } } };
        Resize += (_, _) => { if (initialize) { UpdateTimer(); if (ListVisible) RenderList(); } };
        Shown += async (_, _) => { if (startInTray && trayIcon?.Visible == true) Hide(); if (initialize) { await RefreshAsync(); UpdateTimer(); } };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            // WM_QUERYENDSESSION asks for consent, not for an immediate exit.
            // Leave the window and ongoing work intact in case shutdown is canceled.
            if (e.CloseReason == CloseReason.WindowsShutDown) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon != null)
            { e.Cancel = true; Hide(); return; }
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
        timer.Interval = (ListVisible && autoRefresh.Checked) || RulesActive || controller.Saved.Recovery.Count != 0 ? 2500 : 15000;
        timer.Enabled = !closing && !IsDisposed && (autoRefresh.Checked || RulesActive || controller.Saved.Recovery.Count != 0);
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
        var button = UiTheme.Button(text);
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
            bool full = forceFull || (ListVisible && autoRefresh.Checked);
            var scanned = await Task.Run(() => scanSession.Scan(full));
            if (closing || IsDisposed) return;
            entries = scanned.Where(x => x.Pid != Environment.ProcessId).ToList();
            try { await controller.ApplyAsync(entries); }
            finally { UpdateEntryStatesCore(forceFull || autoRefresh.Checked); }
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
        var matches = controller.RuleMatcher();
        var rows = entries.Select(x => new RenderedRow(x, matches(x), controller.IsTemporarilyShown(x))).ToList();
        if (renderedLanguage == L.Current && renderedSearch == search.Text && rows.Count == renderedRows.Count &&
            rows.Zip(renderedRows).All(x => x.First.SameContent(x.Second))) return;
        var pathCounts = entries.GroupBy(x => Controller.NormalizePath(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        string filter = search.Text.Trim();
        if (renderedLanguage == L.Current && renderedSearch == search.Text && rows.Count == renderedRows.Count &&
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
                    using var icon = imageCache.Create(entry, small.ImageSize.Width);
                    using var largeIcon = imageCache.Create(entry, large.ImageSize.Width);
                    small.Images[item.ImageIndex] = icon; large.Images[item.ImageIndex] = largeIcon;
                    item.Tag = entry; item.ToolTipText = Details(entry);
                    item.SubItems[1].Text = L.T(entry.State == 1 ? "fullyHidden" : "normal");
                    item.SubItems[2].Text = row.Rule ? L.T("ruleMatchIndicator") : "—";
                    item.ForeColor = entry.State == 1 ? Color.FromArgb(140, 145, 155) : UiTheme.Ink;
                    item.MatchesRule = row.Rule;
                }
                renderedRows = rows;
            }
            finally { list.EndUpdate(); list.Invalidate(); }
            return;
        }
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => ((TrayEntry)x.Tag!).Key).ToHashSet();
        var topKey = list.View == View.Details ? (list.TopItem?.Tag as TrayEntry)?.Key : null;
        var images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(28, 28) };
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
            if (!(entry.Name + entry.Path + entry.Tooltip).Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            using var icon = imageCache.Create(entry, images.ImageSize.Width);
            images.Images.Add(icon);
            using var largeIcon = imageCache.Create(entry, largeImages.ImageSize.Width);
            largeImages.Images.Add(largeIcon);
            var label = pathCounts[Controller.NormalizePath(entry.Path)] > 1
                ? $"{entry.Name} · {(entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString())} · PID {entry.Pid}" : entry.Name;
            var item = new TrayListItem(label, row.Rule) { Tag = entry, ImageIndex = images.Images.Count - 1,
                ToolTipText = Details(entry), Selected = selected.Contains(entry.Key) };
            item.SubItems.Add(L.T(entry.State == 1 ? "fullyHidden" : "normal"));
            item.SubItems.Add(row.Rule ? L.T("ruleMatchIndicator") : "—");
            item.SubItems.Add(System.IO.Path.GetFileName(entry.Path)); item.SubItems.Add(entry.Path);
            item.BackColor = list.View == View.Details && list.Items.Count % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
            if (entry.State == 1) item.ForeColor = Color.FromArgb(140, 145, 155);
            list.Items.Add(item);
        }
        var top = list.Items.Cast<ListViewItem>().FirstOrDefault(x => ((TrayEntry)x.Tag!).Key == topKey);
        if (top != null) list.TopItem = top;
        renderedRows = rows; renderedLanguage = L.Current; renderedSearch = search.Text;
        }
        finally { list.EndUpdate(); oldImages?.Dispose(); oldLargeImages?.Dispose(); }
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
        if (busy || closing) return;
        using var dialog = CreateRulesDialog();
        timer.Stop();
        try { dialog.ShowDialog(this); } finally { UpdateTimer(); }
        _ = RefreshAsync();
    }

    internal Form CreateRulesDialog()
    {
        var dialog = new Form { Text = L.T("hideRules"), Size = new(880, 540), MinimumSize = new(700, 440),
            StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = UiTheme.Canvas, ForeColor = UiTheme.Ink, Padding = new(24) };
        var rules = new SmoothListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
            HideSelection = false, ShowItemToolTips = true };
        UiTheme.StyleList(rules);
        rules.Columns.Add(L.T("softwareName"), 240); rules.Columns.Add(L.T("path"), 550);
        var images = new ImageList { ImageSize = new(32, 32), ColorDepth = ColorDepth.Depth32Bit };
        // Materialize the image list before temporary bitmaps are disposed.
        _ = images.Handle;
        rules.SmallImageList = images;
        dialog.Disposed += (_, _) => images.Dispose();
        foreach (var path in controller.Saved.HiddenPaths)
        {
            var entry = entries.Concat(controller.Saved.Recovery).FirstOrDefault(x => Controller.SamePath(x.Path, path))
                ?? new TrayEntry { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            using var icon = TrayImages.Create(entry with { State = 0 }, 32); images.Images.Add(icon);
            var row = new ListViewItem(entry.Name + " · " + L.T("allApplicationIcons")) { Tag = path, ImageIndex = images.Images.Count - 1, ToolTipText = path,
                BackColor = rules.Items.Count % 2 == 0 ? UiTheme.Surface : UiTheme.Stripe };
            row.SubItems.Add(path); rules.Items.Add(row);
        }
        foreach (var rule in controller.Saved.HiddenIcons)
        {
            var entry = entries.Concat(controller.Saved.Recovery).FirstOrDefault(rule.Matches)
                ?? new TrayEntry { Path = rule.Path, Name = System.IO.Path.GetFileNameWithoutExtension(rule.Path) };
            using var icon = TrayImages.Create(entry with { State = 0 }, 32); images.Images.Add(icon);
            var row = new ListViewItem($"{entry.Name} · {rule.Label}") { Tag = rule, ImageIndex = images.Images.Count - 1,
                ToolTipText = rule.Path + "\n" + rule.Label, BackColor = rules.Items.Count % 2 == 0 ? UiTheme.Surface : UiTheme.Stripe };
            row.SubItems.Add(rule.Path); rules.Items.Add(row);
        }
        rules.Resize += (_, _) => rules.Columns[1].Width = Math.Max(320, rules.ClientSize.Width - rules.Columns[0].Width - 24);
        var empty = new Label { Text = L.T("noHideRulesMessage"), Dock = DockStyle.Bottom, Height = 40, ForeColor = UiTheme.Muted, Visible = rules.Items.Count == 0 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new(0, 16, 0, 0), FlowDirection = FlowDirection.RightToLeft };
        var close = UiTheme.Button(L.T("close")); close.DialogResult = DialogResult.Cancel;
        var remove = UiTheme.Button(L.T("removeRulesAndRestoreIcons")); remove.Enabled = false;
        bool removing = false;
        dialog.FormClosing += (_, e) => { if (removing) e.Cancel = true; };
        rules.SelectedIndexChanged += (_, _) => remove.Enabled = !removing && rules.SelectedItems.Count > 0;
        remove.Click += async (_, _) =>
        {
            if (busy || removing) return;
            EnsureUiContext();
            busy = removing = true; remove.Enabled = close.Enabled = rules.Enabled = false;
            try
            {
                foreach (var row in rules.SelectedItems.Cast<ListViewItem>().ToList())
                {
                    if (row.Tag is IconRule iconRule)
                    {
                        await controller.ChangeManyAsync(controller.Saved.Recovery.Where(iconRule.Matches), false);
                        controller.RemoveIconRule(iconRule); rules.Items.Remove(row); continue;
                    }
                    var path = (string)row.Tag!;
                    // Keep the rule available for retry if restoring an icon fails.
                    await controller.ChangeManyAsync(controller.Saved.Recovery.Where(x => Controller.SamePath(x.Path, path)), false);
                    controller.RemoveRule(path); rules.Items.Remove(row);
                }
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, L.T("operationIncomplete")); }
            finally
            {
                removing = false; close.Enabled = rules.Enabled = true;
                empty.Visible = rules.Items.Count == 0; remove.Enabled = rules.SelectedItems.Count > 0;
                CompleteOperation();
            }
        };
        buttons.Controls.Add(close); buttons.Controls.Add(remove);
        dialog.Controls.Add(rules); dialog.Controls.Add(empty); dialog.Controls.Add(buttons);
        dialog.Controls.Add(UiTheme.Heading(L.T("hideRules"), L.T("hideRulesScopeHelp"), Font));
        dialog.CancelButton = close;
        UiTheme.Apply(dialog); dialog.Shown += (_, _) => UiTheme.Apply(dialog);
        return dialog;
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
