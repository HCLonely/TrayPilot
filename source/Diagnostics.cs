using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TrayPilot;
internal static class Diagnostics
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args[0] == "--scan" && args.Length == 2)
            { File.WriteAllText(args[1], JsonSerializer.Serialize(Scanner.Scan(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
            if (args[0] == "--test-host" && args.Length == 2) return Host(args[1]);
            if (args[0] == "--self-test" && args.Length == 2) return Test(args[1]);
            if (args.Length == 2 && args[0] is "--preview-settings-en" or "--preview-about-en" or "--preview-properties-en")
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "preview-state");
                var previewController = new Controller(folder); previewController.Saved.Language = "en-US";
                using var form = new MainForm(previewController, initialize: false);
                using var capture = new System.Windows.Forms.Timer { Interval = 500 };
                capture.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(x => x != form);
                    if (dialog == null) return;
                    capture.Stop(); using var image = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(args[1]); dialog.Close();
                };
                form.Shown += (_, _) =>
                {
                    capture.Start();
                    if (args[0] == "--preview-properties-en")
                        typeof(MainForm).GetMethod("ShowProperties", flags)!.Invoke(form, new object[] { Scanner.Scan().First() });
                    else typeof(MainForm).GetMethod(args[0] == "--preview-settings-en" ? "ShowSettings" : "ShowAbout", flags)!.Invoke(form, null);
                    form.RequestExit();
                };
                Application.Run(form); return 0;
            }
            if (args[0] is "--preview" or "--preview-grid" or "--preview-en" or "--preview-tray-en" or "--preview-small-en" && args.Length == 2)
            {
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "preview-state");
                var previewController = new Controller(folder);
                if (args[0].EndsWith("-en")) previewController.Saved.Language = "en-US";
                using var form = new MainForm(previewController);
                if (args[0] == "--preview-small-en") form.Size = form.MinimumSize;
                if (args[0] == "--preview-grid")
                    ((RadioButton)typeof(MainForm).GetField("layoutMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!).Checked = true;
                var timer = new System.Windows.Forms.Timer { Interval = 3000 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop(); Control target = form;
                    if (args[0] == "--preview-tray-en")
                    {
                        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        typeof(MainForm).GetMethod("ShowMainMenu", flags)!.Invoke(form, null);
                        target = (ContextMenuStrip)typeof(MainForm).GetField("trayMenu", flags)!.GetValue(form)!;
                    }
                    Application.DoEvents(); target.PerformLayout();
                    using var image = new Bitmap(target.Width, target.Height);
                    target.DrawToBitmap(image, new Rectangle(Point.Empty, target.Size)); image.Save(args[1]); form.RequestExit();
                };
                form.Shown += (_, _) => timer.Start(); Application.Run(form); timer.Dispose(); return 0;
            }
            return 2;
        }
        catch (Exception ex) { if (args.Length > 1) File.WriteAllText(args[1] + ".error.txt", ex.ToString()); return 1; }
    }
    static int Host(string folder)
    {
        Directory.CreateDirectory(folder);
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams { Caption = "TrayPilot integration-test owner", Parent = -3 });
        var icons = new List<Native.IconData>();
        var guid = new Guid("5D7C1256-6A13-4E5A-9496-2D615DF04B81");
        for (uint i = 0; i < 2; i++)
        {
            var data = new Native.IconData { Size = (uint)Marshal.SizeOf<Native.IconData>(), Window = window.Handle, Id = 9107 + i,
                Flags = 7u | (i == 1 ? 32u : 0u), Message = 0x8001, Icon = SystemIcons.Information.Handle,
                Guid = i == 1 ? guid : Guid.Empty, Tip = i == 1 ? "TrayPilot GUID test" : "TrayPilot UID test" };
            if (!Native.Shell_NotifyIconW(0, ref data)) throw new Exception("Test icon creation failed.");
            icons.Add(data);
            if (i == 1) { data.Version = 4; if (!Native.Shell_NotifyIconW(4, ref data)) throw new Exception("Cannot set version 4."); }
        }
        File.WriteAllText(Path.Combine(folder, "ready"), window.Handle.ToString());
        var until = DateTime.UtcNow.AddSeconds(300);
        try
        {
            while (!File.Exists(Path.Combine(folder, "stop")) && DateTime.UtcNow < until)
            {
                if (File.Exists(Path.Combine(folder, "update")))
                {
                    var data = icons[0]; data.Flags = 4; data.Tip = "TrayPilot updated tooltip"; Native.Shell_NotifyIconW(1, ref data);
                    File.Delete(Path.Combine(folder, "update")); File.WriteAllText(Path.Combine(folder, "updated"), "ok");
                }
                Application.DoEvents(); Thread.Sleep(30);
            }
        }
        finally { foreach (var data in icons) { var d = data; Native.Shell_NotifyIconW(2, ref d); } window.DestroyHandle(); }
        return 0;
    }
    static Process StartHost(string folder)
    {
        // Single-file release: use a different executable path to test genuine cross-application control.
        var executable = Environment.ProcessPath!;
        if (!File.Exists(Path.ChangeExtension(executable, ".dll")))
        {
            executable = Path.Combine(folder, "TrayPilot-TestOwner.exe");
            if (!File.Exists(executable)) File.Copy(Environment.ProcessPath!, executable);
        }
        var p = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--test-host", folder } })!;
        for (int i = 0; i < 100 && !File.Exists(Path.Combine(folder, "ready")); i++) Thread.Sleep(100);
        if (!File.Exists(Path.Combine(folder, "ready"))) throw new Exception("Host did not become ready.");
        return p;
    }
    static int Test(string report)
    {
        var log = new List<string>();
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var controller = new Controller(Path.Combine(folder, "state"));
        Process? host = null;
        void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL: " + text); log.Add("PASS: " + text); File.WriteAllLines(report, log); }
        try
        {
            host = StartHost(folder); Thread.Sleep(500);
            var own = Scanner.Scan().Where(x => x.Pid == host.Id).ToList();
            Check(own.Count == 2, "Automatically discover both UID and GUID icons belonging to a separate process (message-only window).");
            Check(own.All(x => x.State == 0), "Both test icons initially displayed.");
            TestInteraction(controller, own, Check, folder);
            TestShellFeatures(controller, own, Check);
            controller.AddRule(own[0].Path); controller.Apply(own);
            Check(own.All(x => Native.State(x) == 1), "Hide both icons with NIS_HIDDEN while owner process stays alive.");
            Check(!host.HasExited, "Owner process remains running.");
            Check(Scanner.Scan().Count(x => x.Pid == host.Id && x.State == 1) == 2, "Hidden icons remain discoverable for restoration.");
            File.WriteAllText(Path.Combine(folder, "update"), "");
            for (int n = 0; n < 40 && !File.Exists(Path.Combine(folder, "updated")); n++) Thread.Sleep(100);
            Check(File.Exists(Path.Combine(folder, "updated")), "Owner updated its tooltip while hidden.");
            Check(own.All(x => Native.State(x) == 1), "Tooltip update does not undo hidden state.");
            var recovery = new Controller(Path.Combine(folder, "state")); recovery.RestoreManaged();
            Check(own.All(x => Native.State(x) == 0), "Persisted journal restores both icons from a new controller instance.");
            recovery.Apply(Scanner.Scan());
            Check(own.All(x => Native.State(x) == 1), "Saved application rule is reapplied.");
            recovery.RestoreManaged();
            Check(own.All(x => Native.State(x) == 0), "Exit restoration returns both icons to normal display.");
            TestEndTask(controller, own, Check);
            Check(host.WaitForExit(5000), "End-task menu terminates the test process."); host.Dispose(); host = null;
            File.Delete(Path.Combine(folder, "stop")); File.Delete(Path.Combine(folder, "ready"));
            host = StartHost(folder); Thread.Sleep(500);
            var restarted = Scanner.Scan().Where(x => x.Pid == host.Id).ToList();
            Check(restarted.Count == 2, "Discover icons again after target application restart.");
            recovery.Apply(restarted);
            Check(restarted.All(x => Native.State(x) == 1), "Existing path rule hides restarted application icons.");
            recovery.RemoveRule(restarted[0].Path); recovery.RestoreManaged();
            Check(restarted.All(x => Native.State(x) == 0), "Removing rule and restoring works after restart.");
            log.Add("OS: " + Environment.OSVersion.Version); log.Add("TrayPilot v0.4 integration tests complete.");
            return 0;
        }
        catch (Exception ex) { log.Add(ex.ToString()); return 1; }
        finally
        {
            try { new Controller(Path.Combine(folder, "state")).RestoreManaged(); } catch (Exception e) { log.Add("Cleanup: " + e.Message); }
            if (host != null) { File.WriteAllText(Path.Combine(folder, "stop"), ""); host.WaitForExit(5000); host.Dispose(); }
            File.WriteAllLines(report, log);
        }
    }
    static void TestInteraction(Controller controller, List<TrayEntry> own, Action<bool, string> check, string folder)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var form = new MainForm(controller, initialize: false);
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, own);
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("itemMenu", flags)!.GetValue(form)!;
        form.Show(); Application.DoEvents();
        var render = typeof(MainForm).GetMethod("RenderList", flags)!;
        render.Invoke(form, null);
        check(list.Items.Count == 2 && list.SmallImageList?.Images.Count == 2 && list.Items.Cast<ListViewItem>().All(x => x.ImageIndex >= 0),
            "List displays a corresponding image for each test tray icon.");
        var first = list.Items[0];
        render.Invoke(form, null);
        check(ReferenceEquals(first, list.Items[0]), "Unchanged refresh preserves rows for stable hovering and selection.");
        check(first.ToolTipText.Contains(own[0].Path) && first.ToolTipText.Contains($"PID {own[0].Pid}")
            && first.ToolTipText.Contains(own[0].Tooltip) && first.ToolTipText.Contains("状态："), "Hover details include state, tooltip, process ID and full path.");
        void ClickIcon(MouseButtons button = MouseButtons.Left, int clicks = 2)
        {
            var item = list.Items.Cast<ListViewItem>().First(x => ((TrayEntry)x.Tag!).Key == own[0].Key);
            item.EnsureVisible();
            var bounds = item.GetBounds(ItemBoundsPortion.Icon);
            typeof(Control).GetMethod(clicks == 2 ? "OnMouseDoubleClick" : "OnMouseClick", flags)!.Invoke(list,
                new object[] { new MouseEventArgs(button, clicks, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, 0) });
            if (button == MouseButtons.Right && clicks == 1)
            {
                check(menu.Visible && menu.Items.Count == 3 && menu.Items[0].Text == "属性" && menu.Items[2].Text == "结束任务",
                    "Right-click opens the manager's three-item context menu.");
                menu.Close();
            }
            var until = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < until)
            { Application.DoEvents(); Thread.Sleep(1); }
            check(!(bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)!, "Interaction completes.");
        }
        var autoRefresh = (CheckBox)typeof(MainForm).GetField("autoRefresh", flags)!.GetValue(form)!;
        var refreshTimer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("timer", flags)!.GetValue(form)!;
        var layoutMode = (RadioButton)typeof(MainForm).GetField("layoutMode", flags)!.GetValue(form)!;
        check(autoRefresh.Checked, "Automatic refresh is selected by default.");
        typeof(MainForm).GetMethod("UpdateTimer", flags)!.Invoke(form, null);
        check(refreshTimer.Enabled, "Default automatic refresh starts the timer.");
        autoRefresh.Checked = false;
        check(!refreshTimer.Enabled, "Disabling automatic refresh stops the timer.");
        check(list.Items.Cast<ListViewItem>().All(x => x.SubItems[1].Text == "正常"), "Visible state text has no overflow suffix.");
        foreach (int mode in new[] { 0, 1 })
        {
            layoutMode.Checked = mode == 1;
            ClickIcon(MouseButtons.Left, 1);
            ClickIcon(MouseButtons.Right, 1);
            ClickIcon(MouseButtons.Right, 2);
            check(own.All(x => Native.State(x) == 0) && !controller.HasRule(own[0].Path), "Single-click and right-click do not toggle icons in layout " + mode);
        }
        layoutMode.Checked = false;
        ClickIcon();
        check(own.All(x => Native.State(x) == 1) && controller.HasRule(own[0].Path), "Double-clicking visible icon hides application icons and saves rule.");
        var hidden = list.Items.Cast<ListViewItem>().First(x => ((TrayEntry)x.Tag!).Key == own[0].Key);
        check(hidden.ForeColor == Color.FromArgb(140, 145, 155) && hidden.ToolTipText.Contains("已隐藏"), "Hidden row and hover details reflect hidden state.");
        using var normal = TrayImages.Create(own[0] with { State = 0 }, 24);
        using var faded = TrayImages.Create(own[0] with { State = 1 }, 24);
        long Alpha(Bitmap bitmap) { long sum = 0; for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) sum += bitmap.GetPixel(x, y).A; return sum; }
        check(Alpha(normal) > 0 && Alpha(faded) > 0 && Alpha(faded) < Alpha(normal) * 0.4, "Hidden image is rendered at reduced opacity.");
        var selectedItem = list.Items.Cast<ListViewItem>().First(x => ((TrayEntry)x.Tag!).Key == own[0].Key);
        selectedItem.Selected = true;
        layoutMode.Checked = true;
        check(list.View == View.LargeIcon && list.LargeImageList?.ImageSize.Width == 40 && selectedItem.Selected,
            "Compact grid uses 40-pixel icons and preserves selection.");
        var search = (TextBox)typeof(MainForm).GetField("search", flags)!.GetValue(form)!;
        search.Text = own[0].Path;
        check(list.Items.Count == 2, "Search filters correctly in grid layout.");
        search.Text = "no-matching-tray-icon-test";
        check(list.Items.Count == 0, "Grid supports an empty search result.");
        search.Text = "";
        ClickIcon();
        check(own.All(x => Native.State(x) == 0) && !controller.HasRule(own[0].Path), "Double-clicking hidden icon restores application icons and removes rule.");
        using var fallback = TrayImages.Create(new TrayEntry { Path = "missing.exe", IconSnapshot = new byte[] { 1, 2, 3 } }, 24);
        check(Alpha(fallback) > 0, "Invalid snapshot and missing executable fall back to a visible application icon.");
        check(!refreshTimer.Enabled, "Manual actions do not restart disabled automatic refresh.");
        layoutMode.Checked = true;
        ClickIcon();
        check(own.All(x => Native.State(x) == 1), "Grid double-click hides icons.");
        var gridItem = list.Items.Cast<ListViewItem>().First(x => ((TrayEntry)x.Tag!).Key == own[0].Key);
        gridItem.Selected = true;
        layoutMode.Checked = false;
        check(list.View == View.Details && gridItem.Selected && gridItem.ToolTipText.Contains("已隐藏"), "Returning to list preserves selection and hidden state.");
        ClickIcon();
        check(own.All(x => Native.State(x) == 0), "List double-click restores icons after switching layouts.");
        var details = ProgramActions.Describe(own[0], false);
        check(details.Contains(own[0].Path) && details.Contains($"PID：{own[0].Pid}") && details.Contains("文件版本：") && details.Contains("启动时间："),
            "Properties include executable path, process ID, version and start time.");
        foreach (bool hide in new[] { true, false })
        {
            typeof(MainForm).GetMethod("ShowItemMenu", flags)!.Invoke(form, new object[] { own[0], new Point(30, 30) });
            check(menu.Items[1].Text == (hide ? "隐藏图标" : "显示图标"), "Context-menu label reflects live icon state.");
            menu.Items[1].PerformClick();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(1); }
            check(own.All(x => Native.State(x) == (hide ? 1 : 0)), "Context-menu command changes the application icon state.");
        }
        search.Text = own[0].Name; form.ActiveControl = search; search.Focus(); search.Select(1, 2);
        bool searchWasFocused = search.Focused;
        var searchHandle = search.Handle;
        var refresh = (Task)typeof(MainForm).GetMethod("RefreshAsync", flags)!.Invoke(form, null)!;
        check(search.Enabled && search.Focused == searchWasFocused && form.ActiveControl == search && search.SelectionStart == 1 && search.SelectionLength == 2,
            "Refresh leaves the search box enabled, focused and its selection intact.");
        search.Text = own[0].Path; search.Select(2, 3);
        var refreshDeadline = DateTime.UtcNow.AddSeconds(60);
        while (!refresh.IsCompleted && DateTime.UtcNow < refreshDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(refresh.IsCompleted, "Refresh completes while the user edits search.");
        refresh.GetAwaiter().GetResult();
        check(search.Handle == searchHandle && search.Focused == searchWasFocused && form.ActiveControl == search && search.Text == own[0].Path && search.SelectionStart == 2 && search.SelectionLength == 3
            && list.Items.Count == 2, "Refresh preserves search text, control, focus and caret, and applies the latest filter.");
        autoRefresh.Checked = true;
        check(refreshTimer.Enabled, "Automatic refresh can be enabled again.");
        autoRefresh.Checked = false;
    }

    static void TestEndTask(Controller controller, List<TrayEntry> own, Action<bool, string> check)
    {
        bool rejected = false;
        try { ProgramActions.EndAsync(own[0] with { Started = own[0].Started + 1 }).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && Scanner.SameOwner(own[0]), "End-task rejects stale process identity without killing the live process.");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var form = new MainForm(controller, initialize: false);
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, own.ToList());
        form.Show(); Application.DoEvents();
        typeof(MainForm).GetMethod("RenderList", flags)!.Invoke(form, null);
        typeof(MainForm).GetMethod("ShowItemMenu", flags)!.Invoke(form, new object[] { own[0], new Point(30, 30) });
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("itemMenu", flags)!.GetValue(form)!;
        menu.Items[2].PerformClick();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        check(!Scanner.SameOwner(own[0]) && list.Items.Count == 0, "End-task menu removes all icons belonging to the terminated process.");
    }

    static void TestShellFeatures(Controller controller, List<TrayEntry> own, Action<bool, string> check)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var packs = L.Packs();
        check(packs.ContainsKey("zh-CN") && packs.ContainsKey("en-US") && packs["zh-CN"].Strings.Keys.ToHashSet().SetEquals(packs["en-US"].Strings.Keys),
            "External Simplified Chinese and English packs contain matching translation keys.");
        check(new SavedState().CloseToTray && new SavedState().Language == "zh-CN" && !new SavedState().HotkeyEnabled,
            "New and migrated settings default to close-to-tray, Chinese and no reserved hotkey.");
        using var form = new MainForm(controller, initialize: false);
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, own.ToList());
        form.Show(); Application.DoEvents();
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        var search = (TextBox)typeof(MainForm).GetField("search", flags)!.GetValue(form)!;
        ((CheckBox)typeof(MainForm).GetField("autoRefresh", flags)!.GetValue(form)!).Checked = false;
        search.Text = own[0].Name;
        using (var settingsTimer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            Exception? settingsError = null;
            bool saved = false;
            IEnumerable<Control> Descendants(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    yield return child;
                    foreach (var nested in Descendants(child)) yield return nested;
                }
            }
            settingsTimer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(x => x != form);
                if (dialog == null) return;
                settingsTimer.Stop();
                try
                {
                    var controls = Descendants(dialog).ToList();
                    var combo = controls.OfType<ComboBox>().Single();
                    check(((MainForm.LanguageChoice)combo.SelectedItem!).Code == controller.Saved.Language, "Settings select the actual saved language on opening.");
                    combo.SelectedItem = combo.Items.Cast<MainForm.LanguageChoice>().Single(x => x.Code == "en-US");
                    controls.OfType<CheckBox>().Single(x => x.Text == L.T("关闭窗口后保留在托盘运行")).Checked = false;
                    controls.OfType<CheckBox>().Single(x => x.Name == "menuHotkeyEnabled").Checked = false;
                    typeof(Control).GetMethod("OnKeyDown", flags)!.Invoke(controls.OfType<TextBox>().Single(x => x.Name == "menuHotkey"),
                        new object[] { new KeyEventArgs(Keys.Control | Keys.Alt | Keys.K) });
                    controls.OfType<Button>().Single(x => x.Text == L.T("保存")).PerformClick();
                    saved = dialog.DialogResult == DialogResult.OK;
                    if (!saved) dialog.Close();
                }
                catch (Exception ex) { settingsError = ex; dialog.Close(); }
            };
            settingsTimer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            if (settingsError != null) throw settingsError;
            check(saved && controller.Saved.Language == "en-US" && !controller.Saved.CloseToTray
                && controller.Saved.Hotkey == (int)(Keys.Control | Keys.Alt | Keys.K),
                "Settings dialog saves language, close behavior and a captured key combination.");
            settingsTimer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            if (settingsError != null) throw settingsError;
            check(saved && controller.Saved.Language == "en-US", "Reopening settings retains the selected English language.");
            controller.Saved.CloseToTray = true;
        }
        L.Set("en-US"); typeof(MainForm).GetMethod("ApplyLanguage", flags)!.Invoke(form, null);
        check(form.Text.Contains("Tray Icon Manager") && list.Columns[0].Text == "Application name" && search.Text == own[0].Name
            && list.Items.Cast<ListViewItem>().All(x => x.SubItems[1].Text == "Shown"), "Language switch updates captions and state text without changing search.");
        using (var properties = InfoDialog.Create("Properties", ProgramActions.Properties(own[0], false), form.Font))
        {
            check(!properties.Controls.Cast<Control>().Any(x => x is TextBoxBase) && properties.Controls.OfType<ListView>().Single().LabelEdit == false,
                "Properties display is a non-editable table, not a text box.");
            check(properties.Controls.OfType<ListView>().Single().Items.Cast<ListViewItem>().Any(x => x.Text == "File version"), "Property labels are translated into English.");
        }
        L.Set("zh-CN"); typeof(MainForm).GetMethod("ApplyLanguage", flags)!.Invoke(form, null);
        search.Text = "no-results";
        typeof(MainForm).GetMethod("InitializeTray", flags)!.Invoke(form, null);
        var tray = (NotifyIcon)typeof(MainForm).GetField("trayIcon", flags)!.GetValue(form)!;
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("trayMenu", flags)!.GetValue(form)!;
        void BuildMenu() => typeof(MainForm).GetMethod("BuildTrayMenu", flags)!.Invoke(form, null);
        ToolStripMenuItem AppRow()
        {
            for (int page = 0; page < 100; page++)
            {
                typeof(MainForm).GetField("trayPage", flags)!.SetValue(form, page);
                BuildMenu();
                var row = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(x => x.Tag as string == own[0].Path);
                if (row != null) return row;
            }
            throw new Exception("Test owner missing from tray pages.");
        }
        BuildMenu();
        check(tray.Visible && menu.Items[0].Text == "打开主界面" && menu.Items[menu.Items.Count - 3].Text == "关于" && menu.Items[menu.Items.Count - 1].Text == "退出",
            "Tray menu places Open first, Settings and About near the bottom, and Exit last.");
        check(AppRow().Checked && list.Items.Count == 0, "Tray quick controls include applications excluded by the main search filter.");
        foreach (bool expectedHidden in new[] { true, false })
        {
            AppRow().PerformClick();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            BuildMenu();
            check(AppRow().Checked == !expectedHidden && own.All(x => Native.State(x) == (expectedHidden ? 1 : 0)),
                "Single-click quick control toggles real state and updates its check mark.");
        }
        var allRows = new HashSet<string>();
        int previousPage = -1;
        for (int page = 0; page < 100; page++)
        {
            typeof(MainForm).GetField("trayPage", flags)!.SetValue(form, page); BuildMenu();
            int actualPage = (int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)!;
            if (actualPage == previousPage) break;
            previousPage = actualPage;
            var paths = menu.Items.OfType<ToolStripMenuItem>().Where(x => x.Tag is string).Select(x => (string)x.Tag!).ToList();
            check(paths.Count <= MainForm.TrayPageSize && paths.All(allRows.Add), "Quick-control pages contain at most ten applications without duplicates.");
        }
        if (previousPage > 0)
        {
            typeof(MainForm).GetField("trayPage", flags)!.SetValue(form, 0); BuildMenu();
            menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("下一页")).PerformClick();
            Application.DoEvents();
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == 1 && menu.Visible, "Next-page click reopens quick controls on the requested page.");
            void Wheel(int delta)
            {
                Native.SendMessageW(menu.Handle, 0x020A, (nint)((long)(ushort)(short)delta << 16), 0);
                Application.DoEvents();
            }
            Wheel(120);
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == 0 && menu.Visible, "Wheel up moves to the previous page and keeps the menu open.");
            Wheel(120);
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == 0, "Wheel up stops at the first page.");
            Wheel(-60);
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == 0, "High-resolution wheel deltas accumulate before changing page.");
            Wheel(-60);
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == 1 && menu.Visible, "Wheel down moves to the next page.");
            Wheel(-12000);
            check((int)typeof(MainForm).GetField("trayPage", flags)!.GetValue(form)! == previousPage && menu.Visible, "Fast scrolling stops at the final page.");
            menu.Close();
        }
        check(allRows.Contains(own[0].Path), "Pagination keeps the test application reachable.");
        foreach (string eventName in new[] { "OnMouseClick", "OnMouseDoubleClick" })
        {
            form.Hide();
            typeof(NotifyIcon).GetMethod(eventName, flags)!.Invoke(tray, new object[] { new MouseEventArgs(MouseButtons.Left, eventName == "OnMouseClick" ? 1 : 2, 0, 0, 0) });
            check(form.Visible, "Left tray click/double-click opens the main window.");
        }
        BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("TrayPilot（本程序）")).PerformClick();
        var selfDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < selfDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(!tray.Visible && !controller.Saved.ShowTrayIcon, "The manager tray icon can be hidden and the preference is saved.");
        form.Close();
        check(!form.Visible && !form.IsDisposed, "Close-to-tray continues running when the manager tray icon is hidden.");
        form.ActivateMainWindow();
        check(form.Visible, "The activation entry point reopens a manager with no tray icon.");
        BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("TrayPilot（本程序）")).PerformClick();
        selfDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < selfDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(tray.Visible && controller.Saved.ShowTrayIcon, "The manager tray icon can be shown again.");
        var mainHotkey = (GlobalHotkey)typeof(MainForm).GetField("mainHotkey", flags)!.GetValue(form)!;
        Keys mainChosen = Keys.None;
        foreach (var key in new[] { Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5 })
            if (mainHotkey.TrySet(true, Keys.Control | Keys.Alt | Keys.Shift | key)) { mainChosen = Keys.Control | Keys.Alt | Keys.Shift | key; break; }
        check(mainChosen != Keys.None, "A separate main-window hotkey can be registered.");
        form.Hide();
        int mainId = mainHotkey.Matches((nint)0x4A11) ? 0x4A11 : 0x4A12;
        Native.SendMessageW(form.Handle, 0x0312, mainId, 0); Application.DoEvents();
        check(form.Visible && !menu.Visible, "The main-window hotkey opens the window without opening quick controls.");
        var hotkey = (GlobalHotkey)typeof(MainForm).GetField("hotkey", flags)!.GetValue(form)!;
        Keys chosen = Keys.None;
        foreach (var key in new[] { Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11 })
            if (hotkey.TrySet(true, Keys.Control | Keys.Alt | Keys.Shift | key)) { chosen = Keys.Control | Keys.Alt | Keys.Shift | key; break; }
        check(chosen != Keys.None, "A configurable global hotkey can be registered.");
        using (var competitor = new GlobalHotkey(list.Handle))
        {
            check(!competitor.TrySet(true, chosen), "An occupied global hotkey is rejected.");
            check(!hotkey.TrySet(true, Keys.T), "An unmodified key is rejected without dropping the old registration.");
            controller.Saved.CloseToTray = true;
            controller.AddRule(own[0].Path); controller.Apply(own);
            form.Close(); Application.DoEvents();
            check(!form.IsDisposed && !form.Visible && tray.Visible && own.All(x => Native.State(x) == 1),
                "Closing to tray keeps the application alive and preserves hidden icons.");
            // Route the registered WM_HOTKEY while the main window is hidden.
            int id = hotkey.Matches((nint)0x4A01) ? 0x4A01 : 0x4A02;
            Native.SendMessageW(form.Handle, 0x0312, id, 0); Application.DoEvents();
            check(menu.Visible && !form.Visible, "Global-hotkey dispatch opens quick controls while the main window stays hidden.");
            menu.Close();
            check(hotkey.TrySet(false, chosen) && competitor.TrySet(true, chosen), "Disabling the hotkey releases the registration.");
        }
        typeof(MainForm).GetMethod("OpenMainWindow", flags)!.Invoke(form, null);
        check(form.Visible, "Open main window restores the hidden manager.");
        form.RequestExit();
        check(form.IsDisposed && own.All(x => Native.State(x) == 0), "Explicit Exit bypasses close-to-tray and restores managed icons.");
        using var directExit = new MainForm(controller, initialize: false);
        directExit.Show(); typeof(MainForm).GetMethod("InitializeTray", flags)!.Invoke(directExit, null);
        controller.Saved.CloseToTray = false; controller.Apply(own);
        directExit.Close();
        check(directExit.IsDisposed && own.All(x => Native.State(x) == 0), "With close-to-tray disabled, closing exits and restores icons.");
        controller.Saved.CloseToTray = true; controller.Saved.Language = "en-US";
        controller.Saved.HotkeyEnabled = true; controller.Saved.Hotkey = (int)chosen;
        controller.Saved.MainHotkeyEnabled = true; controller.Saved.MainHotkey = (int)mainChosen; controller.Saved.ShowTrayIcon = false;
        controller.Save();
        var settingsFile = (string)typeof(Controller).GetField("file", flags)!.GetValue(controller)!;
        var loaded = new Controller(Path.GetDirectoryName(settingsFile)!).Saved;
        check(loaded.CloseToTray && loaded.Language == "en-US" && loaded.HotkeyEnabled && loaded.Hotkey == (int)chosen && loaded.MainHotkeyEnabled && loaded.MainHotkey == (int)mainChosen && !loaded.ShowTrayIcon,
            "Language, close-to-tray and hotkey settings survive a controller reload.");
        L.Set("missing-language"); check(L.Current == "zh-CN" && L.T("关于") == "关于", "Unknown language safely falls back to Simplified Chinese.");
        controller.Saved.Language = "zh-CN"; controller.Saved.HotkeyEnabled = false; controller.Saved.MainHotkeyEnabled = false; controller.Saved.ShowTrayIcon = true; controller.RemoveRule(own[0].Path);
    }
}

