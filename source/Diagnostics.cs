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
            bool markedPreview = args[0].Contains("-marked");
            if (markedPreview) args[0] = args[0].Replace("-marked", "");
            bool hoverPreview = args[0].Contains("-hover");
            if (hoverPreview) args[0] = args[0].Replace("-hover", "");
            bool darkPreview = args[0].Contains("-dark");
            if (darkPreview) args[0] = args[0].Replace("-dark", "");
            if (args[0] == "--write-icon" && args.Length == 2) { AppIcon.Save(args[1]); return 0; }
            if (args[0] == "--scan" && args.Length == 2)
            { File.WriteAllText(args[1], JsonSerializer.Serialize(Scanner.Scan(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
            if (args[0] == "--test-host" && args.Length == 2) return Host(args[1]);
            if (args[0] == "--self-test" && args.Length == 2) return Test(args[1]);
            if (args[0] == "--startup-test" && args.Length == 2) return TestStartup(args[1]);
            if (args[0] == "--verify-special-icons" && args.Length == 2) return TestSpecialIcons(args[1]);
            if (args[0] == "--verify-task-manager" && args.Length == 2) return TestTaskManager(args[1]);
            if (args[0] == "--icon-selection-test" && args.Length == 2) return TestIconSelection(args[1]);
            if (args.Length == 2 && args[0] is "--preview-settings-en" or "--preview-about-en" or "--preview-properties-en" or "--preview-rules-en")
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "preview-state");
                var previewController = new Controller(folder); previewController.Saved.Language = "en-US";
                if (args[0] == "--preview-rules-en") previewController.Saved.HiddenPaths.AddRange(Scanner.Scan().Select(x => x.Path).Distinct().Take(4));
                if (darkPreview) previewController.Saved.Theme = "dark";
                if (markedPreview) { previewController.Saved.HiddenPaths = Scanner.Scan().Select(x => x.Path).Distinct().Take(3).ToList(); previewController.Saved.RulesPaused = true; }
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
                    else typeof(MainForm).GetMethod(args[0] == "--preview-settings-en" ? "ShowSettings" : args[0] == "--preview-rules-en" ? "EditRules" : "ShowAbout", flags)!.Invoke(form, null);
                    form.RequestExit();
                };
                Application.Run(form); return 0;
            }
            if (args[0] is "--preview" or "--preview-grid" or "--preview-en" or "--preview-tray-en" or "--preview-small-en" && args.Length == 2)
            {
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "preview-state");
                var previewController = new Controller(folder);
                if (args[0].EndsWith("-en")) previewController.Saved.Language = "en-US";
                if (darkPreview) previewController.Saved.Theme = "dark";
                if (markedPreview) { previewController.Saved.HiddenPaths = Scanner.Scan().Select(x => x.Path).Distinct().Take(3).ToList(); previewController.Saved.RulesPaused = true; }
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
                    if (hoverPreview)
                    {
                        var previewList = (ListView)typeof(MainForm).GetField("list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
                        if (previewList.Items.Count > 0)
                        {
                            var rect = previewList.Items[0].Bounds;
                            typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(previewList,
                                new object[] { new MouseEventArgs(MouseButtons.None, 0, rect.Left + 10, rect.Top + 10, 0) });
                        }
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
    static int TestIconSelection(string report)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "integration-selection-" + Guid.NewGuid().ToString("N"));
        var controller = new Controller(Path.Combine(folder, "state"));
        using var host = StartHost(folder);
        var log = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); log.Add("PASS: " + message); }
        List<TrayEntry> Discover(Process process)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            List<TrayEntry> icons;
            do
            {
                icons = Scanner.Scan().Where(x => x.Pid == process.Id).OrderBy(x => x.Id).ToList();
                if (icons.Count == 2) return icons;
                Thread.Sleep(100);
            } while (!process.HasExited && DateTime.UtcNow < deadline);
            return icons;
        }
        try
        {
            var own = Discover(host);
            Check(own.Count == 2, "Discover two independently addressable icons from one application.");
            using var form = new MainForm(controller, initialize: false);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainForm).GetField("entries", flags)!.SetValue(form, own);
            var change = typeof(MainForm).GetMethod("ChangeEntries", flags)!;
            change.Invoke(form, new object[] { new[] { own[0] }, true });
            Check(Native.State(own[0]) == 1 && Native.State(own[1]) == 0, "Hiding one icon leaves its sibling visible.");
            change.Invoke(form, new object[] { new[] { own[0] }, false });
            foreach (var target in own)
            {
                controller.AddIconRule(target); controller.AddIconRule(target);
                Check(controller.Saved.HiddenIcons.Count == 1, "Duplicate icon rules are ignored.");
                var loaded = new Controller(Path.Combine(folder, "state"));
                loaded.Apply(own);
                Check(own.All(x => Native.State(x) == (x.Key == target.Key ? 1 : 0)), "Persisted UID/GUID rule hides only its matching icon.");
                loaded.RestoreManaged();
                controller.RemoveIconRule(controller.Saved.HiddenIcons.Single());
            }
            controller.AddRule(own[0].Path); controller.Apply(own);
            change.Invoke(form, new object[] { new[] { own[0] }, false }); controller.Apply(own);
            Check(Native.State(own[0]) == 0 && Native.State(own[1]) == 1, "Restoring one icon under an application rule preserves sibling hiding after refresh.");
            controller.ResetManualVisibility(); controller.Apply(own);
            Check(own.All(x => Native.State(x) == 1), "Resuming rules clears individual temporary overrides.");
            controller.RemoveRule(own[0].Path); controller.RestoreManaged();
            foreach (var icon in own) controller.AddIconRule(icon);
            controller.Apply(own);
            using (var dialog = form.CreateRulesDialog())
            {
                dialog.Show(); Application.DoEvents();
                var rules = dialog.Controls.OfType<ListView>().Single();
                Check(rules.Items.Count == 2 && rules.Items.Cast<ListViewItem>().All(x => x.Tag is IconRule), "Rule manager lists individual icon rules separately.");
                var selected = rules.Items.Cast<ListViewItem>().Single(x => ((IconRule)x.Tag!).Matches(own[0]));
                selected.Selected = true; rules.Focus(); Application.DoEvents();
                dialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(x => x.Text == L.T("removeRulesAndRestoreIcons")).PerformClick();
                Check(controller.Saved.HiddenIcons.Count == 1 && Native.State(own[0]) == 0 && Native.State(own[1]) == 1,
                    "Removing an individual rule restores only its icon and retains its sibling rule.");
                dialog.Close();
            }
            controller.RemoveIconRule(controller.Saved.HiddenIcons.Single()); controller.RestoreManaged();
            controller.AddIconRule(own[0]);
            File.WriteAllText(Path.Combine(folder, "stop"), "");
            Check(host.WaitForExit(5000), "First test owner exits.");
            File.Delete(Path.Combine(folder, "stop")); File.Delete(Path.Combine(folder, "ready"));
            using var restarted = StartHost(folder);
            try
            {
                var next = Discover(restarted);
                Check(next.Count == 2, "Rediscover icons after owner restart.");
                controller.Apply(next);
                Check(next.All(x => Native.State(x) == (x.Id == own[0].Id ? 1 : 0)), "Icon rule survives a new process and window without hiding its sibling.");
                controller.RestoreManaged();
            }
            finally { File.WriteAllText(Path.Combine(folder, "stop"), ""); restarted.WaitForExit(5000); }
            return 0;
        }
        finally
        {
            controller.RestoreManaged(); File.WriteAllText(Path.Combine(folder, "stop"), ""); host.WaitForExit(5000);
            File.WriteAllLines(report, log);
        }
    }

    static int TestTaskManager(string report)
    {
        var log = new List<string>();
        var entries = Scanner.Scan().Where(x => Path.GetFileName(x.Path).Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0) throw new Exception("Task Manager must be running with a tray icon.");
        if (!entries.Any(x => x.Id == 0)) throw new Exception("Task Manager's live UID 0 icon was not discovered.");
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,
            "integration-taskmgr-" + Guid.NewGuid().ToString("N")));
        try
        {
            foreach (var entry in entries)
            {
                if (!Scanner.SameOwner(entry) || Scanner.SameOwner(entry with { Started = entry.Started + 1 }))
                    throw new Exception("Task Manager owner validation failed.");
                log.Add($"PASS: Discover and verify Task Manager icon UID {entry.Id}, initial state {entry.State}.");
            }
            controller.AddRule(entries[0].Path);
            controller.Apply(entries);
            for (int i = 0; i < 25; i++)
            {
                Thread.Sleep(200);
                if (entries.Any(x => Native.State(x) != 1)) throw new Exception("Task Manager icon did not remain hidden.");
            }
            log.Add("PASS: The path rule hides all Task Manager icons through live CPU updates for five seconds.");
            var rescanned = Scanner.Scan().Where(x => Controller.SamePath(x.Path, entries[0].Path)).ToList();
            if (entries.Any(x => !rescanned.Any(y => y.Key == x.Key && y.State == 1)))
                throw new Exception("Hidden Task Manager icons were lost during rescan.");
            log.Add("PASS: Hidden icons remain discoverable for restoration.");
        }
        finally { controller.RestoreManaged(); File.WriteAllLines(report, log); }
        if (entries.Any(x => Native.State(x) != x.State)) throw new Exception("Original visibility was not restored.");
        log.Add("PASS: Restore original visibility without changing existing hidden icons.");
        File.WriteAllLines(report, log);
        return 0;
    }

    static int TestSpecialIcons(string report)
    {
        var log = new List<string>();
        var entries = Scanner.Scan().Where(x => x.Guid == Scanner.HardwareRemovalGuid ||
            Path.GetFileName(x.Path).Equals("NVDisplay.Container.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        bool Wait(TrayEntry entry, int state)
        {
            for (int i = 0; i < 50; i++) { if (Native.State(entry) == state) return true; Thread.Sleep(40); }
            return false;
        }
        foreach (var entry in entries)
        {
            int before = Native.State(entry);
            try
            {
                if (!Scanner.SameOwner(entry)) throw new Exception("Owner verification failed: " + entry.Name);
                log.Add("PASS: Discover and verify owner: " + entry.Name);
                if (Scanner.SameOwner(entry with { Started = entry.Started + 1 })) throw new Exception("Stale identity accepted.");
                log.Add("PASS: Reject stale process identity: " + entry.Name);
                if (!Native.SetHidden(entry, before == 0) || !Wait(entry, before == 0 ? 1 : 0))
                    throw new Exception("Visibility change failed: " + entry.Name);
                log.Add("PASS: Toggle visibility: " + entry.Name);
            }
            finally
            {
                Native.SetHidden(entry, before == 1);
                if (!Wait(entry, before)) throw new Exception("Restoration failed: " + entry.Name);
            }
            log.Add("PASS: Restore original visibility: " + entry.Name);
        }
        if (!entries.Any(x => x.Guid == Scanner.HardwareRemovalGuid) || !entries.Any(x => !Scanner.IsShellEntry(x)))
            throw new Exception("Both requested icons must be present for this machine-specific verification.");
        File.WriteAllLines(report, log); return 0;
    }

    static int TestStartup(string report)
    {
        string key = @"Software\TrayPilot\Tests\startup-" + Guid.NewGuid().ToString("N");
        var log = new List<string>();
        try
        {
            TestStartupRegistration(new StartupRegistration(key), key, (ok, text) =>
            {
                if (!ok) throw new Exception("FAIL: " + text);
                log.Add("PASS: " + text);
            });
            File.WriteAllLines(report, log); return 0;
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    static void TestStartupRegistration(StartupRegistration startup, string keyPath, Action<bool, string> check)
    {
        check(!startup.Enabled, "Startup is disabled when no registration exists.");
        var empty = startup.Capture();
        startup.SetEnabled(true);
        check(startup.Enabled && startup.Command == "\"" + Environment.ProcessPath + "\" --startup", "Startup registers a quoted executable with the background startup argument.");
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
            key.SetValue("Unrelated", "keep");
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath + @"\Approval"))
            key.SetValue("TrayPilot", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        check(!startup.Enabled, "A Task Manager disabled startup entry is displayed as disabled.");
        var disabled = startup.Capture();
        startup.SetEnabled(true);
        check(startup.Enabled, "Explicitly enabling startup clears the previous disabled approval.");
        startup.Restore(disabled);
        check(!startup.Enabled && startup.Capture().Approval?.Data is byte[] bytes && bytes[0] == 3,
            "Startup rollback preserves the previous Windows approval state.");
        startup.SetEnabled(false);
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
            check(!startup.Enabled && key!.GetValue("TrayPilot") == null && (string?)key.GetValue("Unrelated") == "keep",
                "Disabling removes only TrayPilot's registration and preserves unrelated entries.");
        startup.Restore(empty);
        var controller = new Controller(Path.Combine(Path.GetTempPath(), "TrayPilot-startup-" + Guid.NewGuid().ToString("N")));
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using (var settingsForm = new MainForm(controller, initialize: false, startup: startup))
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            Exception? failure = null;
            bool rejectSave = false;
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(x => x != settingsForm);
                if (dialog == null) return;
                timer.Stop();
                try
                {
                    ((CheckBox)dialog.Controls.Find("startup", true).Single()).Checked = true;
                    if (rejectSave)
                    {
                        var panel = dialog.Controls.OfType<TableLayoutPanel>().Single();
                        panel.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(x => x.Text == L.T("save")).PerformClick();
                        check(dialog.DialogResult != DialogResult.OK && !startup.Enabled,
                            "A settings-file save failure rolls back the startup registration.");
                    }
                    dialog.CancelButton!.PerformClick();
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(settingsForm, null);
            if (failure != null) throw failure;
            check(!startup.Enabled, "Canceling Settings does not register the checked startup option.");
            var stateFile = (string)typeof(Controller).GetField("file", flags)!.GetValue(controller)!;
            Directory.CreateDirectory(stateFile + ".tmp"); // Deliberately make the atomic settings write fail.
            rejectSave = true; timer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(settingsForm, null);
            if (failure != null) throw failure;
        }
        foreach (bool iconVisible in new[] { true, false })
        {
            controller.Saved.ShowTrayIcon = iconVisible;
            using var form = new MainForm(controller, initialize: false, startup: startup, startInTray: true);
            typeof(MainForm).GetMethod("InitializeTray", flags)!.Invoke(form, null);
            form.Show(); Application.DoEvents();
            check(form.Visible != iconVisible, "Startup runs in the tray, or opens the window when its tray icon is disabled.");
            form.ActivateMainWindow(); check(form.Visible, "A background startup window can be opened normally.");
        }
    }

    static int Test(string report)
    {
        var log = new List<string>();
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var controller = new Controller(Path.Combine(folder, "state"));
        var startupKey = @"Software\TrayPilot\Tests\" + Path.GetFileName(folder);
        var startup = new StartupRegistration(startupKey);
        Process? host = null;
        void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL: " + text); log.Add("PASS: " + text); File.WriteAllLines(report, log); }
        try
        {
            TestStartupRegistration(startup, startupKey, Check);
            Check(Controller.SamePath(Native.SystemProcessPath((uint)Environment.ProcessId), Environment.ProcessPath!), "System process path fallback agrees with the current executable.");
            Check(Native.SystemProcessStarted((uint)Environment.ProcessId) == Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                "System process creation time fallback preserves exact identity.");
            Check(Native.SystemProcessPath(uint.MaxValue) == "" && Native.SystemProcessStarted(uint.MaxValue) == 0, "Process fallback rejects nonexistent PIDs.");
            host = StartHost(folder); Thread.Sleep(500);
            var own = Scanner.Scan().Where(x => x.Pid == host.Id).ToList();
            Check(own.Count == 2, "Automatically discover both UID and GUID icons belonging to a separate process (message-only window).");
            Check(own.All(x => x.State == 0), "Both test icons initially displayed.");
            TestInteraction(controller, own, Check, folder);
            TestShellFeatures(controller, own, Check, startup);
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
            log.Add("OS: " + Environment.OSVersion.Version); log.Add($"TrayPilot {UpdateChecker.CurrentVersionText} integration tests complete.");
            return 0;
        }
        catch (Exception ex) { log.Add(ex.ToString()); return 1; }
        finally
        {
            try { new Controller(Path.Combine(folder, "state")).RestoreManaged(); } catch (Exception e) { log.Add("Cleanup: " + e.Message); }
            if (host != null) { File.WriteAllText(Path.Combine(folder, "stop"), ""); host.WaitForExit(5000); host.Dispose(); }
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupKey, throwOnMissingSubKey: false);
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
                check(menu.Visible && menu.Items.Count == 5 && menu.Items[0].Text == "属性" && menu.Items[3].Text == "结束任务",
                    "Right-click offers individual and application-wide controls.");
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
        check(Native.State(own[0]) == 1 && Native.State(own[1]) == 0 && !controller.HasRule(own[0]), "Double-click hides only the clicked icon without adding a rule.");
        var hidden = list.Items.Cast<ListViewItem>().First(x => ((TrayEntry)x.Tag!).Key == own[0].Key);
        check(hidden.ForeColor == Color.FromArgb(140, 145, 155) && hidden.ToolTipText.Contains("已隐藏"), "Hidden row and hover details reflect hidden state.");
        foreach (bool grid in new[] { false, true })
        {
            layoutMode.Checked = grid; Application.DoEvents();
            var hovered = list.Items[0]; hovered.EnsureVisible(); Application.DoEvents(); var bounds = hovered.Bounds;
            typeof(Control).GetMethod("OnMouseMove", flags)!.Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, bounds.Left + 10, bounds.Top + 10, 0) });
            check(((TrayListView)list).HoveredItem == hovered, "Mouse hover tracks the correct item in list and grid layouts.");
            using var bitmap = new Bitmap(list.Width, list.Height); list.DrawToBitmap(bitmap, list.ClientRectangle);
            int blue = 0;
            for (int y = Math.Max(0, bounds.Top); y < Math.Min(bitmap.Height, bounds.Bottom); y++)
                for (int x = Math.Max(0, bounds.Left); x < Math.Min(bitmap.Width, bounds.Right); x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == UiTheme.Highlight.ToArgb()) blue++;
            check(blue > 100, "Hovered items render a blue background in both layouts.");
            if (!grid)
            {
                var region = new Rectangle(list.Columns[0].Width + 3, bounds.Top, Math.Min(300, list.Width - list.Columns[0].Width - 3), bounds.Height);
                int Before() { int count = 0; for (int y = region.Top; y < Math.Min(bitmap.Height, region.Bottom); y++) for (int x = region.Left; x < region.Right; x++) if (bitmap.GetPixel(x, y).ToArgb() == Color.White.ToArgb()) count++; return count; }
                int textPixels = Before();
                using (var graphics = Graphics.FromImage(bitmap))
                    typeof(TrayListView).GetMethod("OnDrawItem", flags)!.Invoke(list, new object[] { new DrawListViewItemEventArgs(graphics, hovered, bounds, hovered.Index, (ListViewItemStates)0) });
                check(textPixels > 0 && Before() == textPixels, "Partial row repaint preserves text in other columns.");
            }
            bool selectedBefore = hovered.Selected; hovered.Selected = true;
            var next = list.Items.Cast<ListViewItem>().First(x => x != hovered); next.EnsureVisible(); Application.DoEvents();
            var nextBounds = next.Bounds;
            var invalidated = new List<Rectangle>();
            InvalidateEventHandler trackInvalidation = (_, e) => invalidated.Add(e.InvalidRect);
            list.Invalidated += trackInvalidation;
            typeof(Control).GetMethod("OnMouseMove", flags)!.Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, nextBounds.Left + 10, nextBounds.Top + 10, 0) });
            list.Invalidated -= trackInvalidation;
            check(invalidated.Count == 2 && invalidated.All(x => x.Height < list.ClientSize.Height && (!grid || x.Width < list.ClientSize.Width)),
                "Hover changes invalidate only the previous and next item, not the viewport.");
            list.Update(); // Exercise buffered WM_PAINT, as well as WM_PRINT below.
            using (var moved = new Bitmap(list.Width, list.Height))
            {
                list.DrawToBitmap(moved, list.ClientRectangle);
                var oldBounds = hovered.Bounds;
                check(((TrayListView)list).HoveredItem == next && moved.GetPixel(Math.Clamp(oldBounds.Left + 6, 0, moved.Width - 1), Math.Clamp(oldBounds.Top + 4, 0, moved.Height - 1)).ToArgb() != UiTheme.Highlight.ToArgb(),
                    "Moving to another item removes the previous blue hover even if it remains selected.");
            }
            hovered.Selected = selectedBefore;
            typeof(Control).GetMethod("OnMouseLeave", flags)!.Invoke(list, new object[] { EventArgs.Empty });
            check(((TrayListView)list).HoveredItem == null, "Hover highlight clears when the mouse leaves.");
        }
        layoutMode.Checked = false;
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
        check(own.All(x => Native.State(x) == 0) && !controller.HasRule(own[0].Path), "Double-click restores icons without changing rules.");
        using var fallback = TrayImages.Create(new TrayEntry { Path = "missing.exe", IconSnapshot = new byte[] { 1, 2, 3 } }, 24);
        check(Alpha(fallback) > 0, "Invalid snapshot and missing executable fall back to a visible application icon.");
        check(!refreshTimer.Enabled, "Manual actions do not restart disabled automatic refresh.");
        layoutMode.Checked = true;
        ClickIcon();
        check(Native.State(own[0]) == 1 && Native.State(own[1]) == 0, "Grid double-click hides only the clicked icon.");
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
            check(Native.State(own[0]) == (hide ? 1 : 0) && Native.State(own[1]) == 0, "Context-menu command changes only the clicked icon.");
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
        typeof(MainForm).GetMethod("ShowItemMenu", flags)!.Invoke(form, new object[] { own[0], new Point(30, 30) });
        check(menu.Items[2].Text == L.T("addIconRule") && menu.Items[2].Enabled, "Context menu offers an individual icon rule.");
        menu.Items[2].PerformClick();
        var ruleDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < ruleDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(controller.HasIconRule(own[0]) && !controller.HasRule(own[0].Path) && Native.State(own[0]) == 1 && Native.State(own[1]) == 0,
            "Adding an icon rule hides only that icon.");
        controller.RemoveIconRule(controller.Saved.HiddenIcons.Single());
        typeof(MainForm).GetMethod("ShowItemMenu", flags)!.Invoke(form, new object[] { own[0], new Point(30, 30) });
        ((ToolStripMenuItem)menu.Items[4]).DropDownItems[2].PerformClick();
        ruleDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < ruleDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(controller.HasRule(own[0].Path) && own.All(x => Native.State(x) == 1), "Adding a matching rule immediately applies it when rules are active.");
        controller.AddRule(own[0].Path.ToUpperInvariant().Replace('\\', '/'));
        check(controller.Saved.HiddenPaths.Count(x => Controller.SamePath(x, own[0].Path)) == 1, "Equivalent case and slash variants do not create duplicate rules.");
        check(list.Items.Cast<TrayListItem>().All(x => x.MatchesRule && x.SubItems[2].Text == L.T("ruleMatchIndicator")), "Matching items carry a persistent rule marker.");
        typeof(MainForm).GetMethod("ChangePaths", flags)!.Invoke(form, new object[] { new[] { own[0].Path }, false });
        controller.Apply(own);
        check(controller.HasRule(own[0].Path) && controller.IsTemporarilyShown(own[0].Path) && own.All(x => Native.State(x) == 0), "Manual restore retains the rule and automatic refresh respects the temporary override.");
        var commands = (FlowLayoutPanel)typeof(MainForm).GetField("actions", flags)!.GetValue(form)!;
        commands.Controls.OfType<Button>().Single(x => x.Text == L.T("hideMatchingIcons")).PerformClick();
        ruleDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < ruleDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(!controller.IsTemporarilyShown(own[0].Path) && own.All(x => Native.State(x) == 1), "Hide matching button clears temporary overrides and reapplies saved rules.");
        var savedRules = controller.Saved.HiddenPaths.ToList();
        commands.Controls.OfType<Button>().Single(x => x.Text == L.T("restoreAll")).PerformClick();
        ruleDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < ruleDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        controller.Apply(own);
        check(controller.Saved.HiddenPaths.SequenceEqual(savedRules) && controller.IsTemporarilyShown(own[0].Path) && own.All(x => Native.State(x) == 0),
            "Restore all retains matching rules and restored icons stay visible after automatic refresh.");
        check(list.Items.Cast<TrayListItem>().All(x => x.MatchesRule && x.SubItems[2].Text == L.T("ruleMatchIndicator")),
            "Restore all preserves matching-rule markers in the list.");
        typeof(MainForm).GetMethod("ChangePaths", flags)!.Invoke(form, new object[] { new[] { own[0].Path }, true });
        check(controller.Saved.HiddenPaths.SequenceEqual(savedRules) && own.All(x => Native.State(x) == 1), "Manual hiding retains matching rules.");
        typeof(MainForm).GetMethod("ChangePaths", flags)!.Invoke(form, new object[] { new[] { own[0].Path }, false });
        controller.Apply(own);
        check(controller.Saved.HiddenPaths.SequenceEqual(savedRules) && own.All(x => Native.State(x) == 0), "Individual restoration retains matching rules.");
        controller.RemoveRule(own[0].Path); foreach (var entry in own) controller.Show(entry);
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
        menu.Items[3].PerformClick();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        check(!Scanner.SameOwner(own[0]) && list.Items.Count == 0, "End-task menu removes all icons belonging to the terminated process.");
    }

    static void TestShellFeatures(Controller controller, List<TrayEntry> own, Action<bool, string> check, StartupRegistration startup)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var packs = L.Packs();
        check(packs.ContainsKey("zh-CN") && packs.ContainsKey("en-US") && packs["zh-CN"].Strings.Keys.ToHashSet().SetEquals(packs["en-US"].Strings.Keys),
            "External Simplified Chinese and English packs contain matching translation keys.");
        check(new SavedState().CloseToTray && new SavedState().Language == L.SystemLanguage && !new SavedState().ShowAllHotkeyEnabled,
            "New and migrated settings default to close-to-tray, the supported system language (otherwise English) and no reserved hotkey.");
        check(new SavedState().Theme == "system" && !new SavedState().HideRulesHotkeyEnabled, "Appearance defaults to following the system and new hotkeys start disabled.");
        using var form = new MainForm(controller, initialize: false, visibilityScanner: () => own.Select(x => x with { State = Native.State(x) }).ToList(), startup: startup);
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, own.ToList());
        form.Show(); Application.DoEvents();
        controller.Saved.Theme = "dark"; typeof(MainForm).GetMethod("ApplyTheme", flags)!.Invoke(form, null);
        check(UiTheme.Dark && form.BackColor == UiTheme.Canvas, "Dark mode applies the dark palette to the main window.");
        controller.Saved.Theme = "light"; typeof(MainForm).GetMethod("ApplyTheme", flags)!.Invoke(form, null);
        check(!UiTheme.Dark, "Light mode restores the light palette.");
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
                    var startupToggle = controls.OfType<CheckBox>().Single(x => x.Name == "startup");
                    check(startupToggle.Checked == startup.Enabled, "Settings read the actual startup registration.");
                    startupToggle.Checked = true;
                    var combo = controls.OfType<ComboBox>().Single(x => x.Name == "language");
                    check(((MainForm.LanguageChoice)combo.SelectedItem!).Code == controller.Saved.Language, "Settings select the actual saved language on opening.");
                    combo.SelectedItem = combo.Items.Cast<MainForm.LanguageChoice>().Single(x => x.Code == "en-US");
                    controls.OfType<CheckBox>().Single(x => x.Text == L.T("closeToTray")).Checked = false;
                    controls.OfType<CheckBox>().Single(x => x.Name == "showAllHotkeyEnabled").Checked = false;
                    typeof(Control).GetMethod("OnKeyDown", flags)!.Invoke(controls.OfType<TextBox>().Single(x => x.Name == "showAllHotkey"),
                        new object[] { new KeyEventArgs(Keys.Control | Keys.Alt | Keys.K) });
                    controls.OfType<Button>().Single(x => x.Text == L.T("save")).PerformClick();
                    saved = dialog.DialogResult == DialogResult.OK;
                    if (!saved) dialog.Close();
                }
                catch (Exception ex) { settingsError = ex; dialog.Close(); }
            };
            settingsTimer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            if (settingsError != null) throw settingsError;
            check(saved && controller.Saved.Language == "en-US" && !controller.Saved.CloseToTray
                && controller.Saved.ShowAllHotkey == (int)(Keys.Control | Keys.Alt | Keys.K),
                "Settings dialog saves language, close behavior and a captured key combination.");
            settingsTimer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            if (settingsError != null) throw settingsError;
            check(saved && controller.Saved.Language == "en-US", "Reopening settings retains the selected English language.");
            check(startup.Enabled, "Saving settings enables startup and reopening retains it.");
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
        var trayWindow = (NativeWindow)typeof(NotifyIcon).GetField("_window", flags)!.GetValue(tray)!;
        form.Hide();
        Native.SendMessageW(trayWindow.Handle, 0x0800, 0, 0x0201);
        Native.SendMessageW(trayWindow.Handle, 0x0800, 0, 0x0202);
        check(form.Visible, "The first native left tray click opens the hidden main window.");
        form.Hide();
        check(menu.Items.Count == 0, "Cold-start tray menu has not been built before the first click.");
        Native.SendMessageW(trayWindow.Handle, 0x0800, 0, 0x0205);
        Application.DoEvents();
        check(menu.Visible && menu.Items.Count > 0, "The first native right tray click populates and opens the menu.");
        menu.Items[0].PerformClick(); Application.DoEvents();
        check(form.Visible, "The first tray menu opens the main window on its first action click.");
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
        var startupItem = menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Name == "startup");
        check(startupItem.Checked, "Tray menu reflects startup enabled in Settings.");
        startupItem.PerformClick();
        check(!startup.Enabled && !startupItem.Checked, "Tray startup toggle disables registration immediately.");
        BuildMenu();
        check(!menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Name == "startup").Checked, "Rebuilt tray menu keeps startup state synchronized.");
        check(AppRow().Checked && list.Items.Count == 0, "Tray quick controls include applications excluded by the main search filter.");
        foreach (bool expectedHidden in new[] { true, false })
        {
            AppRow().DropDownItems[0].PerformClick();
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
            menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("nextPage")).PerformClick();
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
        menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("selfTrayMenuItem")).PerformClick();
        var selfDeadline = DateTime.UtcNow.AddSeconds(60);
        while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < selfDeadline) { Application.DoEvents(); Thread.Sleep(1); }
        check(!tray.Visible && !controller.Saved.ShowTrayIcon, "The manager tray icon can be hidden and the preference is saved.");
        form.Close();
        check(!form.Visible && !form.IsDisposed, "Close-to-tray continues running when the manager tray icon is hidden.");
        form.ActivateMainWindow();
        check(form.Visible, "The activation entry point reopens a manager with no tray icon.");
        BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == L.T("selfTrayMenuItem")).PerformClick();
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
        var hotkey = (GlobalHotkey)typeof(MainForm).GetField("showAllHotkey", flags)!.GetValue(form)!;
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
            typeof(MainForm).GetField("busy", flags)!.SetValue(form, true);
            Native.SendMessageW(form.Handle, 0x0312, id, 0);
            check((bool?)typeof(MainForm).GetField("pendingVisibilityPreset", flags)!.GetValue(form) == false, "Bulk hotkeys queue while a refresh is busy.");
            typeof(MainForm).GetMethod("CompleteOperation", flags)!.Invoke(form, null); Application.DoEvents();
            var presetDeadline = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < presetDeadline) { Application.DoEvents(); Thread.Sleep(1); }
            check(!menu.Visible && !form.Visible && controller.Saved.RulesPaused && controller.HasRule(own[0].Path) && own.All(x => Native.State(x) == 0), "Show-all hotkey restores icons while retaining and pausing rules.");
            typeof(MainForm).GetMethod("RefreshAsync", flags)!.Invoke(form, null);
            presetDeadline = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < presetDeadline) { Application.DoEvents(); Thread.Sleep(1); }
            check(own.All(x => Native.State(x) == 0), "Refresh does not rehide icons while saved rules are paused.");
            var hideHotkey = (GlobalHotkey)typeof(MainForm).GetField("hideRulesHotkey", flags)!.GetValue(form)!;
            var hideKeys = new[] { Keys.F2, Keys.F3, Keys.F4, Keys.F5 }.Select(x => Keys.Control | Keys.Alt | Keys.Shift | x).First(x => hideHotkey.TrySet(true, x));
            int hideId = hideHotkey.Matches((nint)0x4A21) ? 0x4A21 : 0x4A22;
            Native.SendMessageW(form.Handle, 0x0312, hideId, 0); Application.DoEvents();
            presetDeadline = DateTime.UtcNow.AddSeconds(60);
            while ((bool)typeof(MainForm).GetField("busy", flags)!.GetValue(form)! && DateTime.UtcNow < presetDeadline) { Application.DoEvents(); Thread.Sleep(1); }
            check(!controller.Saved.RulesPaused && own.All(x => Native.State(x) == 1), "Hide-matching hotkey resumes saved rules and hides matching icons.");
            menu.Close();
            check(hotkey.TrySet(false, chosen) && competitor.TrySet(true, chosen), "Disabling the hotkey releases the registration.");
        }
        typeof(MainForm).GetMethod("OpenMainWindow", flags)!.Invoke(form, null);
        check(form.Visible, "Open main window restores the hidden manager.");
        using (var rulesDialog = form.CreateRulesDialog())
        {
            rulesDialog.Show(form); Application.DoEvents();
            var rulesList = rulesDialog.Controls.OfType<ListView>().Single();
            var rule = rulesList.Items.Cast<ListViewItem>().Single(x => (string)x.Tag! == own[0].Path);
            check(rule.ImageIndex >= 0 && rulesList.SmallImageList!.Images.Count == rulesList.Items.Count && rule.SubItems[1].Text == own[0].Path,
                "Hidden rules show application icons and paths.");
            rule.Selected = true; rulesList.Focus(); Application.DoEvents();
            var removeRule = rulesDialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(x => x.Text == L.T("removeRulesAndRestoreIcons"));
            removeRule.PerformClick();
            check(!controller.HasRule(own[0].Path) && own.All(x => Native.State(x) == 0) && rulesList.Items.Count == 0,
                "Deleting a selected icon rule restores its icons and updates the empty list.");
            rulesDialog.Close();
        }
        controller.AddRule(own[0].Path); controller.Apply(own);
        form.RequestExit();
        check(form.IsDisposed && own.All(x => Native.State(x) == 0), "Explicit Exit bypasses close-to-tray and restores managed icons.");
        using var directExit = new MainForm(controller, initialize: false);
        directExit.Show(); typeof(MainForm).GetMethod("InitializeTray", flags)!.Invoke(directExit, null);
        controller.Saved.CloseToTray = false; controller.Apply(own);
        directExit.Close();
        check(directExit.IsDisposed && own.All(x => Native.State(x) == 0), "With close-to-tray disabled, closing exits and restores icons.");
        controller.Saved.CloseToTray = true; controller.Saved.Language = "en-US"; controller.Saved.Theme = "dark";
        controller.Saved.ShowAllHotkeyEnabled = true; controller.Saved.ShowAllHotkey = (int)chosen;
        controller.Saved.MainHotkeyEnabled = true; controller.Saved.MainHotkey = (int)mainChosen; controller.Saved.ShowTrayIcon = false;
        controller.Save();
        var settingsFile = (string)typeof(Controller).GetField("file", flags)!.GetValue(controller)!;
        var loaded = new Controller(Path.GetDirectoryName(settingsFile)!).Saved;
        check(loaded.Theme == "dark" && loaded.CloseToTray && loaded.Language == "en-US" && loaded.ShowAllHotkeyEnabled && loaded.ShowAllHotkey == (int)chosen && loaded.MainHotkeyEnabled && loaded.MainHotkey == (int)mainChosen && !loaded.ShowTrayIcon,
            "Language, close-to-tray and hotkey settings survive a controller reload.");
        L.Set("missing-language"); check(L.Current == L.FallbackLanguage && L.T("about") == "About", "Unknown language safely falls back to English.");
        controller.Saved.Theme = "system"; controller.Saved.Language = "zh-CN"; controller.Saved.ShowAllHotkeyEnabled = false; controller.Saved.MainHotkeyEnabled = false; controller.Saved.ShowTrayIcon = true; controller.RemoveRule(own[0].Path);
    }
}

