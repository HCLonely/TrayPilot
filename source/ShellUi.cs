namespace TrayPilot;

internal sealed partial class MainForm
{
    readonly PagedTrayMenu trayMenu = new() { ShowCheckMargin = true, ShowImageMargin = true };
    readonly MenuStrip menuBar = new();
    readonly Dictionary<Control, string> captions = new();
    readonly List<string> columnCaptions = new();
    NotifyIcon? trayIcon;
    GlobalHotkey? showAllHotkey, hideRulesHotkey, mainHotkey;
    int trayPage, trayPages = 1;
    bool trayPagePending;
    internal const int TrayPageSize = 10;
    internal sealed record LanguageChoice(string Code, string Name) { public override string ToString() => Name; }
    bool exitRequested, hotkeyWarning;

    void SetupShell(bool initialize)
    {
        void Capture(Control control)
        {
            if (control.Text.Length > 0 && control is not TextBox && control is not ListView) captions[control] = control.Text;
            foreach (Control child in control.Controls) Capture(child);
        }
        Capture(this);
        columnCaptions.AddRange(list.Columns.Cast<ColumnHeader>().Select(x => x.Text));
        menuBar.BackColor = Color.White; menuBar.ForeColor = UiTheme.Ink;
        menuBar.Padding = new(24, 6, 24, 6); menuBar.RenderMode = ToolStripRenderMode.Professional;
        Controls.Add(menuBar); MainMenuStrip = menuBar;
        ApplyLanguage();
        trayMenu.Opening += (_, e) =>
        {
            BuildTrayMenu();
            // An initially empty dropdown arrives with Cancel=true. Recompute it
            // after populating, otherwise the very first right-click is discarded.
            e.Cancel = trayMenu.Items.Count == 0;
        };
        trayMenu.PageRequested += MoveTrayPage;
        if (initialize) InitializeTray();
    }

