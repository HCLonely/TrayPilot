using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static SystemIconsSnapshot SystemDashboardSample(bool loading = false, bool failed = false) => new(!loading && !failed, loading,
        16 | 1024 | 2048, 1 | 2 | 8 | 256 | 1024 | 2048, 1024 | 2048, false, false,
        failed ? "Preview: connection unavailable (no native session started)" : "", "",
        new (string, string)[12]);

    static int PreviewSystemDashboard(string output, bool dark, bool english, bool loading, bool failed, bool small, bool compatibility, bool bottom)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "system-dashboard-preview-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Language = english ? "en-US" : "zh-CN"; controller.Saved.Theme = dark ? "dark" : "light";
        controller.Saved.HiddenSystemIcons = 16 | 1024 | 2048;
        using var main = new MainForm(controller, initialize: false); if (small) main.Size = main.MinimumSize;
        DashboardCall(main, "ShowSystemIcons"); var page = DashboardField<SystemIconsPage>(main, "dashboardSystemPage");
        page.ReadOnlyPreview = true; page.RefreshState(SystemDashboardSample(loading, failed)); page.Choose(failed ? 1 : 1024);
        using var capture = new System.Windows.Forms.Timer { Interval = 700 };
        capture.Tick += (_, _) =>
        {
            var target = compatibility ? main.OwnedForms.FirstOrDefault() : main;
            if (target == null) return; capture.Stop();
            if (bottom)
            {
                var grid = (Panel)typeof(SystemIconsPage).GetField("grid", DashboardFlags)!.GetValue(page)!;
                grid.AutoScrollPosition = new(0, int.MaxValue);
                for (int i = 0; i < 100; i++) page.RefreshState(page.Snapshot);
            }
            using var bitmap = new Bitmap(target.Width, target.Height); target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, target.Size)); bitmap.Save(output);
            if (target != main) target.Close(); main.Close();
        };
        main.Shown += (_, _) =>
        {
            capture.Start();
            if (compatibility) typeof(SystemIconsPage).GetMethod("OpenCompatibility", DashboardFlags)!.Invoke(page, null);
        };
        Application.Run(main); return 0;
    }

    static int TestSystemDashboard(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        L.Set("zh-CN"); UiTheme.Set("light");
        var state = SystemDashboardSample(); int calls = 0, lastRestoreMask = 0;
        TaskCompletionSource<SystemIconsSnapshot>? pending = null;
        bool failChange = false, failConnect = false;
        Task<SystemIconsSnapshot> Change(int mask, int restore)
        {
            calls++; lastRestoreMask = restore;
            if (failChange) return Task.FromException<SystemIconsSnapshot>(new IOException("Simulated persistence failure"));
            if (pending != null) return pending.Task;
            state = state with { Requested = mask, Hidden = mask & state.Found };
            return Task.FromResult(state);
        }
        Task<SystemIconsSnapshot> Connect() => failConnect ? Task.FromException<SystemIconsSnapshot>(new IOException("Simulated connection failure")) : Task.FromResult(state = state with { Connected = true, Connecting = false, Error = "" });
        using var font = new Font("Microsoft YaHei UI", 9.5f);
        using var host = new Form { ClientSize = new(1180, 880), Font = font };
        using var page = new SystemIconsPage(state, () => state, Connect, Change,
            legacy => { state = state with { Legacy = legacy }; return Connect(); }, font);
        host.Controls.Add(page); host.Show();
        void Complete(Task task)
        {
            for (int i = 0; i < 100 && !task.IsCompleted; i++) { Application.DoEvents(); Thread.Sleep(5); }
            task.GetAwaiter().GetResult();
        }
        Check(page.Cards.Count == 12 && page.Cards.Select(card => card.Mask).Distinct().Count() == 12, "All 12 supported system controls have individual cards.");
        Check(page.Cards.All(card => card.Toggle.AccessibleName?.Contains("显示") == true), "Visibility switches expose individual accessible names.");
        Check(page.Cards.Single(card => card.Mask == 1).Toggle.Checked && !page.Cards.Single(card => card.Mask == 16).Toggle.Checked,
            "Switch-on means display and switch-off means saved hiding, including absent controls.");
        Check(state.StateKey(4) == "systemNativeNotFound" && state.StateKey(16) == "systemNativeWaiting", "Absent controls distinguish unconfigured and waiting-to-hide states.");
        Check(state.StateKey(1024) == "systemNativeHidden" && state.StateKey(1) == "systemNativeVisible", "Found controls use actual hidden and visible state.");
        Check((state with { Requested = state.Requested | 1 }).StateKey(1) == "systemPagePending", "Saved hiding without a hidden acknowledgement is shown as pending.");
        var shared = state with { Found = state.Found | 48, Hidden = state.Hidden & ~48, Shared = true };
        Check(shared.StateKey(16) == "systemSharedIndicator" && shared.StateKey(32) == "systemNativeVisible", "A singly requested shared indicator is pending instead of falsely reported hidden.");
        Check((shared with { Requested = shared.Requested | 48, Hidden = shared.Hidden | 48 }).StateKey(16) == "systemNativeHidden", "Both hidden shared choices report the acknowledged hidden state.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool focusAvailable = page.Cards.Single(card => card.Mask == 1).Toggle.Focus() && page.Cards.Single(card => card.Mask == 1).Toggle.Focused;
        var saving = page.ToggleAsync(1); int before = state.Requested;
        Check(page.Working && page.Snapshot.Requested == before && page.Cards.Single(card => card.Mask == 1).Toggle.Checked, "Pending saves retain the original switch value until confirmation.");
        Check(page.Cards.All(card => !card.Toggle.Enabled), "Saving disables all visibility switches to prevent repeated submissions.");
        Check(!page.NavigationAllowed, "Navigation cannot interrupt an outstanding visibility change.");
        var duplicate = page.ToggleAsync(2); Complete(duplicate); Check(calls == 1, "A second operation cannot overlap an outstanding save.");
        state = state with { Requested = before | 1, Hidden = state.Hidden | 1 }; pending.SetResult(state); Complete(saving); pending = null;
        Check(!page.Working && !page.Cards.Single(card => card.Mask == 1).Toggle.Checked, "The switch changes only after a successful response.");
        Check(!focusAvailable || page.Cards.Single(card => card.Mask == 1).Toggle.Focused,
            "Successful saves preserve available native keyboard focus (hidden test hosts may not receive OS focus).");
        Complete(page.UndoAsync()); Check(page.Snapshot.Requested == before, "Undo restores the prior system-hidden choice mask.");
        Complete(page.RestoreAllAsync()); Check(page.Snapshot.Requested == 0 && page.Cards.All(card => card.Toggle.Checked) && lastRestoreMask == SystemIconCatalog.All,
            "Restore-all requests restoration of every system category and clears absent choices.");
        Complete(page.UndoAsync()); Check(page.Snapshot.Requested == before, "Restore-all is undoable without editing application rules.");
        failChange = true; Complete(page.ToggleAsync(2));
        Check(page.Snapshot.Requested == before && page.Cards.Single(card => card.Mask == 2).Toggle.Checked && page.Snapshot.Error.Contains("persistence failure"), "Failed saves retain original switch choices and expose the error.");
        failChange = false; state = state with { Connected = false, Error = "offline" }; page.RefreshState(state);
        Check(page.Cards.All(card => !card.Toggle.Enabled), "Connection failure disables switches while retaining their saved values.");
        failConnect = true; Complete(page.ReconnectAsync()); Check(!page.Snapshot.Connected && page.Snapshot.Requested == before, "Failed retry retains saved hiding choices.");
        failConnect = false; Complete(page.ReconnectAsync()); Check(page.Snapshot.Connected && page.Snapshot.Requested == before, "Successful retry restores availability without clearing choices.");
        Complete(page.ChangeDiscoveryAsync(true)); Check(page.Snapshot.Legacy && page.Snapshot.Requested == before, "Discovery-mode switching retains the hidden-choice mask.");
        Complete(page.ToggleAsync(4)); failChange = true; Complete(page.UndoAsync());
        Check(((Button)typeof(SystemIconsPage).GetField("undo", DashboardFlags)!.GetValue(page)!).Enabled,
            "Failed undo retains the previous operation for retry."); failChange = false;
        var radios = Descendants(page).OfType<RadioButton>().Where(radio => radio.Appearance == Appearance.Button).ToList();
        radios[1].Checked = true; Check(page.Cards.Count(card => card.Visible) == 4, "Common-controls filtering shows four controls.");
        radios[2].Checked = true; Check(page.Cards.Count(card => card.Visible) == 5, "Indicator filtering shows five controls.");
        radios[3].Checked = true; Check(page.Cards.Count(card => card.Visible) == 3, "Taskbar filtering shows three controls.");
        radios[0].Checked = true; page.RefreshState(state with { Connecting = true });
        Check(page.Cards.Count(card => card.Visible) == 12 && page.Cards.All(card => !card.Toggle.Enabled), "Loading retains all controls and saved values while disabling changes.");
        var grid = (Panel)typeof(SystemIconsPage).GetField("grid", DashboardFlags)!.GetValue(page)!;
        void Bottom() { grid.AutoScrollPosition = new(0, int.MaxValue); Application.DoEvents(); }
        bool EndsAtCards() => Math.Abs(page.Cards.Where(card => card.Visible).Max(card => card.Bottom) + 4 * page.DeviceDpi / 96 - grid.ClientSize.Height) <= 2;
        host.ClientSize = new(1180, 650); Application.DoEvents(); Bottom();
        Check(grid.VerticalScroll.Visible && EndsAtCards(), "Scrolling stops at the last card row without an empty trailing viewport.");
        int extent = grid.DisplayRectangle.Height, offset = grid.AutoScrollPosition.Y;
        for (int i = 0; i < 100; i++) page.RefreshState(page.Snapshot);
        Check(grid.DisplayRectangle.Height == extent && grid.AutoScrollPosition.Y == offset && EndsAtCards(), "One hundred state refreshes preserve content extent and bottom scroll position.");
        radios[1].Checked = true; Application.DoEvents(); Bottom();
        Check(!grid.VerticalScroll.Visible && grid.AutoScrollPosition.Y == 0, "Filtering to a fitting group clears the previous scroll offset and scrollbar.");
        radios[0].Checked = true; Application.DoEvents(); Bottom();
        Check(EndsAtCards(), "Returning to all controls restores the exact card extent.");
        host.ClientSize = new(800, 650); Application.DoEvents(); Bottom();
        Check(EndsAtCards() && !grid.HorizontalScroll.Visible, "Narrow-window reflow keeps the bottom at the last row without horizontal overflow.");
        grid.AutoScrollPosition = Point.Empty; grid.ScrollControlIntoView(page.Cards.Last()); Application.DoEvents();
        Check(page.Cards.Last().Bottom <= grid.ClientSize.Height && page.Cards.Last().Top >= 0, "Keyboard-style scrolling can reveal the entire final card.");
        host.ClientSize = new(1180, 1050); Application.DoEvents();
        Check(!grid.VerticalScroll.Visible && grid.AutoScrollPosition.Y == 0, "Enlarging the viewport clamps scrolling to zero when all cards fit.");
        host.Close();

        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "system-page-test-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Language = "zh-CN"; controller.Saved.HiddenPaths.Add(@"C:\TrayPilot.UI.Tests\application.exe"); controller.Saved.HiddenSystemIcons = 16;
        using var main = new MainForm(controller, initialize: false); main.Show();
        int topLevel = Application.OpenForms.Cast<Form>().Count(form => form.TopLevel);
        DashboardCall(main, "ShowSystemIcons"); var embedded = DashboardField<SystemIconsPage>(main, "dashboardSystemPage");
        Check(embedded.Parent == DashboardField<Panel>(main, "dashboardPageHost") && embedded.Visible && main.OwnedForms.Length == 0,
            "The system controls page opens inside the main-window content host.");
        Check(Application.OpenForms.Cast<Form>().Count(form => form.TopLevel) == topLevel && main.systemIconSessionForDiagnostics == null,
            "Isolated UI testing creates no additional window and starts no native taskbar session.");
        embedded.Choose(1024); DashboardCall(main, "ShowIconsPage"); DashboardCall(main, "ShowSystemIcons");
        Check(ReferenceEquals(embedded, DashboardField<SystemIconsPage>(main, "dashboardSystemPage")), "Navigation retains the system-controls page instance.");
        Check(DashboardField<ThemeButton>(main, "dashboardSystemNav").Selected && !DashboardField<ThemeButton>(main, "dashboardIconNav").Selected,
            "Sidebar selection identifies the active system-controls page.");
        Check(controller.Saved.HiddenPaths.Count == 1 && controller.Saved.HiddenSystemIcons == 16, "Read-only page navigation preserves application rules and system settings.");
        main.Close(); File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
