namespace TrayPilot;
internal sealed partial class MainForm : Form
{
    readonly Controller controller;
    readonly Func<List<TrayEntry>> visibilityScanner;
    readonly TextBox search = new() { PlaceholderText = "搜索软件名称、进程或路径", Width = 280 };
    readonly ListView list = new TrayListView() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    readonly CheckBox autoRefresh = new() { Text = "自动刷新（每 2.5 秒）", Checked = true, AutoSize = true, Margin = new(20, 5, 0, 0) };
    readonly RadioButton layoutMode = new() { Text = "网格", Appearance = Appearance.Button, AutoSize = true };
    readonly ContextMenuStrip itemMenu = new();
    List<TrayEntry> entries = new();
    string? renderedContent;
    bool busy, closing;
    internal MainForm(Controller controller, bool initialize = true, Func<List<TrayEntry>>? visibilityScanner = null)
    {
        this.controller = controller;
        this.visibilityScanner = visibilityScanner ?? Scanner.Scan;
        L.Set(controller.Saved.Language); UiTheme.Set(controller.Saved.Theme);
        Text = "TrayPilot · 托盘图标管理"; Width = 1180; Height = 760; MinimumSize = new(1020, 620);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 9.5f);
        BackColor = UiTheme.Canvas; ForeColor = UiTheme.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 20, 28, 12), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 58)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Absolute, 26));
        var heading = new Label { Text = "让托盘只留下需要的图标", Font = new(Font.FontFamily, 22, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new Label { Text = "双击切换显示 / 隐藏；右键查看属性或结束任务。淡色表示隐藏，悬停查看详情。",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = Padding.Empty }, 0, 1);
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new(0, 4, 0, 10) };
        searchRow.RowStyles.Add(new(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var searchBox = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(14, 10, 14, 8), Margin = new(0, 0, 12, 0) };
        search.BorderStyle = BorderStyle.None; search.Dock = DockStyle.Fill; search.BackColor = UiTheme.Surface; search.ForeColor = UiTheme.Ink;
        searchBox.Controls.Add(search);
        searchBox.Controls.Add(new Label { Text = "搜索", Dock = DockStyle.Left, Width = 62, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface });
        searchRow.Controls.Add(searchBox, 0, 0);
        var refresh = UiTheme.Button("刷新"); refresh.Click += async (_, _) => { if (!busy && !closing) await RefreshAsync(); };
        searchRow.Controls.Add(refresh, 1, 0);
        autoRefresh.Margin = new(18, 10, 0, 0); autoRefresh.ForeColor = UiTheme.Muted;
        searchRow.Controls.Add(autoRefresh, 2, 0); layout.Controls.Add(searchRow, 0, 2);
        AddButton("隐藏选中", () => ChangeSelected(true)); AddButton("恢复选中", () => ChangeSelected(false));
        var hideRules = UiTheme.Button("隐藏规则命中"); hideRules.Click += async (_, _) => await ApplyVisibilityPresetAsync(true); actions.Controls.Add(hideRules);
        AddButton("全部恢复", () => RunAction(() => { controller.RestoreManaged(); controller.ClearRules(); }));
        var commandRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        commandRow.ColumnStyles.Add(new(SizeType.Percent, 100)); commandRow.ColumnStyles.Add(new(SizeType.AutoSize));
        actions.Margin = Padding.Empty; commandRow.Controls.Add(actions, 0, 0);
        var options = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        options.Controls.Add(new Label { Text = "布局", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new(0, 10, 10, 0) });
        var listMode = new RadioButton { Text = "列表", Appearance = Appearance.Button, AutoSize = true, Checked = true };
        UiTheme.Toggle(listMode); UiTheme.Toggle(layoutMode);
        options.Controls.Add(listMode); options.Controls.Add(layoutMode);
        commandRow.Controls.Add(options, 1, 0); layout.Controls.Add(commandRow, 0, 3);
        layoutMode.CheckedChanged += (_, _) => ChangeLayout();
        autoRefresh.CheckedChanged += (_, _) => { UpdateTimer(); UpdateStatus(); };
        list.BorderStyle = BorderStyle.None; list.BackColor = UiTheme.Surface; list.ForeColor = UiTheme.Ink;
        list.Columns.Add("软件名称", 215); list.Columns.Add("图标状态", 125); list.Columns.Add("命中规则", 140);
        list.Columns.Add("进程", 155); list.Columns.Add("路径", 430);
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
        layout.Controls.Add(new Label { Text = "关闭窗口可保留在托盘运行；选择“退出”恢复隐藏图标并结束程序。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new(Font.FontFamily, 8.5f), Margin = Padding.Empty }, 0, 6);
        Controls.Add(layout);
        SetupShell(initialize); ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        search.TextChanged += (_, _) => RenderList();
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { if (initialize) { await RefreshAsync(); UpdateTimer(); } };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon != null)
            { e.Cancel = true; Hide(); return; }
            timer.Stop();
            try { controller.RestoreManaged(); closing = true; }
            catch (Exception ex) { e.Cancel = true; exitRequested = false; OpenMainWindow(); MessageBox.Show(this, ex.Message + "\n" + L.T("恢复记录已保留，请稍后重试关闭。"), L.T("恢复未完成")); UpdateTimer(); }
        };
        FormClosed += (_, _) => timer.Stop();
    }
    void UpdateTimer() => timer.Enabled = autoRefresh.Checked && !closing && !IsDisposed;
    void UpdateStatus() => status.Text = L.F("检测到 {0} 个图标 · {1} 条隐藏规则 · {2}", entries.Count, controller.Saved.HiddenPaths.Count,
        L.T(autoRefresh.Checked ? "每 2.5 秒自动刷新" : "自动刷新已关闭 · 可点击刷新手动检查"))
        + (controller.Saved.RulesPaused ? " · " + L.T("自动隐藏已暂停") : "")
        + (hotkeyWarning ? " · " + L.T("快捷键注册失败，请在设置中更换组合。") : "");
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
        catch (Exception ex) { status.Text = L.T("检查失败：") + ex.Message; }
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
            x.Key, x.Name, x.Path, x.Tooltip, x.State, Rule = controller.HasRule(x.Path), Manual = controller.IsTemporarilyShown(x.Path),
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
            var item = new TrayListItem(entry.Name, controller.HasRule(entry.Path)) { Tag = entry, ImageIndex = images.Images.Count - 1,
                ToolTipText = Details(entry), Selected = selected.Contains(entry.Key) };
            item.SubItems.Add(L.T(entry.State == 1 ? "完全隐藏" : "正常"));
            item.SubItems.Add(controller.HasRule(entry.Path) ? L.T("✓ 命中") : "—");
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
    string Details(TrayEntry entry) => L.F("{0}\n状态：{1}\n自动隐藏：{2}\n托盘提示（Windows 缓存）：{3}\n进程：{4} · PID {5}\n路径：{6}\n图标标识：{7}\n左键双击切换显示 / 隐藏；同一软件的所有托盘图标一起切换。",
        entry.Name, L.T(entry.State == 1 ? "已隐藏" : "显示"), L.T(controller.HasRule(entry.Path) ? "是" : "否"),
        string.IsNullOrWhiteSpace(entry.Tooltip) ? L.T("无") : entry.Tooltip, System.IO.Path.GetFileName(entry.Path), entry.Pid,
        entry.Path, entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString())
        + (controller.IsTemporarilyShown(entry.Path) ? "\n" + L.T("暂时显示，执行“隐藏规则命中”后重新隐藏。") : "");

    void ToggleEntry(TrayEntry entry)
    {
        RunAction(() =>
        {
            var state = Scanner.SameOwner(entry) ? Native.State(entry) : -1;
            if (state is not (0 or 1)) throw new IOException(L.T("此图标已失效，请刷新后重试。"));
            ChangePaths(new[] { entry.Path }, state == 0);
        });
    }

    void ShowItemMenu(TrayEntry entry, Point location)
    {
        itemMenu.Close();
        while (itemMenu.Items.Count > 0) { var old = itemMenu.Items[0]; itemMenu.Items.RemoveAt(0); old.Dispose(); }
        int state = Scanner.SameOwner(entry) ? Native.State(entry) : -1;
        itemMenu.Items.Add(L.T("属性"), null, (_, _) => { itemMenu.Close(); ShowProperties(entry); });
        var toggle = itemMenu.Items.Add(L.T(state == 1 ? "显示图标" : "隐藏图标"), null, (_, _) => { itemMenu.Close(); ToggleEntry(entry); });
        toggle.Enabled = !busy && state is 0 or 1;
        var rule = itemMenu.Items.Add(L.T(controller.HasRule(entry.Path) ? "已在命中规则" : "添加到命中规则"), null, (_, _) =>
        {
            itemMenu.Close(); RunAction(() => controller.AddRule(entry.Path));
        });
        rule.Enabled = !busy && !controller.HasRule(entry.Path) && state is 0 or 1;
        var end = itemMenu.Items.Add(L.T("结束任务"), null, async (_, _) => { itemMenu.Close(); await EndTaskAsync(entry); });
        end.Enabled = !busy && state is 0 or 1 && entry.Pid != Environment.ProcessId && !Scanner.IsShellEntry(entry);
        UiTheme.Apply(itemMenu); itemMenu.Show(list, location);
    }

    void ShowProperties(TrayEntry entry)
    {
        using var dialog = InfoDialog.Create(entry.Name + " · " + L.T("属性"), ProgramActions.Properties(entry, controller.HasRule(entry.Path)), Font);
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
            status.Text = L.F("已结束 {0}（PID {1}）。", entry.Name, entry.Pid);
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = L.T("结束任务失败：") + ex.Message; }
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
        var paths = list.SelectedItems.Cast<ListViewItem>().Select(x => ((TrayEntry)x.Tag!).Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) { status.Text = L.T("请先选择一个或多个软件（支持 Ctrl / Shift 多选）。"); return; }
        RunAction(() => ChangePaths(paths, hide));
    }
    async void RunAction(Action action)
    {
        if (busy || closing) return;
        try { action(); await RefreshAsync(); }
        catch (Exception ex) { status.Text = ex.Message; OpenMainWindow(); MessageBox.Show(this, ex.Message, L.T("操作未完成"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
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
        var dialog = new Form { Text = L.T("隐藏规则"), Size = new(880, 540), MinimumSize = new(700, 440),
            StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = UiTheme.Canvas, ForeColor = UiTheme.Ink, Padding = new(24) };
        var rules = new SmoothListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
            HideSelection = false, ShowItemToolTips = true };
        UiTheme.StyleList(rules);
        rules.Columns.Add(L.T("软件名称"), 240); rules.Columns.Add(L.T("路径"), 550);
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
            var row = new ListViewItem(entry.Name) { Tag = path, ImageIndex = images.Images.Count - 1, ToolTipText = path,
                BackColor = rules.Items.Count % 2 == 0 ? UiTheme.Surface : UiTheme.Stripe };
            row.SubItems.Add(path); rules.Items.Add(row);
        }
        rules.Resize += (_, _) => rules.Columns[1].Width = Math.Max(320, rules.ClientSize.Width - rules.Columns[0].Width - 24);
        var empty = new Label { Text = L.T("暂无隐藏规则"), Dock = DockStyle.Bottom, Height = 40, ForeColor = UiTheme.Muted, Visible = rules.Items.Count == 0 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new(0, 16, 0, 0), FlowDirection = FlowDirection.RightToLeft };
        var close = UiTheme.Button(L.T("关闭")); close.DialogResult = DialogResult.Cancel;
        var remove = UiTheme.Button(L.T("删除选中规则并恢复图标")); remove.Enabled = false;
        rules.SelectedIndexChanged += (_, _) => remove.Enabled = rules.SelectedItems.Count > 0;
        remove.Click += (_, _) =>
        {
            try
            {
                foreach (var row in rules.SelectedItems.Cast<ListViewItem>().ToList())
                {
                    var path = (string)row.Tag!;
                    // Keep the rule available for retry if restoring an icon fails.
                    foreach (var entry in controller.Saved.Recovery.Where(x => Controller.SamePath(x.Path, path)).ToList()) controller.Show(entry);
                    controller.RemoveRule(path); rules.Items.Remove(row);
                }
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, L.T("操作未完成")); }
            finally { empty.Visible = rules.Items.Count == 0; remove.Enabled = rules.SelectedItems.Count > 0; }
        };
        buttons.Controls.Add(close); buttons.Controls.Add(remove);
        dialog.Controls.Add(rules); dialog.Controls.Add(empty); dialog.Controls.Add(buttons);
        dialog.Controls.Add(UiTheme.Heading(L.T("隐藏规则"), L.T("隐藏规则（包含当前未运行的软件）"), Font));
        dialog.CancelButton = close;
        UiTheme.Apply(dialog); dialog.Shown += (_, _) => UiTheme.Apply(dialog);
        return dialog;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
