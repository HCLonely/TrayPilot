namespace TrayPilot;

internal sealed partial class MainForm
{
    readonly PagedTrayMenu trayMenu = new() { ShowCheckMargin = true, ShowImageMargin = true };
    readonly MenuStrip menuBar = new();
    readonly Dictionary<Control, string> captions = new();
    readonly List<string> columnCaptions = new();
    NotifyIcon? trayIcon;
    GlobalHotkey? hotkey, mainHotkey;
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
        trayMenu.Opening += (_, _) => BuildTrayMenu();
        trayMenu.PageRequested += MoveTrayPage;
        if (initialize) InitializeTray();
    }

    void InitializeTray()
    {
        if (trayIcon != null) return;
        _ = Handle;
        trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "TrayPilot", ContextMenuStrip = trayMenu, Visible = controller.Saved.ShowTrayIcon };
        trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { trayMenu.Close(); OpenMainWindow(); } };
        trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { trayMenu.Close(); OpenMainWindow(); } };
        hotkey = new GlobalHotkey(Handle);
        hotkeyWarning = !hotkey.TrySet(controller.Saved.HotkeyEnabled, (Keys)controller.Saved.Hotkey);
        mainHotkey = new GlobalHotkey(Handle, 0x4A11);
        hotkeyWarning |= !mainHotkey.TrySet(controller.Saved.MainHotkeyEnabled, (Keys)controller.Saved.MainHotkey);
        UpdateStatus();
    }

    void ApplyLanguage()
    {
        foreach (var caption in captions) caption.Key.Text = L.T(caption.Value);
        search.PlaceholderText = L.T("搜索软件名称、进程或路径");
        for (int i = 0; i < columnCaptions.Count; i++) list.Columns[i].Text = L.T(columnCaptions[i]);
        while (menuBar.Items.Count > 0) { var item = menuBar.Items[0]; menuBar.Items.RemoveAt(0); item.Dispose(); }
        menuBar.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
        menuBar.Items.Add(L.T("关于"), null, (_, _) => ShowAbout());
        menuBar.Items.Add(L.T("退出"), null, (_, _) => RequestExit());
        RenderList(); UpdateStatus();
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
        var self = new ToolStripMenuItem(L.T("TrayPilot（本程序）")) { Checked = controller.Saved.ShowTrayIcon, Enabled = !busy };
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
        trayMenu.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
        trayMenu.Items.Add(L.T("关于"), null, (_, _) => ShowAbout());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("退出"), null, (_, _) => RequestExit());
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
        }, Font);
        timer.Stop();
        try { if (Visible) dialog.ShowDialog(this); else dialog.ShowDialog(); } finally { UpdateTimer(); }
    }

    void ShowSettings()
    {
        trayMenu.Close();
        using var dialog = new Form { Text = L.T("设置"), Size = new(740, 470), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, Font = Font };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 2, RowCount = 8 };
        panel.ColumnStyles.Add(new(SizeType.Absolute, 240)); panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        var packs = L.Packs();
        if (packs.Count == 0) packs["zh-CN"] = new L.Pack { Name = "简体中文" };
        var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        languages.Items.AddRange(packs.Select(x => new LanguageChoice(x.Key, x.Value.Name)).ToArray());
        languages.SelectedIndex = Math.Max(0, languages.Items.Cast<LanguageChoice>().ToList().FindIndex(x => x.Code == L.Current));
        var closeToTray = new CheckBox { Text = L.T("关闭窗口后保留在托盘运行"), Checked = controller.Saved.CloseToTray, AutoSize = true };
        var enableHotkey = new CheckBox { Text = L.T("快捷管理菜单快捷键"), Name = "menuHotkeyEnabled", Checked = controller.Saved.HotkeyEnabled, AutoSize = true };
        Keys selectedKeys = (Keys)controller.Saved.Hotkey;
        var shortcut = new TextBox { Name = "menuHotkey", ReadOnly = true, Dock = DockStyle.Fill, Text = new KeysConverter().ConvertToString(selectedKeys) };
        shortcut.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true; e.Handled = true;
            if (GlobalHotkey.Valid(e.KeyData)) { selectedKeys = e.KeyData; shortcut.Text = new KeysConverter().ConvertToString(selectedKeys); }
        };
        var showTrayIcon = new CheckBox { Text = L.T("显示本程序托盘图标"), Checked = controller.Saved.ShowTrayIcon, AutoSize = true };
        var enableMainHotkey = new CheckBox { Text = L.T("打开主界面快捷键"), Checked = controller.Saved.MainHotkeyEnabled, AutoSize = true };
        Keys mainKeys = (Keys)controller.Saved.MainHotkey;
        var mainShortcut = new TextBox { Name = "mainHotkey", ReadOnly = true, Dock = DockStyle.Fill, Text = new KeysConverter().ConvertToString(mainKeys) };
        mainShortcut.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true; e.Handled = true;
            if (GlobalHotkey.Valid(e.KeyData)) { mainKeys = e.KeyData; mainShortcut.Text = new KeysConverter().ConvertToString(mainKeys); }
        };
        var help = new Label { Text = L.T("按 Ctrl 或 Alt 加其他键设置快捷键。隐藏本程序图标后，可再次运行程序打开主界面。"), AutoSize = true, MaximumSize = new(440, 0) };
        var error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new(560, 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = L.T("保存"), AutoSize = true };
        var cancel = new Button { Text = L.T("取消"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        panel.Controls.Add(new Label { Text = L.T("语言"), AutoSize = true }, 0, 0); panel.Controls.Add(languages, 1, 0);
        panel.Controls.Add(closeToTray, 0, 1); panel.SetColumnSpan(closeToTray, 2);
        panel.Controls.Add(enableHotkey, 0, 2); panel.Controls.Add(shortcut, 1, 2);
        panel.Controls.Add(showTrayIcon, 0, 3); panel.SetColumnSpan(showTrayIcon, 2);
        panel.Controls.Add(enableMainHotkey, 0, 4); panel.Controls.Add(mainShortcut, 1, 4);
        panel.Controls.Add(help, 1, 5); panel.Controls.Add(error, 0, 6); panel.SetColumnSpan(error, 2);
        panel.Controls.Add(buttons, 0, 7); panel.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(panel); dialog.CancelButton = cancel;
        save.Click += (_, _) =>
        {
            hotkey ??= new GlobalHotkey(Handle);
            mainHotkey ??= new GlobalHotkey(Handle, 0x4A11);
            bool previousMainEnabled = controller.Saved.MainHotkeyEnabled, previousTray = controller.Saved.ShowTrayIcon;
            int previousMainKeys = controller.Saved.MainHotkey;
            bool previousEnabled = controller.Saved.HotkeyEnabled;
            int previousKeys = controller.Saved.Hotkey;
            bool previousClose = controller.Saved.CloseToTray;
            string previousLanguage = controller.Saved.Language;
            if ((enableHotkey.Checked && !GlobalHotkey.Valid(selectedKeys)) || (enableMainHotkey.Checked && !GlobalHotkey.Valid(mainKeys))
                || (enableHotkey.Checked && enableMainHotkey.Checked && selectedKeys == mainKeys))
            { error.Text = L.T("快捷键无效或已被占用，请更换组合。"); return; }
            hotkey.Dispose(); mainHotkey.Dispose();
            if (!hotkey.TrySet(enableHotkey.Checked, selectedKeys) || !mainHotkey.TrySet(enableMainHotkey.Checked, mainKeys))
            {
                hotkey.Dispose(); mainHotkey.Dispose();
                hotkeyWarning = !hotkey.TrySet(previousEnabled, (Keys)previousKeys);
                hotkeyWarning |= !mainHotkey.TrySet(previousMainEnabled, (Keys)previousMainKeys);
                UpdateStatus(); error.Text = L.T("快捷键无效或已被占用，请更换组合。"); return;
            }
            try
            {
                controller.Saved.HotkeyEnabled = enableHotkey.Checked; controller.Saved.Hotkey = (int)selectedKeys;
                controller.Saved.CloseToTray = closeToTray.Checked; controller.Saved.Language = ((LanguageChoice)languages.SelectedItem!).Code;
                controller.Saved.MainHotkeyEnabled = enableMainHotkey.Checked; controller.Saved.MainHotkey = (int)mainKeys;
                controller.Saved.ShowTrayIcon = showTrayIcon.Checked;
                controller.Save(); if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon; hotkeyWarning = false; L.Set(controller.Saved.Language); ApplyLanguage(); dialog.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                controller.Saved.HotkeyEnabled = previousEnabled; controller.Saved.Hotkey = previousKeys;
                controller.Saved.CloseToTray = previousClose; controller.Saved.Language = previousLanguage;
                controller.Saved.MainHotkeyEnabled = previousMainEnabled; controller.Saved.MainHotkey = previousMainKeys;
                controller.Saved.ShowTrayIcon = previousTray;
                if (trayIcon != null) trayIcon.Visible = previousTray;
                hotkey.Dispose(); mainHotkey.Dispose();
                hotkeyWarning = !hotkey.TrySet(previousEnabled, (Keys)previousKeys);
                hotkeyWarning |= !mainHotkey.TrySet(previousMainEnabled, (Keys)previousMainKeys); L.Set(previousLanguage); ApplyLanguage(); error.Text = ex.Message;
            }
        };
        timer.Stop();
        try { if (Visible) dialog.ShowDialog(this); else dialog.ShowDialog(); } finally { UpdateTimer(); }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && hotkey?.Matches(message.WParam) == true) { ShowMainMenu(); return; }
        if (message.Msg == 0x0312 && mainHotkey?.Matches(message.WParam) == true) { OpenMainWindow(); return; }
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        hotkey?.Dispose(); hotkey = null; mainHotkey?.Dispose(); mainHotkey = null;
        base.OnHandleDestroyed(e);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (trayIcon != null && controller != null)
        {
            hotkey = new GlobalHotkey(Handle);
            hotkeyWarning = !hotkey.TrySet(controller.Saved.HotkeyEnabled, (Keys)controller.Saved.Hotkey);
            mainHotkey = new GlobalHotkey(Handle, 0x4A11);
            hotkeyWarning |= !mainHotkey.TrySet(controller.Saved.MainHotkeyEnabled, (Keys)controller.Saved.MainHotkey);
        }
    }
}
