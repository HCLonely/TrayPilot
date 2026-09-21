using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestSystemIconUi(string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var log = new List<string>();
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "ui-state-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(new Controller(folder), initialize: false);
        using var driver = new System.Windows.Forms.Timer { Interval = 200 };
        Exception? failure = null; int attempts = 0;
        void Check(bool ok, string message) { if (!ok) throw new IOException(message); log.Add("PASS " + message); }
        driver.Tick += async (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.Name == "LiveSystemIconsDialog");
            if (dialog == null) return;
            if (form.systemIconSessionForDiagnostics == null && ++attempts < 150) return;
            driver.Stop();
            try
            {
                var session = form.systemIconSessionForDiagnostics ?? throw new IOException("Taskbar connection timed out");
                var rows = (TrayListView)dialog.Controls.Find("SystemIconRows", true).Single();
                var hide = (Button)dialog.Controls.Find("HideSystemIcon", true).Single();
                var restore = (Button)dialog.Controls.Find("RestoreSystemIcon", true).Single();
                Check(rows.Items.Count == 12, "Catalog exposes all 12 supported categories");
                Check(rows.SmallImageList?.Images.Count == 12 && rows.Items.Cast<ListViewItem>().All(x => x.ImageIndex == x.Index),
                    "Every system icon row has a corresponding image");
                foreach (var entry in SystemIconCatalog.Items)
                {
                    using var fallback = SystemIconImages.Create(entry.Mask, 24, ("X", "Missing TrayPilot Test Font"));
                    int pixels = 0;
                    for (int y = 0; y < fallback.Height; y++) for (int x = 0; x < fallback.Width; x++)
                        if (fallback.GetPixel(x, y).A > 0) pixels++;
                    Check(pixels > 20, $"Built-in fallback renders for category {entry.Mask}");
                }
                var appearance = session.ReadAppearances();
                log.Add("INFO Taskbar appearance available for: " + string.Join(", ", SystemIconCatalog.Items.Where((x, i) => !string.IsNullOrEmpty(appearance[i].Text)).Select(x => x.Mask)));
                int initial = session.Hidden;
                var target = rows.Items.Cast<ListViewItem>().First(x => ((int)x.Tag! & session.Found & ~initial) != 0);
                int bit = (int)target.Tag!;
                async Task AwaitState(int requested, bool? hidden)
                {
                    for (int i = 0; i < 80; i++)
                    {
                        if (session.Requested == requested && (!hidden.HasValue || ((session.Hidden & bit) != 0) == hidden) && hide.Enabled) return;
                        await Task.Delay(100);
                    }
                    throw new IOException("UI operation timed out");
                }
                void DoubleClick(Point point) => typeof(Control).GetMethod("OnMouseDoubleClick", flags)!.Invoke(rows,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 2, point.X, point.Y, 0) });
                target.Selected = true; target.EnsureVisible(); rows.Focus();
                for (int i = 0; i < 100 && !hide.Enabled; i++) await Task.Delay(100);
                Check(hide.Enabled, "Dialog connection completes before interaction");
                var point = new Point(target.Bounds.Left + 12, target.Bounds.Top + target.Bounds.Height / 2);
                DoubleClick(point); DoubleClick(point);
                await AwaitState(bit, true);
                Check(session.Hidden == (initial | bit), "Double-click hides only the hit row; rapid reentry is ignored");
                Check(new Controller(folder).Saved.HiddenSystemIcons == bit, "Hide choice is saved immediately");
                DoubleClick(point); await AwaitState(0, false);
                Check(session.Hidden == initial, "Second double-click restores the row");
                Check(new Controller(folder).Saved.HiddenSystemIcons == 0, "Restore choice is saved immediately");
                var other = rows.Items.Cast<ListViewItem>().First(x => x != target); other.Selected = true;
                typeof(Control).GetMethod("OnMouseDown", flags)!.Invoke(rows, new object[] { new MouseEventArgs(MouseButtons.Right, 1, point.X, point.Y, 0) });
                Check(rows.SelectedItems.Count == 1 && rows.SelectedItems[0] == target, "Right-click selects its target instead of the previous row");
                var menu = rows.ContextMenuStrip!; menu.Show(rows, point);
                var toggle = (ToolStripMenuItem)menu.Items.Find("ToggleSystemIcon", false).Single();
                Check(menu.Items.Count == 1 && toggle.Text == L.T("hideSelected"), "Visible icon menu has one hide action without duplicates");
                toggle.PerformClick(); menu.Close();
                await AwaitState(bit, true);
                menu.Show(rows, point);
                Check(menu.Items.Count == 1 && toggle.Text == L.T("restoreSelected"), "Hidden icon menu has one restore action without duplicates");
                toggle.PerformClick(); menu.Close();
                await AwaitState(0, false);
                Check(session.Hidden == initial, "Context-menu hide and restore use the selected target");
                DoubleClick(new Point(20, rows.ClientSize.Height - 5)); await Task.Delay(300);
                Check(session.Requested == 0, "Double-clicking empty space does not toggle a selection");
                var absent = rows.Items.Cast<ListViewItem>().FirstOrDefault(x => (session.Found & (int)x.Tag!) == 0);
                if (absent != null)
                {
                    absent.Selected = true; hide.PerformClick(); await AwaitState((int)absent.Tag!, null);
                    Check(absent.SubItems[1].Text == L.T("systemNativeWaiting"), "Absent icon can be queued for hiding");
                    restore.PerformClick(); await AwaitState(0, null);
                }
                var discovery = (ComboBox)dialog.Controls.Find("SystemIconDiscovery", true).Single();
                Check(discovery.Items.Count == 2 && discovery.SelectedIndex == 0 && !session.Legacy,
                    "New compatibility method is the default and original method remains selectable");
                discovery.SelectedIndex = 1;
                Check(new Controller(folder).Saved.UseLegacySystemIconDiscovery, "Original method selection is persisted");
                for (int i = 0; i < 500 && !discovery.Enabled; i++) await Task.Delay(100);
                Check(discovery.Enabled, "Original connection attempt completes and allows switching back");
                discovery.SelectedIndex = 0;
                for (int i = 0; i < 500 && !discovery.Enabled; i++) await Task.Delay(100);
                Check(discovery.Enabled && form.systemIconSessionForDiagnostics is { Legacy: false, Error: 0 },
                    "Switching back reconnects with the new method");
                Check(!new Controller(folder).Saved.UseLegacySystemIconDiscovery, "New method selection is persisted");
                using (var screenshot = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, dialog.Size)); screenshot.Save(report + ".png"); }
                CheckSelectionRendering(Path.GetDirectoryName(Path.GetFullPath(report))!, log);
            }
            catch (Exception ex) { failure = ex; }
            finally { dialog.Close(); }
        };
        form.Shown += async (_, _) =>
        {
            try
            {
                driver.Start(); typeof(MainForm).GetMethod("ShowSystemIcons", flags)!.Invoke(form, null);
                for (int i = 0; i < 50 && form.systemIconSessionForDiagnostics != null; i++) await Task.Delay(100);
                Check(form.systemIconSessionForDiagnostics == null, "Closing an unused system-icon dialog releases its native session");
            }
            catch (Exception ex) { failure ??= ex; }
            finally { form.RequestExit(); }
        };
        Application.Run(form);
        if (failure == null)
        {
            try { CheckSystemIconRestart(folder, log); CheckSystemIconExitPreference(folder, log); }
            catch (Exception ex) { failure = ex; }
        }
        File.WriteAllLines(report, log);
        if (failure != null) throw failure;
        return 0;
    }

    static void CheckSystemIconRestart(string folder, List<string> log)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var controller = new Controller(folder);
        controller.Saved.RestoreIconsOnExit = true;
        controller.SetHiddenSystemIcons(2 | 4); // Network plus a possibly absent battery indicator.
        // A failed save must preserve the previous preference in memory and on disk.
        Directory.CreateDirectory(Path.Combine(folder, "settings.json.tmp"));
        try
        {
            try { controller.SetHiddenSystemIcons(0); throw new Exception("Expected save failure"); }
            catch (UnauthorizedAccessException) { }
            if (controller.Saved.HiddenSystemIcons != 6 || new Controller(folder).Saved.HiddenSystemIcons != 6)
                throw new IOException("Failed save lost the previous preference");
        }
        finally { Directory.Delete(Path.Combine(folder, "settings.json.tmp")); }
        log.Add("PASS Failed preference save preserves memory and disk state");
        for (int launch = 0; launch < 2; launch++)
        {
            using var restarted = new MainForm(new Controller(folder), initialize: false);
            typeof(MainForm).GetMethod("SetupSystemIconWatch", flags)!.Invoke(restarted, null);
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            int attempts = 0; bool clearing = false; Exception? error = null;
            timer.Tick += async (_, _) =>
            {
                try
                {
                    if (++attempts > 300) throw new IOException($"Startup preference application timed out (launch={launch}, clearing={clearing})");
                    var session = restarted.systemIconSessionForDiagnostics;
                    if (clearing && session == null)
                    {
                        if (new Controller(folder).Saved.HiddenSystemIcons != 0) throw new IOException("Restore all did not clear saved preferences");
                        timer.Stop(); restarted.RequestExit(); return;
                    }
                    if (session == null || session.Error != 0) return;
                    if (!clearing && (session.Requested != 6 || (session.Hidden & 2) == 0)) return;
                    if (launch == 0)
                    {
                        if (session.ReadAppearances().Any(x => !string.IsNullOrEmpty(x.Text)))
                            throw new IOException("Background sessions should not collect icon appearances");
                        log.Add("PASS Background system-icon management skips appearance collection");
                        timer.Stop(); restarted.RequestExit(); return;
                    }
                    if (!clearing)
                    {
                        timer.Stop();
                        int hiddenBefore = session.Hidden;
                        var actions = (FlowLayoutPanel)typeof(MainForm).GetField("actions", flags)!.GetValue(restarted)!;
                        actions.Controls.OfType<Button>().Single(x => x.Text == L.T("restoreAll")).PerformClick();
                        for (int i = 0; i < 50 && (bool)typeof(MainForm).GetField("busy", flags)!.GetValue(restarted)!; i++)
                            await Task.Delay(100);
                        await Task.Delay(500); // Include native ticks and any deferred session release.
                        if ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(restarted)! ||
                            !ReferenceEquals(restarted.systemIconSessionForDiagnostics, session) ||
                            session.Requested != 6 || session.Hidden != hiddenBefore ||
                            new Controller(folder).Saved.HiddenSystemIcons != 6)
                            throw new IOException("Main-window Restore all changed system-icon visibility, preferences or session.");
                        log.Add("PASS Main-window Restore all preserves system-icon visibility, saved choices and active session");
                        timer.Start();
                        typeof(MainForm).GetMethod("RestoreLiveSystemIcons", flags)!.Invoke(restarted, null);
                        clearing = true; return;
                    }
                    if (session.Requested != 0 || session.Acknowledged == 0 || (session.Hidden & 2) != 0) return;
                    if (new Controller(folder).Saved.HiddenSystemIcons != 0) throw new IOException("Restore all did not clear saved preferences");
                    timer.Stop(); restarted.RequestExit();
                }
                catch (Exception ex) { error = ex; timer.Stop(); restarted.RequestExit(); }
            };
            restarted.Shown += (_, _) => timer.Start();
            Application.Run(restarted);
            if (error != null) throw error;
            if (launch == 0 && new Controller(folder).Saved.HiddenSystemIcons != 6) throw new IOException("Exit discarded hide preferences");
            Thread.Sleep(500); // Allow the helper's restoration timer to finish before the next session.
        }
        log.Add("PASS Startup applies saved icons without opening the system-icons dialog; exit preserves preferences; next launch reapplies them");
        log.Add("PASS Explicit system-icon restoration clears saved system-icon choices");
    }
    static void CheckSystemIconExitPreference(string folder, List<string> log)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void WaitFor(Func<bool> ready)
        {
            for (int i = 0; i < 80; i++) { if (ready()) return; Thread.Sleep(100); }
            throw new IOException("System-icon exit preference test timed out");
        }
        using var observer = new SystemIconSession();
        WaitFor(() => observer.Ticks > 0 && observer.Error == 0);
        int bit = SystemIconCatalog.Items.Select(x => x.Mask).First(x => (observer.Found & ~observer.Hidden & x) != 0);
        try
        {
            var controller = new Controller(Path.Combine(folder, "no-restore"));
            using (var form = new MainForm(controller, initialize: false))
            {
                _ = form.Handle;
                var session = new SystemIconSession(initialMask: bit);
                typeof(MainForm).GetField("systemIconSession", flags)!.SetValue(form, session);
                WaitFor(() => (observer.Hidden & bit) != 0);
                form.RequestExit();
                Thread.Sleep(600);
                if (!form.IsDisposed || (observer.Hidden & bit) == 0) throw new IOException("Default exit restored a system icon");
                log.Add("PASS Default exit stops native management without restoring the hidden system icon");
            }
            controller = new Controller(Path.Combine(folder, "restore"));
            controller.Saved.RestoreIconsOnExit = true;
            using (var form = new MainForm(controller, initialize: false))
            {
                _ = form.Handle;
                var session = new SystemIconSession(initialMask: bit);
                typeof(MainForm).GetField("systemIconSession", flags)!.SetValue(form, session);
                WaitFor(() => session.Ticks > 0 && (session.Hidden & bit) != 0);
                form.RequestExit();
                WaitFor(() => (observer.Hidden & bit) == 0);
                if (!form.IsDisposed) throw new IOException("Restoring exit did not close the window");
                log.Add("PASS Opt-in exit restores system icons, including originals preserved by an earlier exit");
            }
        }
        finally
        {
            using var cleanup = new SystemIconSession();
            int request = cleanup.Set(0);
            WaitFor(() => cleanup.Acknowledged == request && cleanup.Error == 0);
        }
    }

    static void CheckSelectionRendering(string folder, List<string> log)
    {
        foreach (string theme in new[] { "light", "dark" }) foreach (bool grid in new[] { false, true })
        {
            UiTheme.Set(theme);
            using var window = new Form { ClientSize = new(520, 260) };
            using var images = new ImageList { ImageSize = new(40, 40), ColorDepth = ColorDepth.Depth32Bit };
            using var icon = AppIcon.Draw(40); images.Images.Add(icon);
            var list = new TrayListView { Dock = DockStyle.Fill, View = grid ? View.LargeIcon : View.Details, HideSelection = false, LargeImageList = images };
            list.Columns.Add("Application", 270); list.Columns.Add("State", 220);
            foreach (string name in new[] { "Selected application", "Pointer is here", "Other application" })
            { var item = new ListViewItem(name, 0); item.SubItems.Add("Visible"); list.Items.Add(item); }
            window.Controls.Add(list); UiTheme.Apply(window); window.Show(); Application.DoEvents();
            if (grid) { Native.SendMessageW(list.Handle, 0x1035, 0, (nint)((110 << 16) | 160)); list.ArrangeIcons(ListViewAlignment.Top); }
            list.Items[0].Selected = true;
            var second = list.Items[1].Bounds;
            typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list,
                new object[] { new MouseEventArgs(MouseButtons.None, 0, second.Left + 10, second.Top + 10, 0) });
            window.Focus(); Application.DoEvents();
            using var bitmap = new Bitmap(list.Width, list.Height); list.DrawToBitmap(bitmap, list.ClientRectangle);
            int selectedPixels = 0, hoverPixels = 0;
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            { int color = bitmap.GetPixel(x, y).ToArgb(); if (color == UiTheme.Selection.ToArgb()) selectedPixels++; if (color == UiTheme.Hover.ToArgb()) hoverPixels++; }
            if (selectedPixels < 500 || hoverPixels < 500) throw new IOException($"Selection and hover are not distinct in {theme}/{grid}");
            bitmap.Save(Path.Combine(folder, $"selection-{theme}-{(grid ? "grid" : "list")}.png"));
            log.Add($"PASS {theme}/{(grid ? "grid" : "list")} selection remains prominent and distinct from hover");
            window.Close();
        }
    }
}
