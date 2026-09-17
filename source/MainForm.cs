namespace TrayPilot;
internal sealed partial class MainForm : Form
{
    readonly Controller controller;
    readonly TextBox search = new() { PlaceholderText = "搜索软件名称、进程或路径", Width = 280 };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    readonly CheckBox autoRefresh = new() { Text = "自动刷新（每 2.5 秒）", Checked = true, AutoSize = true, Margin = new(20, 5, 0, 0) };
    readonly RadioButton layoutMode = new() { Text = "网格", Appearance = Appearance.Button, AutoSize = true };
    readonly ContextMenuStrip itemMenu = new();
    List<TrayEntry> entries = new();
    string? renderedContent;
    bool busy, closing;
    internal MainForm(Controller controller, bool initialize = true)
    {
        this.controller = controller;
        L.Set(controller.Saved.Language);
        Text = "TrayPilot · 托盘图标管理"; Width = 1180; Height = 700; MinimumSize = new(1020, 520);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(247, 249, 252);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new(SizeType.Absolute, 45)); layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 46)); layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = "让托盘只留下需要的图标", Font = new(Font.FontFamily, 19, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new Label { Text = "双击切换显示 / 隐藏；右键查看属性或结束任务。淡色表示隐藏，悬停查看详情。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(80, 90, 105) }, 0, 1);
        actions.Controls.Add(search);
        AddButton("隐藏选中", () => ChangeSelected(true)); AddButton("恢复选中", () => ChangeSelected(false));
        AddButton("刷新", async () => await RefreshAsync()); AddButton("隐藏规则", EditRules);
        AddButton("全部恢复", () => RunAction(() => { controller.Saved.HiddenPaths.Clear(); controller.Save(); controller.RestoreManaged(); }));
        layout.Controls.Add(actions, 0, 2);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(new Label { Text = "布局", AutoSize = true, Margin = new(0, 5, 8, 0) });
        options.Controls.Add(new RadioButton { Text = "列表", Appearance = Appearance.Button, AutoSize = true, Checked = true });
        options.Controls.Add(layoutMode); options.Controls.Add(autoRefresh);
        layout.Controls.Add(options, 0, 3);
        layoutMode.CheckedChanged += (_, _) => ChangeLayout();
        autoRefresh.CheckedChanged += (_, _) => { UpdateTimer(); UpdateStatus(); };
        list.Columns.Add("软件名称", 215); list.Columns.Add("图标状态", 125); list.Columns.Add("自动隐藏", 80);
        list.Columns.Add("进程", 155); list.Columns.Add("路径", 430);
        list.ShowItemToolTips = true;
        list.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || closing) return;
            if (list.HitTest(e.Location).Item?.Tag is TrayEntry entry) ShowItemMenu(entry, e.Location);
        };
        list.MouseDoubleClick += (_, e) =>
        {
            if (busy || closing || list.HitTest(e.Location).Item?.Tag is not TrayEntry entry) return;
            if (e.Button == MouseButtons.Left && ModifierKeys == Keys.None) ToggleEntry(entry);
        };
        layout.Controls.Add(list, 0, 4); layout.Controls.Add(status, 0, 5);
        layout.Controls.Add(new Label { Text = "关闭窗口可保留在托盘运行；选择“退出”恢复隐藏图标并结束程序。", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 6);
        Controls.Add(layout);
        SetupShell(initialize);
        search.TextChanged += (_, _) => RenderList();
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { if (initialize) { await RefreshAsync(); UpdateTimer(); } };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon?.Visible == true)
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
        + (hotkeyWarning ? " · " + L.T("快捷键注册失败，请在设置中更换组合。") : "");
    void ChangeLayout()
    {
        list.View = layoutMode.Checked ? View.LargeIcon : View.Details;
        if (list.View == View.LargeIcon)
        {
            int width = 120 * DeviceDpi / 96, height = 90 * DeviceDpi / 96;
            Native.SendMessageW(list.Handle, 0x1035, 0, (nint)((height << 16) | width)); // LVM_SETICONSPACING
            list.ArrangeIcons(ListViewAlignment.Top);
        }
    }
    void AddButton(string text, Action handler)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.System, Margin = new(8, 0, 0, 0) };
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
            controller.Apply(entries);
            entries = entries.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
            RenderList();
            UpdateStatus();
        }
        catch (Exception ex) { status.Text = L.T("检查失败：") + ex.Message; }
        finally { busy = false; }
    }
    void RenderList()
    {
        // Leave unchanged rows intact so periodic scanning does not dismiss tooltips or reset scrolling.
        var content = System.Text.Json.JsonSerializer.Serialize(new { Language = L.Current, Search = search.Text, Rows = entries.Select(x => new
        {
            x.Key, x.Name, x.Path, x.Tooltip, x.State, Rule = controller.HasRule(x.Path),
            Icon = x.IconSnapshot == null ? "" : Convert.ToBase64String(x.IconSnapshot)
        }) });
        if (content == renderedContent) return;
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(x => ((TrayEntry)x.Tag!).Key).ToHashSet();
        var topKey = list.View == View.Details ? (list.TopItem?.Tag as TrayEntry)?.Key : null;
        var images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(24, 24) };
        var largeImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(40, 40) };
        var oldImages = list.SmallImageList;
        var oldLargeImages = list.LargeImageList;
        list.BeginUpdate(); list.Items.Clear();
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
            var item = new ListViewItem(entry.Name) { Tag = entry, ImageIndex = images.Images.Count - 1,
                ToolTipText = Details(entry), Selected = selected.Contains(entry.Key) };
            item.SubItems.Add(L.T(entry.State == 1 ? "完全隐藏" : "正常"));
            item.SubItems.Add(L.T(controller.HasRule(entry.Path) ? "是" : "否"));
            item.SubItems.Add(System.IO.Path.GetFileName(entry.Path)); item.SubItems.Add(entry.Path);
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
        entry.Path, entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString());

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
        var end = itemMenu.Items.Add(L.T("结束任务"), null, async (_, _) => { itemMenu.Close(); await EndTaskAsync(entry); });
        end.Enabled = !busy && state is 0 or 1 && entry.Pid != Environment.ProcessId;
        itemMenu.Show(list, location);
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
        finally { busy = false; }
    }

    void ChangePaths(IEnumerable<string> paths, bool hide)
    {
        foreach (var path in paths)
        {
            if (hide) controller.AddRule(path); else controller.RemoveRule(path);
            foreach (var entry in entries.Where(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
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
        using var dialog = new Form { Text = L.T("隐藏规则（包含当前未运行的软件）"), Width = 770, Height = 390, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var rules = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };
        rules.Items.AddRange(controller.Saved.HiddenPaths.Cast<object>().ToArray());
        var remove = new Button { Text = L.T("删除选中规则并恢复图标"), Dock = DockStyle.Bottom, Height = 44 };
        remove.Click += (_, _) =>
        {
            try
            {
                foreach (var path in rules.SelectedItems.Cast<string>().ToList())
                {
                    controller.RemoveRule(path);
                    foreach (var entry in controller.Saved.Recovery.Where(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)).ToList()) controller.Show(entry);
                    rules.Items.Remove(path);
                }
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message); }
        };
        dialog.Controls.Add(rules); dialog.Controls.Add(remove);
        timer.Stop(); dialog.ShowDialog(this); UpdateTimer(); _ = RefreshAsync();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            hotkey?.Dispose();
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
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