    void InitializeTray()
    {
        if (trayIcon != null) return;
        _ = Handle;
        trayIcon = new NotifyIcon { Icon = AppIcon.Create(), Text = "TrayPilot", ContextMenuStrip = trayMenu };
        trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { trayMenu.Close(); OpenMainWindow(); } };
        trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { trayMenu.Close(); OpenMainWindow(); } };
        trayIcon.Visible = controller.Saved.ShowTrayIcon;
        RegisterShortcuts();
        UpdateStatus();
    }

    void ApplyLanguage()
    {
        foreach (var caption in captions) caption.Key.Text = L.T(caption.Value);
        search.PlaceholderText = L.T("搜索软件名称、进程或路径");
        for (int i = 0; i < columnCaptions.Count; i++) list.Columns[i].Text = L.T(columnCaptions[i]);
        while (menuBar.Items.Count > 0) { var item = menuBar.Items[0]; menuBar.Items.RemoveAt(0); item.Dispose(); }
        menuBar.Items.Add(L.T("隐藏规则"), null, (_, _) => EditRules());
        menuBar.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
        menuBar.Items.Add(L.T("关于"), null, (_, _) => ShowAbout());
        menuBar.Items.Add(L.T("退出"), null, (_, _) => RequestExit());
        RenderList(); UpdateStatus(); UiTheme.Apply(menuBar);
    }

    void ClearTrayMenu()
    {
        while (trayMenu.Items.Count > 0)
        {
            var item = trayMenu.Items[0]; trayMenu.Items.RemoveAt(0);
            item.Image?.Dispose(); item.Dispose();
        }
    }

    void BuildTrayMenu()
    {
        ClearTrayMenu();
        trayMenu.Items.Add(L.T("打开主界面"), null, (_, _) => OpenMainWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem(L.T("托盘图标（√ 表示显示，单击切换）")) { Enabled = false });
        var groups = entries.Where(x => x.Pid != Environment.ProcessId && Scanner.SameOwner(x))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        groups = groups.Where(g => g.Any(x => Native.State(x) is 0 or 1)).ToList();
        int pages = trayPages = Math.Max(1, (groups.Count + TrayPageSize - 1) / TrayPageSize);
        trayPage = Math.Clamp(trayPage, 0, pages - 1);
        foreach (var group in groups.Skip(trayPage * TrayPageSize).Take(TrayPageSize))
        {
            var active = group.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
            if (active.Count == 0) continue;
            var entry = active[0];
            bool shown = active.All(x => x.State == 0);
            var row = new ToolStripMenuItem(entry.Name) { Checked = shown, CheckOnClick = false, Enabled = !busy,
                Image = TrayImages.Create(entry with { State = shown ? 0 : 1 }, 20), Tag = entry.Path,
                ToolTipText = entry.Path + "\n" + L.T(shown ? "单击隐藏此软件的所有托盘图标" : "单击显示此软件的所有托盘图标") };
            row.Click += (_, _) =>
            {
                trayMenu.Close();
                RunAction(() =>
                {
                    var current = entries.Where(x => string.Equals(x.Path, entry.Path, StringComparison.OrdinalIgnoreCase) && Scanner.SameOwner(x))
                        .Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
                    if (current.Count == 0) throw new IOException(L.T("此图标已失效，请刷新后重试。"));
                    ChangePaths(new[] { entry.Path }, current.All(x => x.State == 0));
                });
            };
            trayMenu.Items.Add(row);
        }
        if (pages > 1)
        {
            var previous = new ToolStripMenuItem(L.T("上一页")) { Enabled = trayPage > 0 };
            var next = new ToolStripMenuItem(L.T("下一页")) { Enabled = trayPage + 1 < pages };
            previous.Click += (_, _) => MoveTrayPage(-1); next.Click += (_, _) => MoveTrayPage(1);
            trayMenu.Items.Add(previous);
            trayMenu.Items.Add(new ToolStripMenuItem(L.F("第 {0} / {1} 页", trayPage + 1, pages)) { Enabled = false });
            trayMenu.Items.Add(next);
        }
        var self = new ToolStripMenuItem(L.T("TrayPilot（本程序）")) { Checked = controller.Saved.ShowTrayIcon, Image = AppIcon.Draw(20), Enabled = !busy };
        self.Click += (_, _) => RunAction(() =>
        {
            bool previous = controller.Saved.ShowTrayIcon;
            controller.Saved.ShowTrayIcon = !previous;
            try { controller.Save(); } catch { controller.Saved.ShowTrayIcon = previous; throw; }
            if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon;
        });
        trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add(self);
        if (groups.Count == 0) trayMenu.Items.Add(new ToolStripMenuItem(L.T("暂无可管理的图标")) { Enabled = false });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("刷新"), null, async (_, _) => { trayMenu.Close(); await RefreshAsync(); });
        var autoStart = new ToolStripMenuItem(L.T("开机启动")) { Name = "startup", CheckOnClick = false };
        try { autoStart.Checked = startup.Enabled; }
        catch (Exception ex) { autoStart.Enabled = false; autoStart.ToolTipText = ex.Message; }
        autoStart.Click += (_, _) =>
        {
            try { startup.SetEnabled(!startup.Enabled); autoStart.Checked = startup.Enabled; }
            catch (Exception ex) { MessageBox.Show(ex.Message, L.T("开机启动设置失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        trayMenu.Items.Add(autoStart);
        trayMenu.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
        trayMenu.Items.Add(L.T("关于"), null, (_, _) => ShowAbout());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("退出"), null, (_, _) => RequestExit());
        UiTheme.Apply(trayMenu);
    }

    void MoveTrayPage(int delta)
    {
        if (closing || IsDisposed) return;
        int nextPage = (int)Math.Clamp((long)trayPage + delta, 0, trayPages - 1);
        if (nextPage == trayPage) return;
        trayPage = nextPage;
        if (trayPagePending) return;
        var location = trayMenu.Location;
        trayPagePending = true;
        trayMenu.Close();
        BeginInvoke(() =>
        {
            trayPagePending = false;
            if (IsDisposed || closing) return;
            trayMenu.Show(location);
            Native.SetForegroundWindow(trayMenu.Handle);
        });
    }

    void ShowMainMenu()
    {
        if (closing || IsDisposed) return;
        itemMenu.Close();
        // ContextMenuStrip is its own top-level popup, so the main window can stay hidden.
        trayMenu.Show(Cursor.Position);
        Native.SetForegroundWindow(trayMenu.Handle);
    }

    internal void ActivateMainWindow() => OpenMainWindow();

    void OpenMainWindow()
    {
        if (IsDisposed) return;
        Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); Native.SetForegroundWindow(Handle);
    }

    internal void RequestExit()
    {
        trayMenu.Close(); exitRequested = true; Close();
    }

    void ShowAbout()
    {
        trayMenu.Close();
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.3.0";
        using var dialog = InfoDialog.Create(L.T("关于") + " TrayPilot", new Dictionary<string, string>
        {
            [L.T("程序名称")] = "TrayPilot", [L.T("版本")] = version,
            [L.T("说明")] = L.T("Windows 托盘图标管理工具，支持隐藏规则、快捷管理和多语言。"),
            [L.T("运行系统")] = Environment.OSVersion.VersionString,
            [L.T("程序路径")] = Environment.ProcessPath ?? AppContext.BaseDirectory,
            [L.T("设置目录")] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayPilot"),
            [L.T("语言包目录")] = L.Folder
        }, Font, about: true);
        timer.Stop();
        try { if (Visible) dialog.ShowDialog(this); else dialog.ShowDialog(); } finally { UpdateTimer(); }
    }

    void RegisterShortcuts()
    {
        showAllHotkey ??= new GlobalHotkey(Handle, 0x4A01);
        mainHotkey ??= new GlobalHotkey(Handle, 0x4A11);
        hideRulesHotkey ??= new GlobalHotkey(Handle, 0x4A21);
        showAllHotkey.Dispose(); mainHotkey.Dispose(); hideRulesHotkey.Dispose();
        hotkeyWarning = !showAllHotkey.TrySet(controller.Saved.ShowAllHotkeyEnabled, (Keys)controller.Saved.ShowAllHotkey);
        hotkeyWarning |= !mainHotkey.TrySet(controller.Saved.MainHotkeyEnabled, (Keys)controller.Saved.MainHotkey);
        hotkeyWarning |= !hideRulesHotkey.TrySet(controller.Saved.HideRulesHotkeyEnabled, (Keys)controller.Saved.HideRulesHotkey);
    }

    void ShowSettings()
    {
        trayMenu.Close();
        using var dialog = new Form { Text = L.T("设置"), Size = new(800, 750), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, Font = Font, Padding = new(24), BackColor = UiTheme.Canvas, ForeColor = UiTheme.Ink };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), Tag = "surface", ColumnCount = 2, RowCount = 11 };
        foreach (int height in new[] { 42, 42, 38, 38, 38, 46, 46, 46, 80, 44, 48 }) panel.RowStyles.Add(new(SizeType.Absolute, height));
        panel.ColumnStyles.Add(new(SizeType.Absolute, 300)); panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        var packs = L.Packs();
        if (packs.Count == 0) packs["zh-CN"] = new L.Pack { Name = "简体中文" };
        var languages = new ThemeComboBox { Name = "language", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        languages.Items.AddRange(packs.Select(x => new LanguageChoice(x.Key, x.Value.Name)).ToArray());
        languages.SelectedIndex = Math.Max(0, languages.Items.Cast<LanguageChoice>().ToList().FindIndex(x => x.Code == L.Current));
        var themes = new ThemeComboBox { Name = "theme", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        themes.Items.AddRange(new[] { new LanguageChoice("system", L.T("跟随系统")), new LanguageChoice("light", L.T("浅色")), new LanguageChoice("dark", L.T("深色")) });
        themes.SelectedIndex = Math.Max(0, themes.Items.Cast<LanguageChoice>().ToList().FindIndex(x => x.Code == controller.Saved.Theme));
        var closeToTray = new CheckBox { Text = L.T("关闭窗口后保留在托盘运行"), Checked = controller.Saved.CloseToTray, AutoSize = true };
        var showTrayIcon = new CheckBox { Text = L.T("显示本程序托盘图标"), Checked = controller.Saved.ShowTrayIcon, AutoSize = true };
        var autoStart = new CheckBox { Name = "startup", Text = L.T("开机启动"), AutoSize = true };
        string? startupError = null;
        try { autoStart.Checked = startup.Enabled; }
        catch (Exception ex) { autoStart.Enabled = false; startupError = ex.Message; }
        bool originalStartup = autoStart.Checked;
        var enabled = new[] { controller.Saved.MainHotkeyEnabled, controller.Saved.ShowAllHotkeyEnabled, controller.Saved.HideRulesHotkeyEnabled };
        var keys = new[] { (Keys)controller.Saved.MainHotkey, (Keys)controller.Saved.ShowAllHotkey, (Keys)controller.Saved.HideRulesHotkey };
        var names = new[] { "mainHotkey", "showAllHotkey", "hideRulesHotkey" };
        var labels = new[] { "打开主界面快捷键", "显示所有快捷键", "隐藏规则命中快捷键" };
        panel.Controls.Add(new Label { Text = L.T("语言"), AutoSize = true }, 0, 0); panel.Controls.Add(languages, 1, 0);
        panel.Controls.Add(new Label { Text = L.T("外观"), AutoSize = true }, 0, 1); panel.Controls.Add(themes, 1, 1);
        panel.Controls.Add(closeToTray, 0, 2); panel.SetColumnSpan(closeToTray, 2);
        panel.Controls.Add(showTrayIcon, 0, 3); panel.SetColumnSpan(showTrayIcon, 2);
        panel.Controls.Add(autoStart, 0, 4); panel.SetColumnSpan(autoStart, 2);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var toggle = new CheckBox { Name = names[i] + "Enabled", Text = L.T(labels[i]), AutoSize = true, Checked = enabled[i] };
            toggle.CheckedChanged += (_, _) => enabled[index] = toggle.Checked;
            var input = new TextBox { Name = names[i], ReadOnly = true, Dock = DockStyle.Fill, Text = new KeysConverter().ConvertToString(keys[i]) };
            input.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true; e.Handled = true;
                if (GlobalHotkey.Valid(e.KeyData)) { keys[index] = e.KeyData; input.Text = new KeysConverter().ConvertToString(e.KeyData); }
            };
            panel.Controls.Add(toggle, 0, 5 + i); panel.Controls.Add(input, 1, 5 + i);
        }
        var help = new Label { Text = L.T("显示所有会保留规则并暂停自动隐藏；隐藏规则命中会恢复规则。按 Ctrl 或 Alt 加其他键设置快捷键。"), AutoSize = true, MaximumSize = new(660, 0), ForeColor = UiTheme.Muted };
        var error = new Label { Text = startupError ?? "", AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new(660, 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = UiTheme.Button(L.T("保存")); var cancel = UiTheme.Button(L.T("取消")); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        panel.Controls.Add(help, 0, 8); panel.SetColumnSpan(help, 2);
        panel.Controls.Add(error, 0, 9); panel.SetColumnSpan(error, 2);
        panel.Controls.Add(buttons, 0, 10); panel.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(panel); dialog.Controls.Add(UiTheme.Heading(L.T("设置"), L.T("语言、托盘行为与键盘快捷键"), Font));
        dialog.CancelButton = cancel;
        save.Click += (_, _) =>
        {
            var active = keys.Where((_, index) => enabled[index]).ToList();
            if (active.Any(x => !GlobalHotkey.Valid(x)) || active.Distinct().Count() != active.Count)
            { error.Text = L.T("快捷键无效或已被占用，请更换组合。"); return; }
            var old = new { controller.Saved.MainHotkeyEnabled, controller.Saved.MainHotkey, controller.Saved.ShowAllHotkeyEnabled, controller.Saved.ShowAllHotkey,
                controller.Saved.HideRulesHotkeyEnabled, controller.Saved.HideRulesHotkey, controller.Saved.Theme, controller.Saved.Language, controller.Saved.CloseToTray, controller.Saved.ShowTrayIcon };
            void Restore()
            {
                controller.Saved.MainHotkeyEnabled = old.MainHotkeyEnabled; controller.Saved.MainHotkey = old.MainHotkey;
                controller.Saved.ShowAllHotkeyEnabled = old.ShowAllHotkeyEnabled; controller.Saved.ShowAllHotkey = old.ShowAllHotkey;
                controller.Saved.HideRulesHotkeyEnabled = old.HideRulesHotkeyEnabled; controller.Saved.HideRulesHotkey = old.HideRulesHotkey;
                controller.Saved.Theme = old.Theme; controller.Saved.Language = old.Language;
                controller.Saved.CloseToTray = old.CloseToTray; controller.Saved.ShowTrayIcon = old.ShowTrayIcon;
                RegisterShortcuts(); UpdateStatus();
            }
            controller.Saved.MainHotkeyEnabled = enabled[0]; controller.Saved.MainHotkey = (int)keys[0];
            controller.Saved.ShowAllHotkeyEnabled = enabled[1]; controller.Saved.ShowAllHotkey = (int)keys[1];
            controller.Saved.HideRulesHotkeyEnabled = enabled[2]; controller.Saved.HideRulesHotkey = (int)keys[2];
            RegisterShortcuts();
            if (hotkeyWarning) { Restore(); error.Text = L.T("快捷键无效或已被占用，请更换组合。"); return; }
            StartupRegistration.Snapshot? previousStartup = null;
            try
            {
                if (autoStart.Enabled && autoStart.Checked != originalStartup)
                {
                    previousStartup = startup.Capture();
                    startup.SetEnabled(autoStart.Checked);
                }
                controller.Saved.Language = ((LanguageChoice)languages.SelectedItem!).Code;
                controller.Saved.Theme = ((LanguageChoice)themes.SelectedItem!).Code;
                controller.Saved.CloseToTray = closeToTray.Checked; controller.Saved.ShowTrayIcon = showTrayIcon.Checked;
                controller.Save();
            }
            catch (Exception ex)
            {
                Restore(); error.Text = ex.Message;
                if (previousStartup != null)
                    try { startup.Restore(previousStartup); }
                    catch (Exception rollback) { error.Text += "\n" + rollback.Message; }
                return;
            }
            if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon;
            L.Set(controller.Saved.Language); ApplyLanguage(); ApplyTheme(); dialog.DialogResult = DialogResult.OK;
        };
        UiTheme.Apply(dialog); dialog.Shown += (_, _) => UiTheme.Apply(dialog);
        timer.Stop();
        try { if (Visible) dialog.ShowDialog(this); else dialog.ShowDialog(); } finally { UpdateTimer(); }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && showAllHotkey?.Matches(message.WParam) == true) { _ = ApplyVisibilityPresetAsync(false); return; }
        if (message.Msg == 0x0312 && mainHotkey?.Matches(message.WParam) == true) { OpenMainWindow(); return; }
        if (message.Msg == 0x0312 && hideRulesHotkey?.Matches(message.WParam) == true) { _ = ApplyVisibilityPresetAsync(true); return; }
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        showAllHotkey?.Dispose(); showAllHotkey = null; hideRulesHotkey?.Dispose(); hideRulesHotkey = null; mainHotkey?.Dispose(); mainHotkey = null;
        base.OnHandleDestroyed(e);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (trayIcon != null && controller != null)
        {
            RegisterShortcuts();
        }
    }
}
