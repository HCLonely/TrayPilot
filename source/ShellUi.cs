namespace TrayPilot;

internal sealed partial class MainForm
{
    readonly ContextMenuStrip trayMenu = new() { ShowCheckMargin = true, ShowImageMargin = true };
    readonly MenuStrip menuBar = new();
    readonly Dictionary<Control, string> captions = new();
    readonly List<string> columnCaptions = new();
    NotifyIcon? trayIcon;
    GlobalHotkey? hotkey;
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
        Controls.Add(menuBar); MainMenuStrip = menuBar;
        ApplyLanguage();
        trayMenu.Opening += (_, _) => BuildTrayMenu();
        if (initialize) InitializeTray();
    }

    void InitializeTray()
    {
        if (trayIcon != null) return;
        _ = Handle;
        trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "TrayPilot", ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMainMenu(); };
        trayIcon.DoubleClick += (_, _) => { trayMenu.Close(); OpenMainWindow(); };
        hotkey = new GlobalHotkey(Handle);
        hotkeyWarning = !hotkey.TrySet(controller.Saved.HotkeyEnabled, (Keys)controller.Saved.Hotkey);
        UpdateStatus();
    }

    void ApplyLanguage()
    {
        foreach (var caption in captions) caption.Key.Text = L.T(caption.Value);
        search.PlaceholderText = L.T("搜索软件名称、进程或路径");
        for (int i = 0; i < columnCaptions.Count; i++) list.Columns[i].Text = L.T(columnCaptions[i]);
        while (menuBar.Items.Count > 0) { var item = menuBar.Items[0]; menuBar.Items.RemoveAt(0); item.Dispose(); }
        menuBar.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
        menuBar.Items.Add(L.T("快捷管理"), null, (_, _) => ShowMainMenu());
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
        trayMenu.Items.Add(L.T("关于"), null, (_, _) => ShowAbout());
        trayMenu.Items.Add(L.T("退出"), null, (_, _) => RequestExit());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem(L.T("托盘图标（√ 表示显示，单击切换）")) { Enabled = false });
        var groups = entries.Where(x => x.Pid != Environment.ProcessId && Scanner.SameOwner(x))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var group in groups)
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
        if (trayMenu.Items.Count == 5) trayMenu.Items.Add(new ToolStripMenuItem(L.T("暂无可管理的图标")) { Enabled = false });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(L.T("刷新"), null, async (_, _) => { trayMenu.Close(); await RefreshAsync(); });
        trayMenu.Items.Add(L.T("设置"), null, (_, _) => ShowSettings());
    }

    void ShowMainMenu()
    {
        if (closing || IsDisposed) return;
        itemMenu.Close();
        // ContextMenuStrip is its own top-level popup, so the main window can stay hidden.
        trayMenu.Show(Cursor.Position);
        Native.SetForegroundWindow(trayMenu.Handle);
    }

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
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
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
        using var dialog = new Form { Text = L.T("设置"), Size = new(650, 365), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, Font = Font };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 2, RowCount = 6 };
        panel.ColumnStyles.Add(new(SizeType.Absolute, 175)); panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        var packs = L.Packs();
        if (packs.Count == 0) packs["zh-CN"] = new L.Pack { Name = "简体中文" };
        var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DisplayMember = "Value", ValueMember = "Key" };
        languages.DataSource = packs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value.Name)).ToList();
        languages.SelectedValue = L.Current;
        var closeToTray = new CheckBox { Text = L.T("关闭窗口后保留在托盘运行"), Checked = controller.Saved.CloseToTray, AutoSize = true };
        var enableHotkey = new CheckBox { Text = L.T("启用全局快捷键"), Checked = controller.Saved.HotkeyEnabled, AutoSize = true };
        Keys selectedKeys = (Keys)controller.Saved.Hotkey;
        var shortcut = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Text = new KeysConverter().ConvertToString(selectedKeys) };
        shortcut.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true; e.Handled = true;
            if (GlobalHotkey.Valid(e.KeyData)) { selectedKeys = e.KeyData; shortcut.Text = new KeysConverter().ConvertToString(selectedKeys); }
        };
        var help = new Label { Text = L.T("在输入框中按 Ctrl 或 Alt 加其他键。快捷键弹出快捷管理菜单，关闭主窗口后仍有效。"), AutoSize = true, MaximumSize = new(420, 0) };
        var error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new(560, 0) };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = L.T("保存"), AutoSize = true };
        var cancel = new Button { Text = L.T("取消"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        panel.Controls.Add(new Label { Text = L.T("语言"), AutoSize = true }, 0, 0); panel.Controls.Add(languages, 1, 0);
        panel.Controls.Add(closeToTray, 0, 1); panel.SetColumnSpan(closeToTray, 2);
        panel.Controls.Add(enableHotkey, 0, 2); panel.Controls.Add(shortcut, 1, 2);
        panel.Controls.Add(help, 1, 3); panel.Controls.Add(error, 0, 4); panel.SetColumnSpan(error, 2);
        panel.Controls.Add(buttons, 0, 5); panel.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(panel); dialog.CancelButton = cancel;
        save.Click += (_, _) =>
        {
            hotkey ??= new GlobalHotkey(Handle);
            bool previousEnabled = controller.Saved.HotkeyEnabled;
            int previousKeys = controller.Saved.Hotkey;
            bool previousClose = controller.Saved.CloseToTray;
            string previousLanguage = controller.Saved.Language;
            if (!hotkey.TrySet(enableHotkey.Checked, selectedKeys)) { error.Text = L.T("快捷键无效或已被占用，请更换组合。"); return; }
            try
            {
                controller.Saved.HotkeyEnabled = enableHotkey.Checked; controller.Saved.Hotkey = (int)selectedKeys;
                controller.Saved.CloseToTray = closeToTray.Checked; controller.Saved.Language = languages.SelectedValue as string ?? "zh-CN";
                controller.Save(); hotkeyWarning = false; L.Set(controller.Saved.Language); ApplyLanguage(); dialog.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                controller.Saved.HotkeyEnabled = previousEnabled; controller.Saved.Hotkey = previousKeys;
                controller.Saved.CloseToTray = previousClose; controller.Saved.Language = previousLanguage;
                hotkeyWarning = !hotkey.TrySet(previousEnabled, (Keys)previousKeys); L.Set(previousLanguage); ApplyLanguage(); error.Text = ex.Message;
            }
        };
        timer.Stop();
        try { if (Visible) dialog.ShowDialog(this); else dialog.ShowDialog(); } finally { UpdateTimer(); }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && hotkey?.Matches(message.WParam) == true) { ShowMainMenu(); return; }
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        hotkey?.Dispose(); hotkey = null;
        base.OnHandleDestroyed(e);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (trayIcon != null && controller != null)
        {
            hotkey = new GlobalHotkey(Handle);
            hotkeyWarning = !hotkey.TrySet(controller.Saved.HotkeyEnabled, (Keys)controller.Saved.Hotkey);
        }
    }
}
