namespace TrayPilot;

internal sealed class SettingsPage : UserControl
{
    SettingsPreferences baseline, draft;
    readonly Func<SettingsPreferences, bool, Task> save;
    readonly Action<string> preview;
    readonly Action<SettingsPreferences, SettingsPreferences> applied;
    readonly TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new(28, 24, 28, 14) };
    readonly Panel body = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly Button saveButton, cancelButton, errorSummary;
    readonly Label footer = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly List<RadioButton> tabs = new();
    readonly List<ThemePreviewButton> themeButtons = new();
    readonly Label[] shortcutErrors = new Label[3];
    readonly Control?[] shortcutTargets = new Control?[3];
    Label? behavior, startupHelp;
    ThemeSwitch? startupSwitch;
    string startupError = "", operationError = "";
    bool working, building;
    int view;
    internal bool Dirty => draft != baseline;
    internal bool Working => working;
    internal SettingsPreferences Draft => draft;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ReadOnlyPreview { get; set; }

    internal SettingsPage(SettingsPreferences initial, Func<SettingsPreferences, bool, Task> save, Action<string> preview,
        Action<SettingsPreferences, SettingsPreferences> applied, Font font)
    {
        baseline = draft = initial; this.save = save; this.preview = preview; this.applied = applied;
        Font = font; Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi;
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 78, 48, 0 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 64));
        root.Controls.Add(UiTheme.Heading(L.T("settings"), L.T("settingsDashboardSubtitle"), font), 0, 0);
        var tabBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var (key, index) in new[] { ("settingsGeneral", 0), ("settingsAppearanceLanguage", 1), ("settingsShortcuts", 2) })
        {
            var radio = new RadioButton { Text = L.T(key), Appearance = Appearance.Button, AutoSize = true, Checked = index == 0 };
            UiTheme.Toggle(radio); radio.CheckedChanged += (_, _) => { if (radio.Checked && !building) { view = index; BuildView(); } };
            radio.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End)
                { e.Handled = true; int next = e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? 2 : (index + (e.KeyCode == Keys.Right ? 1 : 2)) % 3; tabs[next].Checked = true; tabs[next].Focus(); }
            };
            tabs.Add(radio); tabBar.Controls.Add(radio);
        }
        root.Controls.Add(tabBar, 0, 1);
        errorSummary = UiTheme.Button(""); errorSummary.Name = "settingsErrorSummary"; errorSummary.Dock = DockStyle.Fill; errorSummary.AutoSize = false;
        errorSummary.TextAlign = ContentAlignment.MiddleLeft; errorSummary.AccessibleRole = AccessibleRole.Alert;
        errorSummary.Click += (_, _) => { var invalid = draft.InvalidHotkeys; if (invalid.Length > 0) { SelectView(2); shortcutTargets[invalid[0]]?.Focus(); } };
        root.Controls.Add(errorSummary, 0, 2); root.Controls.Add(body, 0, 3);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new(0, 14, 0, 0) };
        bar.ColumnStyles.Add(new(SizeType.Percent, 100)); bar.ColumnStyles.Add(new(SizeType.AutoSize)); bar.RowStyles.Add(new(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        cancelButton = UiTheme.Button(L.T("settingsCancelChanges")); cancelButton.Name = "settingsCancel"; cancelButton.Click += (_, _) => CancelChanges();
        saveButton = UiTheme.Button(L.T("settingsSaveChanges"), true); saveButton.Name = "settingsSave"; saveButton.Click += async (_, _) => await SaveChangesAsync();
        buttons.Controls.Add(cancelButton); buttons.Controls.Add(saveButton); bar.Controls.Add(footer, 0, 0); bar.Controls.Add(buttons, 1, 0); root.Controls.Add(bar, 0, 4);
        Controls.Add(root); BuildView();
    }
    internal void SelectView(int index) { if (index is >= 0 and <= 2) tabs[index].Checked = true; }
    internal void SetDraft(SettingsPreferences value) { bool themeChanged = draft.Theme != value.Theme; draft = value; operationError = ""; if (themeChanged) preview(draft.Theme); BuildView(); }
    internal void SetStartup(bool? enabled, string error = "")
    {
        if (IsDisposed) return;
        baseline = baseline with { Startup = enabled }; draft = draft with { Startup = enabled }; startupError = error;
        if (startupSwitch != null && !startupSwitch.IsDisposed) { startupSwitch.Checked = enabled == true; startupSwitch.Enabled = enabled.HasValue && !working; }
        if (startupHelp != null && !startupHelp.IsDisposed) startupHelp.Text = L.T(enabled.HasValue ? "settingsStartupHelp" : error.Length > 0 ? "settingsStartupUnavailable" : "settingsStartupLoading");
        Render();
    }
    internal void CancelChanges()
    {
        if (working) return; draft = baseline; operationError = ""; preview(baseline.Theme); BuildView();
    }
    internal void Reload(SettingsPreferences value) { if (working || Dirty) return; baseline = draft = value; startupError = operationError = ""; BuildView(); }
    internal bool CanLeave(Func<DialogResult> decision)
    {
        if (working) return false;
        if (!Dirty) return true;
        if (decision() != DialogResult.Yes) return false;
        CancelChanges(); return true;
    }
    internal async Task<bool> SaveChangesAsync()
    {
        if (working || !Dirty || ReadOnlyPreview || baseline.Startup == null && startupError.Length == 0) return false;
        if (InvokeRequired) throw new InvalidOperationException("Settings saves must start on the window thread.");
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        if (draft.InvalidHotkeys.Length > 0) { Render(); errorSummary.Focus(); return false; }
        var before = baseline; var submitted = draft;
        working = true; Render(); body.Enabled = false; foreach (var tab in tabs) tab.Enabled = false;
        try
        {
            await Task.Yield();
            await save(submitted, submitted.Startup != before.Startup);
            if (IsDisposed) return true;
            baseline = draft = submitted; operationError = "";
            working = false;
            applied(before, submitted);
            if (!IsDisposed) { footer.Text = L.T("settingsSaved"); UiTheme.Apply(this); }
            return true;
        }
        catch (Exception ex) { if (!IsDisposed) operationError = ex.Message; return false; }
        finally
        {
            working = false;
            if (!IsDisposed) { body.Enabled = true; foreach (var tab in tabs) tab.Enabled = true; Render(); if (operationError.Length > 0) errorSummary.Focus(); }
        }
    }
    void Edit(Func<SettingsPreferences, SettingsPreferences> edit)
    { if (building || working) return; draft = edit(draft); operationError = ""; Render(); }
    void BuildView()
    {
        building = true; body.SuspendLayout();
        while (body.Controls.Count > 0) { var control = body.Controls[0]; body.Controls.RemoveAt(0); control.Dispose(); }
        themeButtons.Clear(); Array.Clear(shortcutErrors); Array.Clear(shortcutTargets); behavior = startupHelp = null; startupSwitch = null;
        if (view == 0) BuildGeneral(); else if (view == 1) BuildAppearance(); else BuildShortcuts();
        body.ResumeLayout(true); building = false; UiTheme.Apply(this); Render();
    }
    SurfacePanel Section(string title, int height, out TableLayoutPanel rows)
    {
        var surface = new SurfacePanel { Height = height, Padding = new(18, 14, 18, 12) };
        rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        rows.ColumnStyles.Add(new(SizeType.Percent, 100)); rows.RowStyles.Add(new(SizeType.Absolute, 34));
        rows.Controls.Add(new Label { Text = L.T(title), UseMnemonic = false, Dock = DockStyle.Fill, Font = new(Font.FontFamily, 10, FontStyle.Bold) }, 0, 0);
        surface.Controls.Add(rows); return surface;
    }
    ThemeSwitch ToggleRow(TableLayoutPanel rows, string title, string help, string name, bool enabled, Action<bool> update, out Label description)
    {
        int row = rows.RowCount++; rows.RowStyles.Add(new(SizeType.Percent, 100));
        var item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = new(0, 8, 0, 0) };
        item.ColumnStyles.Add(new(SizeType.Percent, 100)); item.ColumnStyles.Add(new(SizeType.Absolute, 62)); item.RowStyles.Add(new(SizeType.Absolute, 28)); item.RowStyles.Add(new(SizeType.Percent, 100));
        item.Controls.Add(new Label { Text = L.T(title), Dock = DockStyle.Fill }, 0, 0);
        description = new Label { Text = L.T(help), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }; item.Controls.Add(description, 0, 1);
        var toggle = new ThemeSwitch { Name = name, Checked = enabled, AutoCheck = true, AccessibleName = L.T(title), AccessibleDescription = L.T(help), Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new(0, 6, 0, 0) };
        toggle.CheckedChanged += (_, _) => update(toggle.Checked); item.Controls.Add(toggle, 1, 0); item.SetRowSpan(toggle, 2); rows.Controls.Add(item, 0, row); return toggle;
    }
    void BuildGeneral()
    {
        var work = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        work.ColumnStyles.Add(new(SizeType.Percent, 100)); work.ColumnStyles.Add(new(SizeType.Absolute, 280)); work.RowStyles.Add(new(SizeType.Percent, 100));
        var document = new DashboardDocument();
        var startupPanel = Section("settingsStartupSection", 230, out var startupRows);
        startupSwitch = ToggleRow(startupRows, "startWithWindows", "settingsStartupHelp", "startup", draft.Startup == true, value => Edit(p => p with { Startup = value }), out startupHelp);
        startupSwitch.Enabled = draft.Startup.HasValue;
        startupHelp.Text = L.T(draft.Startup.HasValue ? "settingsStartupHelp" : startupError.Length > 0 ? "settingsStartupUnavailable" : "settingsStartupLoading");
        ToggleRow(startupRows, "showOwnTrayIcon", "settingsTrayHelp", "showOwnTrayIcon", draft.ShowTrayIcon, value => Edit(p => p with { ShowTrayIcon = value }), out _); document.Add(startupPanel);
        var exitPanel = Section("settingsExitSection", 230, out var exitRows);
        ToggleRow(exitRows, "closeToTray", "settingsCloseHelp", "closeToTray", draft.CloseToTray, value => Edit(p => p with { CloseToTray = value }), out _);
        ToggleRow(exitRows, "restoreIconsOnExit", "settingsRestoreHelp", "restoreIconsOnExit", draft.RestoreOnExit, value => Edit(p => p with { RestoreOnExit = value }), out _); document.Add(exitPanel);
        document.Add(new Label { Text = L.T("settingsCloseNotice"), Height = 62, ForeColor = UiTheme.Muted, Padding = new(8) }); work.Controls.Add(document, 0, 0);
        var previewPanel = Section("settingsBehaviorPreview", 450, out var previewRows); previewPanel.Dock = DockStyle.Top; previewPanel.Margin = new(18, 0, 0, 0);
        previewRows.RowCount++; previewRows.RowStyles.Add(new(SizeType.Percent, 100)); behavior = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }; previewRows.Controls.Add(behavior, 0, 1); work.Controls.Add(previewPanel, 1, 0);
        void ResizePreview() { bool show = body.ClientSize.Width >= 860 * DeviceDpi / 96; previewPanel.Visible = show; work.ColumnStyles[1].Width = show ? 280 * DeviceDpi / 96 : 0; }
        work.SizeChanged += (_, _) => ResizePreview(); body.Controls.Add(work); ResizePreview();
    }
    void BuildAppearance()
    {
        var document = new DashboardDocument();
        var surface = Section("appearance", 260, out var rows);
        rows.RowCount++; rows.RowStyles.Add(new(SizeType.Percent, 100));
        var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) choices.ColumnStyles.Add(new(SizeType.Percent, 100)); choices.RowStyles.Add(new(SizeType.Percent, 100));
        int column = 0;
        foreach (string mode in new[] { "light", "dark", "system" })
        {
            var button = new ThemePreviewButton(mode) { Dock = DockStyle.Fill, Margin = new(0, 12, 12, 12) };
            button.Click += (_, _) => { Edit(p => p with { Theme = mode }); preview(mode); Render(); };
            themeButtons.Add(button); choices.Controls.Add(button, column++, 0);
        }
        rows.Controls.Add(choices, 0, 1); document.Add(surface);
        var languagePanel = Section("language", 160, out var languageRows);
        var combo = new ThemeComboBox { Name = "language", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L.T("language") };
        combo.Items.AddRange(L.Packs().Select(pack => new MainForm.LanguageChoice(pack.Key, pack.Value.Name)).ToArray());
        combo.SelectedItem = combo.Items.Cast<MainForm.LanguageChoice>().FirstOrDefault(choice => choice.Code == draft.Language) ?? combo.Items[0];
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is MainForm.LanguageChoice choice) Edit(p => p with { Language = choice.Code }); };
        languageRows.RowCount += 2; languageRows.RowStyles.Add(new(SizeType.Absolute, 42)); languageRows.RowStyles.Add(new(SizeType.Percent, 100));
        languageRows.Controls.Add(combo, 0, 1); languageRows.Controls.Add(new Label { Text = L.T("settingsLanguageHelp"), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 2); document.Add(languagePanel);
        document.Add(new Label { Text = L.T("settingsThemeHelp"), Height = 64, Padding = new(8), ForeColor = UiTheme.Muted }); body.Controls.Add(document);
    }
    void BuildShortcuts()
    {
        var document = new DashboardDocument(); var surface = Section("settingsShortcuts", 448, out var rows);
        var labels = new[] { "openMainWindowHotkey", "showAllIconsHotkey", "hideMatchingIconsHotkey" };
        var helps = new[] { "settingsMainHotkeyHelp", "settingsShowHotkeyHelp", "settingsHideHotkeyHelp" };
        var names = new[] { "mainHotkey", "showAllHotkey", "hideRulesHotkey" };
        for (int i = 0; i < 3; i++)
        {
            int index = i, row = rows.RowCount++; rows.RowStyles.Add(new(SizeType.Percent, 100));
            var item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty, Padding = new(0, 7, 0, 0) };
            item.ColumnStyles.Add(new(SizeType.Percent, 100)); item.ColumnStyles.Add(new(SizeType.Absolute, 220)); item.ColumnStyles.Add(new(SizeType.Absolute, 60));
            item.RowStyles.Add(new(SizeType.Absolute, 38)); item.RowStyles.Add(new(SizeType.Absolute, 40)); item.RowStyles.Add(new(SizeType.Percent, 100));
            item.Controls.Add(new Label { Text = L.T(labels[i]), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var edit = UiTheme.Button(FormatKeys(draft.Hotkeys[i].Keys)); edit.Name = names[i]; edit.Dock = DockStyle.Fill; edit.AutoSize = false; edit.AccessibleName = L.T(labels[i]) + " · " + L.T("settingsEditHotkey");
            edit.Click += (_, _) =>
            {
                using var dialog = new HotkeyEditor(L.T(labels[index]), draft.Hotkeys[index].Keys, Font);
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { EditHotkey(index, draft.Hotkeys[index].Enabled, dialog.Value); edit.Text = FormatKeys(dialog.Value); }
            };
            shortcutTargets[i] = edit; item.Controls.Add(edit, 1, 0);
            var toggle = new ThemeSwitch { AutoCheck = true, Name = names[i] + "Enabled", Checked = draft.Hotkeys[i].Enabled, AccessibleName = L.T(labels[i]), Anchor = AnchorStyles.Right, Margin = new(0, 3, 0, 0) };
            toggle.CheckedChanged += (_, _) => EditHotkey(index, toggle.Checked, draft.Hotkeys[index].Keys); item.Controls.Add(toggle, 2, 0);
            var help = new Label { Text = L.T(helps[i]), Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }; item.Controls.Add(help, 0, 1); item.SetColumnSpan(help, 3);
            shortcutErrors[i] = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick }; item.Controls.Add(shortcutErrors[i], 0, 2); item.SetColumnSpan(shortcutErrors[i], 3); rows.Controls.Add(item, 0, row);
        }
        document.Add(surface); document.Add(new Label { Text = L.T("settingsHotkeyHelp"), Height = 92, Padding = new(8), ForeColor = UiTheme.Muted }); body.Controls.Add(document);
    }
    internal void EditHotkey(int index, bool enabled, Keys keys) => Edit(p => index switch
    { 0 => p with { MainEnabled = enabled, MainKeys = keys }, 1 => p with { ShowEnabled = enabled, ShowKeys = keys }, _ => p with { HideEnabled = enabled, HideKeys = keys } });
    internal static string FormatKeys(Keys keys) => new KeysConverter().ConvertToString(keys) ?? keys.ToString();
    internal void Render()
    {
        if (IsDisposed) return;
        var invalid = draft.InvalidHotkeys;
        string error = operationError.Length > 0 ? operationError : invalid.Length > 0 ? L.T("settingsHotkeyConflict") : "";
        errorSummary.Visible = error.Length > 0;
        root.RowStyles[2].Height = error.Length > 0 ? 62 * DeviceDpi / 96 : 0;
        errorSummary.Text = error; errorSummary.AccessibleDescription = error; errorSummary.ForeColor = UiTheme.Dark ? Color.FromArgb(255, 170, 170) : Color.Firebrick;
        for (int i = 0; i < shortcutErrors.Length; i++) if (shortcutErrors[i] is { IsDisposed: false } label)
        { label.Text = invalid.Contains(i) ? L.T("settingsHotkeyFieldError") : ""; label.ForeColor = errorSummary.ForeColor; if (shortcutTargets[i] != null) shortcutTargets[i]!.AccessibleDescription = label.Text; }
        foreach (var button in themeButtons) { button.Selected = button.Mode == draft.Theme; button.Invalidate(); }
        saveButton.Enabled = Dirty && !working && (baseline.Startup.HasValue || startupError.Length > 0);
        cancelButton.Enabled = Dirty && !working; saveButton.Text = L.T(working ? "settingsSaving" : "settingsSaveChanges");
        footer.Text = L.T(working ? "settingsSaving" : Dirty ? "settingsUnsaved" : "settingsSaved"); footer.ForeColor = UiTheme.Muted;
        if (behavior != null)
        {
            behavior.Text = L.T("settingsPreviewLogin") + "\n" + L.T(draft.Startup == true ? "settingsPreviewAuto" : draft.Startup == false ? "settingsPreviewManual" : "settingsStartupLoading")
                + "\n\n" + L.T("settingsPreviewClose") + "\n" + L.T(draft.CloseToTray ? "settingsPreviewBackground" : "settingsPreviewExit")
                + "\n\n" + L.T("settingsPreviewQuit") + "\n" + L.T(draft.RestoreOnExit ? "settingsPreviewRestore" : "settingsPreviewKeep")
                + "\n\n" + L.T("settingsPreviewAccess") + "\n" + L.T(draft.ShowTrayIcon ? "settingsPreviewTray" : "settingsPreviewRelaunch")
                + (draft.MainEnabled ? "\n" + FormatKeys(draft.MainKeys) : "");
            behavior.ForeColor = UiTheme.Muted;
        }
    }
}
