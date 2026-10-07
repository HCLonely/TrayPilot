namespace TrayPilot;

// Main-window presentation. Tray discovery and mutations remain owned by Controller.
internal sealed partial class MainForm
{
    readonly Label dashboardSummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label dashboardHealth = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label dashboardResults = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label dashboardEmpty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    readonly FlowLayoutPanel dashboardDetails = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly PictureBox dashboardProgramIcon = new() { Size = new(40, 40), SizeMode = PictureBoxSizeMode.Zoom };
    readonly Button dashboardToggle = UiTheme.Button("hideIcons", true);
    readonly Button dashboardRule = UiTheme.Button("addIconRule");
    readonly List<RadioButton> dashboardFilters = new();
    readonly List<Button> dashboardThemes = new();
    readonly CheckBox dashboardSelectAll = new ThemeCheckBox() { Text = "dashboardSelectAll", AutoSize = true, Margin = new(16, 5, 0, 0) };
    TableLayoutPanel? dashboardWork;
    SurfacePanel? dashboardDetailPanel;
    TrayEntry? dashboardEntry;
    int dashboardFilter;
    string? dashboardDetailSignature;
    bool dashboardSelectionSync;
    readonly Panel dashboardPageHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    Control? dashboardIconPage;
    RulesDialog? dashboardRulesPage;
    ThemeButton? dashboardIconNav, dashboardRulesNav;
    string? dashboardRulesLanguage;

