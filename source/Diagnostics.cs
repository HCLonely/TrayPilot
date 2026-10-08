using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TrayPilot;
internal static partial class Diagnostics
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected a diagnostic command and output path.");
            bool dark = args[0].Contains("-dark"); string command = args[0].Replace("-dark", "");
            return command switch
            {
                "--web-startup-test" => TestWebStartup(args[1]),
                "--web-startup-live-test" => TestWebStartup(args[1], live: true),
                "--web-dashboard-preview" => PreviewWebDashboard(args[1], dark),
                "--web-rules-preview" => PreviewWebDashboard(args[1], dark, rulesPreview: true),
                "--web-system-preview" => PreviewWebDashboard(args[1], dark, systemPreview: true),
                "--web-preferences-preview" => PreviewWebDashboard(args[1], dark, preferencesPreview: true),
                "--web-feedback-preview" => PreviewWebDashboard(args[1], dark, feedbackPreview: true),
                "--web-dashboard-live-test" => TestWebDashboardLive(args[1]),
                "--web-rules-live-test" => TestWebDashboardLive(args[1], rulesTest: true),
                "--web-system-bridge-test" => TestWebSystemBridge(args[1]),
                "--web-preferences-bridge-test" => TestWebPreferencesBridge(args[1]),
                "--web-feedback-bridge-test" => TestWebFeedbackBridge(args[1]),
                "--test-host" => Host(args[1]),
                "--self-test" => Test(args[1]),
                "--resilience-test" => TestResilience(args[1]),
                "--symbol-cache-test" => TestSymbolCache(args[1]),
                "--startup-test" => TestStartup(args[1]),
                "--package-test" => TestPackage(args[1]),
                "--system-icons-probe" => ProbeSystemIcons(args[1], false),
                "--system-icons-probe-legacy" => ProbeSystemIcons(args[1], true),
                "--system-icons-live-test" => TestLiveSystemIcons(args[1]),
                "--verify-special-icons" => TestSpecialIcons(args[1]),
                "--verify-task-manager" => TestTaskManager(args[1]),
                "--icon-selection-test" => TestIconSelection(args[1]),
                "--scan" => WriteScan(args[1]),
                "--write-icon" => WriteIcon(args[1]),
                _ => throw new ArgumentException("Unknown diagnostic command: " + args[0])
            };
        }
        catch (Exception ex) { if (args.Length > 1) File.WriteAllText(args[1] + ".error.txt", ex.ToString()); return 1; }
    }
    static int ProbeSystemIcons(string path, bool legacy)
    {
        using var session = new SystemIconSession(legacy: legacy);
        for (int i = 0; i < 80 && session.Ticks == 0 && session.Error == 0; i++) Thread.Sleep(250);
        File.WriteAllText(path, $"PID={session.Pid} Found={session.Found} Hidden={session.Hidden} Ticks={session.Ticks} Error=0x{session.Error:X8}\n{session.DebugInfo()}");
        return session.Ticks > 0 && session.Error == 0 ? 0 : 1;
    }
    static int WriteScan(string path) { File.WriteAllText(path, JsonSerializer.Serialize(Scanner.Scan(), new JsonSerializerOptions { WriteIndented = true })); return 0; }
    static int WriteIcon(string path) { AppIcon.Save(path); return 0; }
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
            controller.RemoveIconRule(controller.Saved.HiddenIcons.Single(r => r.Matches(own[0])));
            controller.SetManualVisibility(own[0], false); controller.ChangeManyAsync(new[] { own[0] }, false, asynchronous: false).GetAwaiter().GetResult();
            Check(controller.Saved.HiddenIcons.Count == 1 && Native.State(own[0]) == 0 && Native.State(own[1]) == 1,
                "Removing an individual rule preserves its sibling rule.");
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
        finally { new StartupRegistration(key).SetEnabled(false); Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    static void TestStartupRegistration(StartupRegistration startup, string keyPath, Action<bool, string> check)
    {
        check(!startup.Enabled, "Startup is disabled when no registration exists.");
        var empty = startup.Capture();
        startup.SetEnabled(true);
        check(startup.Enabled && startup.Command == "\"" + Environment.ProcessPath + "\" --startup", "Startup registers a quoted executable with the background startup argument.");
        var enabled = startup.Capture();
        var xml = System.Xml.Linq.XElement.Parse(enabled.TaskXml!);
        System.Xml.Linq.XNamespace ns = xml.Name.Namespace;
        check((xml.Descendants(ns + "Delay").SingleOrDefault()?.Value ?? "PT0S") == "PT0S"
            && xml.Descendants(ns + "Priority").Single().Value == "3"
            && xml.Descendants(ns + "LogonType").Single().Value == "InteractiveToken"
            && (xml.Descendants(ns + "RunLevel").SingleOrDefault()?.Value ?? "LeastPrivilege") == "LeastPrivilege"
            && xml.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S"
            && xml.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value == "false"
            && enabled.Run == null, "Login task has no delay, above-normal priority, no elevation, no time limit or battery restriction, and no duplicate Run entry.");
        xml.Element(ns + "Settings")!.SetElementValue(ns + "Enabled", "false");
        startup.Restore(enabled with { TaskXml = xml.ToString() });
        check(!startup.Enabled, "Disabling the scheduled task is reflected in the UI state.");
        startup.SetEnabled(true);
        check(startup.Enabled, "Explicit enable re-enables the scheduled task.");
        startup.Restore(empty);
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
        {
            key.SetValue("Unrelated", "keep");
            key.SetValue("TrayPilot", startup.Command);
        }
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath + @"\Approval"))
            key.SetValue("TrayPilot", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        check(!startup.Enabled, "A Task Manager disabled startup entry is displayed as disabled.");
        startup.UpgradeLegacy();
        check(startup.Capture().TaskXml == null, "Migration respects a disabled legacy startup entry.");
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
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("TrayPilot", startup.Command);
        startup.UpgradeLegacy();
        check(startup.Enabled && startup.Capture().Run == null && startup.Capture().TaskXml != null,
            "Enabled legacy registrations migrate to the login task without duplicate startup entries.");
        startup.Restore(empty);
        var controller = new Controller(Path.Combine(Path.GetTempPath(), "TrayPilot-startup-" + Guid.NewGuid().ToString("N")));
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
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
            TestStartupRecovery(own, Check, folder);
            TestExitPreferences(own, Check, folder);
            TestSessionEnding(new Controller(Path.Combine(folder, "session-state")), own, Check, Path.Combine(folder, "session-state", "settings.json.tmp"));
            TestBatchRecovery(controller, own, Check, Path.Combine(folder, "state", "settings.json.tmp"));
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
            startup.SetEnabled(false);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupKey, throwOnMissingSubKey: false);
            File.WriteAllLines(report, log);
        }
    }
    static void TestEndTask(Controller controller, List<TrayEntry> own, Action<bool, string> check)
    {
        bool rejected = false;
        try { ProgramActions.EndAsync(own[0] with { Started = own[0].Started + 1 }).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && Scanner.SameOwner(own[0]), "End-task rejects stale process identity.");
        ProgramActions.EndAsync(own[0]).GetAwaiter().GetResult();
        check(!Scanner.SameOwner(own[0]), "End-task terminates only the test-owned process.");
    }
}
