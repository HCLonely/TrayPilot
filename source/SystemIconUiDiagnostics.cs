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
                var point = new Point(target.Bounds.Left + 12, target.Bounds.Top + target.Bounds.Height / 2);
                DoubleClick(point); DoubleClick(point);
                await AwaitState(bit, true);
                Check(session.Hidden == (initial | bit), "Double-click hides only the hit row; rapid reentry is ignored");
                DoubleClick(point); await AwaitState(0, false);
                Check(session.Hidden == initial, "Second double-click restores the row");
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
                using (var screenshot = new Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, dialog.Size)); screenshot.Save(report + ".png"); }
                CheckSelectionRendering(Path.GetDirectoryName(Path.GetFullPath(report))!, log);
            }
            catch (Exception ex) { failure = ex; }
            finally { dialog.Close(); }
        };
        form.Shown += (_, _) =>
        {
            driver.Start(); typeof(MainForm).GetMethod("ShowSystemIcons", flags)!.Invoke(form, null); form.RequestExit();
        };
        Application.Run(form);
        File.WriteAllLines(report, log);
        if (failure != null) throw failure;
        return 0;
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
            { int color = bitmap.GetPixel(x, y).ToArgb(); if (color == UiTheme.Selection.ToArgb()) selectedPixels++; if (color == UiTheme.Highlight.ToArgb()) hoverPixels++; }
            if (selectedPixels < 500 || hoverPixels < 500) throw new IOException($"Selection and hover are not distinct in {theme}/{grid}");
            bitmap.Save(Path.Combine(folder, $"selection-{theme}-{(grid ? "grid" : "list")}.png"));
            log.Add($"PASS {theme}/{(grid ? "grid" : "list")} selection remains prominent and distinct from hover");
            window.Close();
        }
    }
}
