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
    string? renderedContent;
    bool busy, closing;
    internal MainForm(Controller controller, bool initialize = true, Func<List<TrayEntry>>? visibilityScanner = null, StartupRegistration? startup = null, bool startInTray = false)
    {
        this.controller = controller;
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
        AddButton("restoreAll", () => RunAction(() => { controller.RestoreManaged(temporarilyShow: true); RestoreLiveSystemIcons(); }));
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
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { if (startInTray && trayIcon?.Visible == true) Hide(); if (initialize) { await RefreshAsync(); UpdateTimer(); } };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon != null)
            { e.Cancel = true; Hide(); return; }
            timer.Stop();
            try { controller.RestoreManaged(); closing = true; }
            catch (Exception ex) { e.Cancel = true; exitRequested = false; OpenMainWindow(); MessageBox.Show(this, ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage"), L.T("restoreIncomplete")); UpdateTimer(); }
        };
        FormClosed += (_, _) => timer.Stop();
        if (initialize) SetupSystemIconWatch();
    }
    void UpdateTimer() => timer.Enabled = autoRefresh.Checked && !closing && !IsDisposed;
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
    async Task RefreshAsync()
    {
        if (busy || closing || itemMenu.Visible || trayMenu.Visible) return;
        busy = true;
        try
        {
            var scanned = await Task.Run(Scanner.Scan);
            if (closing || IsDisposed) return;
            entries = scanned.Where(x => x.Pid != Environment.ProcessId).ToList();
            try { controller.Apply(entries); }
            finally { UpdateEntryStates(); }
            UpdateStatus();
        }
        catch (Exception ex) { status.Text = L.T("refreshFailedPrefix") + ex.Message; }
        finally { CompleteOperation(); }
    }
    void UpdateEntryStates()
    {
        if (closing || IsDisposed) return;
        entries = entries.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
        RenderList();
    }
    void RenderList()
    {
        // Leave unchanged rows intact so periodic scanning does not dismiss tooltips or reset scrolling.
        var content = System.Text.Json.JsonSerializer.Serialize(new { Language = L.Current, Search = search.Text, Rows = entries.Select(x => new
        {
            x.Key, x.Name, x.Path, x.Tooltip, x.State, Rule = controller.HasRule(x), Manual = controller.IsTemporarilyShown(x),
            Icon = x.IconSnapshot == null ? "" : Convert.ToBase64String(x.IconSnapshot)
        }) });
        if (content == renderedContent) return;
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => ((TrayEntry)x.Tag!).Key).ToHashSet();
        var topKey = list.View == View.Details ? (list.TopItem?.Tag as TrayEntry)?.Key : null;
        var images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(28, 28) };
        var largeImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(40, 40) };
        var oldImages = list.SmallImageList;
        var oldLargeImages = list.LargeImageList;
        list.BeginUpdate(); ((TrayListView)list).ClearHover(); list.Items.Clear();
        list.SmallImageList = images;
        list.LargeImageList = largeImages;
        try
        {
        foreach (var entry in entries)
        {
            if (!(entry.Name + entry.Path + entry.Tooltip).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            using var icon = TrayImages.Create(entry, images.ImageSize.Width);
            images.Images.Add(icon);
            using var largeIcon = TrayImages.Create(entry, largeImages.ImageSize.Width);
            largeImages.Images.Add(largeIcon);
            var label = entries.Count(x => Controller.SamePath(x.Path, entry.Path)) > 1
                ? $"{entry.Name} · {(entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString())} · PID {entry.Pid}" : entry.Name;
            var item = new TrayListItem(label, controller.HasRule(entry)) { Tag = entry, ImageIndex = images.Images.Count - 1,
                ToolTipText = Details(entry), Selected = selected.Contains(entry.Key) };
            item.SubItems.Add(L.T(entry.State == 1 ? "fullyHidden" : "normal"));
            item.SubItems.Add(controller.HasRule(entry) ? L.T("ruleMatchIndicator") : "—");
            item.SubItems.Add(System.IO.Path.GetFileName(entry.Path)); item.SubItems.Add(entry.Path);
            item.BackColor = list.View == View.Details && list.Items.Count % 2 != 0 ? UiTheme.Stripe : UiTheme.Surface;
            if (entry.State == 1) item.ForeColor = Color.FromArgb(140, 145, 155);
            list.Items.Add(item);
        }
        var top = list.Items.Cast<ListViewItem>().FirstOrDefault(x => ((TrayEntry)x.Tag!).Key == topKey);
        if (top != null) list.TopItem = top;
        renderedContent = content;
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
        RunAction(() =>
        {
            var state = Scanner.SameOwner(entry) ? Native.State(entry) : -1;
            if (state is not (0 or 1)) throw new IOException(L.T("iconUnavailableMessage"));
            ChangeEntries(new[] { entry }, state == 0);
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
        program.DropDownItems.Add(L.T("hideIcons"), null, (_, _) => { itemMenu.Close(); RunAction(() => ChangePaths(new[] { entry.Path }, true)); });
        program.DropDownItems.Add(L.T("showIcons"), null, (_, _) => { itemMenu.Close(); RunAction(() => ChangePaths(new[] { entry.Path }, false)); });
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
    {
        foreach (var path in paths.Select(Controller.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            controller.SetManualVisibility(path, hide);
            foreach (var entry in entries.Where(x => Controller.SamePath(x.Path, path)))
                if (hide) controller.Hide(entry); else controller.Show(entry);
        }
    }
    void ChangeSelected(bool hide)
    {
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => (TrayEntry)x.Tag!).ToList();
        if (selected.Count == 0) { status.Text = L.T("noApplicationSelectedMessage"); return; }
        RunAction(() => ChangeEntries(selected, hide));
    }
    void ChangeEntries(IEnumerable<TrayEntry> selected, bool hide)
    {
        foreach (var entry in selected.DistinctBy(x => x.Key))
        {
            controller.SetManualVisibility(entry, hide);
            if (hide) controller.Hide(entry); else controller.Show(entry);
        }
    }
    async void RunAction(Action action)
    {
        if (busy || closing) return;
        try { action(); await RefreshAsync(); }
        catch (Exception ex) { status.Text = ex.Message; OpenMainWindow(); MessageBox.Show(this, ex.Message, L.T("operationIncomplete"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    void EditRules()
    {
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
        rules.SelectedIndexChanged += (_, _) => remove.Enabled = rules.SelectedItems.Count > 0;
        remove.Click += (_, _) =>
        {
            try
            {
                foreach (var row in rules.SelectedItems.Cast<ListViewItem>().ToList())
                {
                    if (row.Tag is IconRule iconRule)
                    {
                        foreach (var entry in controller.Saved.Recovery.Where(iconRule.Matches).ToList()) controller.Show(entry);
                        controller.RemoveIconRule(iconRule); rules.Items.Remove(row); continue;
                    }
                    var path = (string)row.Tag!;
                    // Keep the rule available for retry if restoring an icon fails.
                    foreach (var entry in controller.Saved.Recovery.Where(x => Controller.SamePath(x.Path, path)).ToList()) controller.Show(entry);
                    controller.RemoveRule(path); rules.Items.Remove(row);
                }
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, L.T("operationIncomplete")); }
            finally { empty.Visible = rules.Items.Count == 0; remove.Enabled = rules.SelectedItems.Count > 0; }
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
            systemIconSession?.Dispose(); systemIconSession = null;
            systemIconWatch.Dispose();
            timer.Dispose();
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