    void BuildDashboard()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 1380; Height = 920; MinimumSize = new(1020, 740);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new(SizeType.Absolute, 196)); shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Percent, 100));
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(14, 26, 14, 18), ColumnCount = 1, RowCount = 9 };
        sidebar.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 72, 30, 50, 50, 50 }) sidebar.RowStyles.Add(new(SizeType.Absolute, height));
        sidebar.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 84, 50, 50 }) sidebar.RowStyles.Add(new(SizeType.Absolute, height));
        var brand = new Panel { Dock = DockStyle.Fill };
        var mark = new PictureBox { Image = AppIcon.Draw(34), Size = new(34, 34), Location = new(12, 0), SizeMode = PictureBoxSizeMode.Zoom };
        mark.Disposed += (_, _) => mark.Image?.Dispose(); brand.Controls.Add(mark);
        brand.Controls.Add(new Label { Text = "TrayPilot", AutoSize = true, Location = new(56, 3), Font = new(Font.FontFamily, 14, FontStyle.Bold) });
        sidebar.Controls.Add(brand, 0, 0);
        sidebar.Controls.Add(new Label { Text = "workspace", Dock = DockStyle.Fill, Padding = new(12, 0, 0, 0), ForeColor = UiTheme.Muted }, 0, 1);
        Button Nav(string key, Action handler, bool active = false)
        {
            var button = UiTheme.Button(key, active); button.Dock = DockStyle.Fill; button.AutoSize = false;
            ((ThemeButton)button).Glyph = key;
            button.MinimumSize = Size.Empty; button.Margin = new(0, 0, 0, 6); button.TextAlign = ContentAlignment.MiddleLeft;
            button.Click += (_, _) => handler(); return button;
        }
        var currentNav = Nav("iconManagement", ShowIconsPage);
        dashboardIconNav = (ThemeButton)currentNav;
        ((ThemeButton)currentNav).Selected = true; sidebar.Controls.Add(currentNav, 0, 2);
        // Use an outlined selection here; the primary color is reserved for actions.
        sidebar.Controls[sidebar.Controls.Count - 1].ForeColor = UiTheme.Accent;
        var rulesNav = Nav("hideRules", EditRules); dashboardRulesNav = (ThemeButton)rulesNav; sidebar.Controls.Add(rulesNav, 0, 3);
        var systemNav = Nav("systemIcons", ShowSystemIcons); dashboardSystemNav = (ThemeButton)systemNav; sidebar.Controls.Add(systemNav, 0, 4);
        var health = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(12), Margin = new(0, 0, 0, 12) };
        dashboardHealth.Font = new(Font.FontFamily, 9); health.Controls.Add(dashboardHealth); sidebar.Controls.Add(health, 0, 6);
        var settingsNav = Nav("settings", ShowSettings); dashboardSettingsNav = (ThemeButton)settingsNav; sidebar.Controls.Add(settingsNav, 0, 7);
        var aboutNav = Nav("about", ShowAbout); dashboardAboutNav = (ThemeButton)aboutNav; sidebar.Controls.Add(aboutNav, 0, 8);
        shell.Controls.Add(sidebar, 0, 0);

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 26, 28, 14), ColumnCount = 1, RowCount = 5 };
        foreach (int height in new[] { 78, 42 }) page.RowStyles.Add(new(SizeType.Absolute, height));
        page.RowStyles.Add(new(SizeType.Percent, 100)); page.RowStyles.Add(new(SizeType.Absolute, 44)); page.RowStyles.Add(new(SizeType.Absolute, 27));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.RowStyles.Add(new(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new(SizeType.Percent, 100)); heading.ColumnStyles.Add(new(SizeType.AutoSize));
        var titles = new Panel { Dock = DockStyle.Fill };
        titles.Margin = Padding.Empty;
        titles.Controls.Add(new Label { Text = "dashboardSubtitle", Location = new(0, 48), AutoSize = true, ForeColor = UiTheme.Muted });
        titles.Controls.Add(new Label { Text = "iconManagement", Location = new(0, 3), AutoSize = true, Font = new(Font.FontFamily, 21, FontStyle.Bold) });
        heading.Controls.Add(titles, 0, 0);
        var toolbar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new(0, 6, 0, 0) };
        foreach (var theme in new[] { "light", "dark" })
        {
            var choice = UiTheme.Button(theme == "light" ? "lightTheme" : "darkTheme");
            choice.MinimumSize = new(60, 36); choice.Padding = new(8, 4, 8, 4); choice.Tag = theme;
            choice.Click += (_, _) => SetDashboardTheme(theme); dashboardThemes.Add(choice); toolbar.Controls.Add(choice);
        }
        var restore = UiTheme.Button("restoreAll"); restore.Click += (_, _) => RunAsyncAction(() => controller.RestoreManagedAsync(temporarilyShow: true)); toolbar.Controls.Add(restore);
        heading.Controls.Add(toolbar, 1, 0); page.Controls.Add(heading, 0, 0);
        dashboardSummary.ForeColor = UiTheme.Muted; dashboardSummary.Margin = Padding.Empty; page.Controls.Add(dashboardSummary, 0, 1);

        dashboardWork = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        dashboardWork.ColumnStyles.Add(new(SizeType.Percent, 100)); dashboardWork.ColumnStyles.Add(new(SizeType.Absolute, 292)); dashboardWork.RowStyles.Add(new(SizeType.Percent, 100));
        var listPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(8), Margin = Padding.Empty };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        table.RowStyles.Add(new(SizeType.Absolute, 70)); table.RowStyles.Add(new(SizeType.Absolute, 46)); table.RowStyles.Add(new(SizeType.Percent, 100)); table.RowStyles.Add(new(SizeType.Absolute, 44));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new(8, 12, 8, 12), Margin = Padding.Empty };
        searchRow.RowStyles.Add(new(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var searchBox = new SurfacePanel { Dock = DockStyle.Fill, FocusBorder = true, Padding = new(12, 9, 12, 6), Margin = new(0, 0, 10, 0) };
        search.BorderStyle = BorderStyle.None; search.Dock = DockStyle.Fill;
        search.GotFocus += (_, _) => searchBox.Invalidate(); search.LostFocus += (_, _) => searchBox.Invalidate(); searchBox.Controls.Add(search);
        searchBox.Controls.Add(new Label { Text = "search", Dock = DockStyle.Left, Width = L.Current == "en-US" ? 66 : 48, ForeColor = UiTheme.Muted }); searchRow.Controls.Add(searchBox, 0, 0);
        var refresh = UiTheme.Button("refresh"); refresh.Click += async (_, _) => { if (!busy && !closing) await RefreshAsync(); }; searchRow.Controls.Add(refresh, 1, 0);
        var views = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var listMode = new RadioButton { Text = "listLayout", Appearance = Appearance.Button, AutoSize = true, Checked = true };
        UiTheme.Toggle(listMode); UiTheme.Toggle(layoutMode); views.Controls.Add(listMode); views.Controls.Add(layoutMode); searchRow.Controls.Add(views, 2, 0); table.Controls.Add(searchRow, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new(14, 0, 0, 5), Margin = Padding.Empty };
        foreach (var (key, value) in new[] { ("filterAll", 0), ("filterVisible", 1), ("filterHidden", 2) })
        {
            var radio = new RadioButton { Text = key, Appearance = Appearance.Button, AutoSize = true, Checked = value == 0 };
            UiTheme.Toggle(radio); radio.CheckedChanged += (_, _) => { if (!radio.Checked) return; dashboardFilter = value; renderedSearch = null; RenderList(); };
            dashboardFilters.Add(radio); filters.Controls.Add(radio);
        }
        dashboardSelectAll.CheckedChanged += (_, _) => { if (dashboardSelectionSync) return; bool selected = dashboardSelectAll.Checked; foreach (ListViewItem item in list.Items) item.Selected = selected; UpdateDashboard(); };
        filters.Controls.Add(dashboardSelectAll);
        table.Controls.Add(filters, 0, 1);
        list.BorderStyle = BorderStyle.None; list.BackColor = UiTheme.Surface; list.ForeColor = UiTheme.Ink;
        list.Columns.Add("dashboardIconName", 215); list.Columns.Add("iconState", 94); list.Columns.Add("matchingRules", 125); list.Columns.Add("process", 110); list.Columns.Add("dashboardAction", 68);
        list.SizeChanged += (_, _) => UpdateDashboardColumns();
        list.ShowItemToolTips = true;
        list.CheckBoxes = true;
        list.ColumnClick += (_, e) => SortDashboard(e.Column);
        list.ItemChecked += (_, e) => { if (dashboardSelectionSync) return; dashboardSelectionSync = true; try { e.Item.Selected = e.Item.Checked; } finally { dashboardSelectionSync = false; } UpdateDashboard(); };
        list.ItemSelectionChanged += (_, e) => { if (dashboardSelectionSync || e.Item is not { } item) return; dashboardSelectionSync = true; try { item.Checked = e.IsSelected; } finally { dashboardSelectionSync = false; } };
        list.MouseClick += (_, e) =>
        {
            if (closing || ((TrayListView)list).ItemAt(e.Location)?.Tag is not TrayEntry entry) return;
            if (e.Button == MouseButtons.Right) ShowItemMenu(entry, e.Location);
            else if (e.Button == MouseButtons.Left && list.View == View.Details && !busy && list.HitTest(e.Location).SubItem is { } sub && list.HitTest(e.Location).Item?.SubItems.IndexOf(sub) == 4) ToggleEntry(entry);
        };
        list.MouseDoubleClick += (_, e) => { if (!busy && !closing && e.Button == MouseButtons.Left && ModifierKeys == Keys.None && ((TrayListView)list).ItemAt(e.Location)?.Tag is TrayEntry entry) ToggleEntry(entry); };
        list.SelectedIndexChanged += (_, _) => UpdateDashboard();
        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; listHost.Controls.Add(list);
        dashboardEmpty.ForeColor = UiTheme.Muted; dashboardEmpty.Visible = false; listHost.Controls.Add(dashboardEmpty); table.Controls.Add(listHost, 0, 2);
        dashboardResults.Padding = new(14, 0, 8, 0); dashboardResults.ForeColor = UiTheme.Muted; table.Controls.Add(dashboardResults, 0, 3);
        listPanel.Controls.Add(table); dashboardWork.Controls.Add(listPanel, 0, 0);
        dashboardDetailPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(16), Margin = new(20, 0, 0, 0) };
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        detailLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new(SizeType.Absolute, 42)); detailLayout.RowStyles.Add(new(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new(SizeType.Absolute, 152));
        detailLayout.Controls.Add(new Label { Text = "iconDetailsHeading", Dock = DockStyle.Fill, Font = new(Font.FontFamily, 10, FontStyle.Bold) }, 0, 0);
        detailLayout.Controls.Add(dashboardDetails, 0, 1);
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        dashboardToggle.AutoSize = false; dashboardToggle.Size = new(230, 38); dashboardToggle.Click += (_, _) => { if (dashboardEntry is { } entry && !busy) ToggleEntry(entry); }; detailActions.Controls.Add(dashboardToggle);
        dashboardRule.AutoSize = false; dashboardRule.Size = new(230, 38); dashboardRule.Margin = new(0, 8, 0, 6); dashboardRule.Click += (_, _) => { if (dashboardEntry is not { } entry || busy) return; if (controller.HasRule(entry)) EditRules(); else OpenRuleEditor(entry); }; detailActions.Controls.Add(dashboardRule);
        var properties = UiTheme.Button("properties"); properties.AutoSize = false; properties.Size = new(230, 36); properties.Click += (_, _) => { if (dashboardEntry is { } entry) ShowProperties(entry); }; detailActions.Controls.Add(properties);
        detailLayout.Controls.Add(detailActions, 0, 2); dashboardDetailPanel.Controls.Add(detailLayout); dashboardWork.Controls.Add(dashboardDetailPanel, 1, 0); page.Controls.Add(dashboardWork, 0, 2);
        AddButton("hideSelected", () => ChangeSelected(true)); AddButton("restoreSelected", () => ChangeSelected(false));
        var hideRules = UiTheme.Button("hideMatchingIcons"); hideRules.Click += async (_, _) => await ApplyVisibilityPresetAsync(true); actions.Controls.Add(hideRules);
        autoRefresh.Margin = new(10, 8, 0, 0); autoRefresh.ForeColor = UiTheme.Muted; actions.Controls.Add(autoRefresh); page.Controls.Add(actions, 0, 3);
        status.ForeColor = UiTheme.Muted; status.Margin = Padding.Empty; page.Controls.Add(status, 0, 4);
        dashboardIconPage = page; dashboardPageHost.Controls.Add(page);
        shell.Controls.Add(dashboardPageHost, 1, 0); Controls.Add(shell);
        layoutMode.CheckedChanged += (_, _) => ChangeLayout(); autoRefresh.CheckedChanged += (_, _) => { UpdateTimer(); UpdateStatus(); };
        Resize += (_, _) => { if (dashboardWork == null || dashboardDetailPanel == null) return; bool show = ClientSize.Width >= 1120 * DeviceDpi / 96; dashboardDetailPanel.Visible = show; dashboardWork.ColumnStyles[1].Width = show ? 292 * DeviceDpi / 96 : 0; };
        dashboardDetails.SizeChanged += (_, _) => { foreach (Control child in dashboardDetails.Controls) if (child is Label) child.Width = Math.Max(120, dashboardDetails.ClientSize.Width - 22); };
        Disposed += (_, _) => dashboardProgramIcon.Image?.Dispose();
    }

    void ShowIconsPage()
    {
        if (busy || closing || dashboardRulesPage?.Working == true || dashboardSystemPage?.NavigationAllowed == false || !TryLeaveSettings()) return;
        DeactivateSystemPage();
        HidePreferencesPages();
        if (dashboardRulesPage != null) dashboardRulesPage.Hide();
        dashboardIconPage?.Show(); dashboardIconPage?.BringToFront();
        SetDashboardNavigation(false); RenderList(); search.Focus(); if (initialized) UpdateTimer();
    }

    void SetDashboardNavigation(bool rulesActive, bool systemActive = false, bool settingsActive = false, bool aboutActive = false)
    {
        if (dashboardIconNav != null) { dashboardIconNav.Selected = !rulesActive && !systemActive && !settingsActive && !aboutActive; dashboardIconNav.Invalidate(); }
        if (dashboardRulesNav != null) { dashboardRulesNav.Selected = rulesActive; dashboardRulesNav.Invalidate(); }
        if (dashboardSystemNav != null) { dashboardSystemNav.Selected = systemActive; dashboardSystemNav.Invalidate(); }
        if (dashboardSettingsNav != null) { dashboardSettingsNav.Selected = settingsActive; dashboardSettingsNav.Invalidate(); }
        if (dashboardAboutNav != null) { dashboardAboutNav.Selected = aboutActive; dashboardAboutNav.Invalidate(); }
    }

    void ShowRulesPage()
    {
        if (busy || closing || dashboardSystemPage?.NavigationAllowed == false || !TryLeaveSettings()) return;
        DeactivateSystemPage();
        HidePreferencesPages();
        if (dashboardRulesPage == null || dashboardRulesLanguage != L.Current)
        {
            dashboardRulesPage?.Dispose();
            dashboardRulesPage = new RulesDialog(controller, entries.ToList(), RefreshRuleDataAsync, RunRuleOperationAsync, Font,
                dashboardEntry, embedded: true, returnToIcons: ShowIconsPage);
            dashboardRulesLanguage = L.Current;
            dashboardPageHost.Controls.Add(dashboardRulesPage);
        }
        dashboardRulesPage.UpdateEntries(entries.ToList());
        dashboardIconPage?.Hide(); dashboardRulesPage.Show(); dashboardRulesPage.BringToFront();
        SetDashboardNavigation(true); dashboardRulesPage.FocusSearch(); if (initialized) UpdateTimer();
    }

    void SetDashboardTheme(string theme)
    {
        if (busy || closing) return;
        RunAction(() => { string previous = controller.Saved.Theme; controller.Saved.Theme = theme; try { controller.Save(); } catch { controller.Saved.Theme = previous; throw; } ApplyTheme(); });
    }

    bool DashboardMatches(TrayEntry entry) => dashboardFilter == 0 || (dashboardFilter == 1 ? entry.State == 0 : entry.State == 1);

    void UpdateDashboardColumns()
    {
        if (list.Columns.Count != 5) return;
        bool english = L.Current == "en-US";
        list.Columns[1].Width = (english ? 120 : 94) * DeviceDpi / 96;
        list.Columns[2].Width = (english ? 152 : 125) * DeviceDpi / 96;
        list.Columns[3].Width = 110 * DeviceDpi / 96;
        list.Columns[4].Width = (english ? 80 : 68) * DeviceDpi / 96;
        list.Columns[0].Width = Math.Max(160 * DeviceDpi / 96, list.ClientSize.Width - list.Columns.Cast<ColumnHeader>().Skip(1).Sum(x => x.Width) - SystemInformation.VerticalScrollBarWidth - 4);
    }

    Bitmap DashboardListImage(TrayEntry entry)
    {
        var canvas = new Bitmap(40, 56);
        using var graphics = Graphics.FromImage(canvas);
        using var image = imageCache.Create(entry with { State = 0 }, 32);
        graphics.DrawImageUnscaled(image, 4, 12);
        return canvas;
    }

    string DashboardRuleName(TrayEntry entry) => !controller.HasRule(entry) ? "—" : L.T(controller.HasRule(entry.Path) ? "dashboardProgramRule" : "dashboardSingleRule");

    void UpdateDashboard()
    {
        int ruleCount = controller.Saved.HiddenPaths.Count + controller.Saved.HiddenIcons.Count;
        dashboardSummary.Text = L.F("dashboardSummary", entries.Count, entries.Count(x => x.State == 0), entries.Count(x => x.State == 1), ruleCount);
        foreach (var button in dashboardThemes) { ((ThemeButton)button).Selected = (button.Tag as string) == (UiTheme.Dark ? "dark" : "light"); button.Invalidate(); }
        dashboardHealth.Text = L.T(controller.Saved.RulesPaused ? "autoHidePaused" : ruleCount == 0 ? "dashboardNoRules" : "dashboardRulesRunning") + "\n" + L.F("dashboardRuleCount", ruleCount);
        dashboardResults.Text = L.F("dashboardResultCount", list.Items.Count, list.SelectedItems.Count);
        dashboardSelectionSync = true;
        try
        {
            dashboardSelectAll.Enabled = list.Items.Count != 0;
            dashboardSelectAll.CheckState = list.SelectedItems.Count == 0 ? CheckState.Unchecked : list.SelectedItems.Count == list.Items.Count ? CheckState.Checked : CheckState.Indeterminate;
        }
        finally { dashboardSelectionSync = false; }
        dashboardEmpty.Text = L.T(entries.Count == 0 ? "dashboardEmpty" : "dashboardNoResults"); dashboardEmpty.Visible = list.Items.Count == 0;
        if (dashboardEmpty.Visible) dashboardEmpty.BringToFront();
        var selected = list.FocusedItem?.Selected == true ? list.FocusedItem : list.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
        var entry = selected?.Tag as TrayEntry;
        dashboardEntry = entry;
        dashboardToggle.Enabled = entry != null && !busy; dashboardRule.Enabled = entry != null && !busy;
        var iconSignature = entry?.IconSnapshot is { Length: > 0 } snapshot ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot)) : "";
        var signature = entry == null ? "empty:" + L.Current : entry.Key + ":" + entry.State + ":" + entry.Path + ":" + entry.Name + ":" + controller.HasRule(entry) + ":" + L.Current + ":" + UiTheme.Dark + ":" + iconSignature;
        if (dashboardDetailSignature == signature) return; dashboardDetailSignature = signature;
        dashboardDetails.SuspendLayout();
        while (dashboardDetails.Controls.Count > 0) { var child = dashboardDetails.Controls[0]; dashboardDetails.Controls.RemoveAt(0); if (child != dashboardProgramIcon) child.Dispose(); }
        void TextLine(string text, bool heading = false)
        {
            var label = new Label { Text = text, AutoSize = false, Width = Math.Max(120, dashboardDetails.ClientSize.Width - 22), Height = heading ? 30 : 54,
                Font = heading ? new(Font.FontFamily, 10, FontStyle.Bold) : Font, ForeColor = heading ? UiTheme.Ink : UiTheme.Muted, Margin = new(0, heading ? 10 : 0, 0, 4), AutoEllipsis = true };
            label.Height = heading ? 25 : text.Contains('\n') ? 64 : text.Contains('\\') ? 52 : 28;
            if (heading) label.Disposed += (_, _) => label.Font.Dispose();
            dashboardDetails.Controls.Add(label);
        }
        if (entry == null) { TextLine(L.T("dashboardSelectHint")); }
        else
        {
            dashboardProgramIcon.Image?.Dispose(); dashboardProgramIcon.Image = imageCache.Create(entry with { State = 0 }, 40); dashboardDetails.Controls.Add(dashboardProgramIcon);
            TextLine(entry.Name, true); TextLine(L.T(entry.State == 1 ? "dashboardHiddenHint" : "dashboardVisibleHint"));
            TextLine(L.T("processName"), true); TextLine(Path.GetFileName(entry.Path));
            TextLine(L.T("fullPath"), true); TextLine(entry.Path);
            var copy = UiTheme.Button("copyFullPath"); copy.Text = L.T("copyFullPath"); copy.Click += (_, _) => { try { Clipboard.SetText(entry.Path); status.Text = L.T("pathCopied"); } catch (System.Runtime.InteropServices.ExternalException) { status.Text = L.T("copyFailed"); } }; dashboardDetails.Controls.Add(copy);
            TextLine(L.T("autoHideRule"), true); TextLine(L.T(controller.HasRule(entry) ? "dashboardRuleHint" : "dashboardManualHint"));
            dashboardToggle.Text = L.T(entry.State == 1 ? "showIcons" : "hideIcons"); dashboardRule.Text = L.T(controller.HasRule(entry) ? "hideRules" : "addIconRule");
        }
        dashboardDetails.ResumeLayout();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (dashboardSystemPage?.Visible == true || dashboardSettingsPage?.Visible == true || dashboardAboutPage?.Visible == true) return base.ProcessCmdKey(ref msg, keyData);
        if (dashboardRulesPage?.Visible == true && dashboardRulesPage.HandleShortcut(keyData)) return true;
        if (keyData == (Keys.Control | Keys.Shift | Keys.D1)) { SortDashboard(0); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D2)) { SortDashboard(1); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D3)) { SortDashboard(2); return true; }
        if (keyData == (Keys.Control | Keys.K)) { search.Focus(); return true; }
        if (keyData == Keys.Escape && search.Focused && search.Text.Length > 0) { search.Clear(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
