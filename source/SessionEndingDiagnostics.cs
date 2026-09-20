using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static void TestSessionEnding(Controller controller, List<TrayEntry> entries, Action<bool, string> check, string temporaryFile)
    {
        bool previousPreference = controller.Saved.RestoreIconsOnExit;
        controller.Saved.RestoreIconsOnExit = true;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var busyField = typeof(MainForm).GetField("busy", flags)!;
        var closingField = typeof(MainForm).GetField("closing", flags)!;
        var exitField = typeof(MainForm).GetField("exitRequested", flags)!;
        // Only these test windows receive the messages; no real shutdown is requested.
        foreach (nint reason in new nint[] { 0, unchecked((nint)0x80000000L) }) // Shutdown and ENDSESSION_LOGOFF.
        {
            controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
            using var form = new MainForm(controller, initialize: false);
            var window = form.Handle;
            var timer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("timer", flags)!.GetValue(form)!;
            timer.Interval = 60000; timer.Start();
            foreach (bool busy in new[] { false, true })
            {
                busyField.SetValue(form, busy);
                check(Native.SendMessageW(window, 0x0011, 0, reason) == 1,
                    $"Session-end query accepts shutdown/logoff without a veto (reason={reason}, busy={busy}).");
                check(!form.IsDisposed && form.IsHandleCreated && timer.Enabled &&
                    !(bool)closingField.GetValue(form)! && !(bool)exitField.GetValue(form)! && entries.All(x => Native.State(x) == 1),
                    "Session-end query preserves the window, scheduler and hidden icons until confirmation.");
                Native.SendMessageW(window, 0x0016, 0, reason);
                check(!form.IsDisposed && timer.Enabled && (bool)busyField.GetValue(form)! == busy &&
                    !(bool)closingField.GetValue(form)! && !(bool)exitField.GetValue(form)!,
                    "Canceled shutdown leaves normal operation intact, including an in-flight operation.");
            }
            busyField.SetValue(form, false);
            check(Native.SendMessageW(window, 0x0011, 0, reason) == 1, "A later session-end query is still accepted.");
            Native.SendMessageW(window, 0x0016, 1, reason);
            check(entries.All(x => Native.State(x) == 0) && controller.Saved.Recovery.Count == 0 && !timer.Enabled,
                "Confirmed shutdown/logoff restores icons and stops refresh without waiting for UI continuations.");
        }
        controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
        using (var form = new MainForm(controller, initialize: false))
        {
            var window = form.Handle;
            Directory.CreateDirectory(temporaryFile);
            try
            {
                check(Native.SendMessageW(window, 0x0011, 0, 0) == 1, "An unwritable journal does not veto the session-end query.");
                Native.SendMessageW(window, 0x0016, 1, 0);
                check(entries.All(x => Native.State(x) == 0) && controller.Saved.Recovery.Count == entries.Count &&
                    new Controller(Path.GetDirectoryName(temporaryFile)!).Saved.Recovery.Count == entries.Count,
                    "Shutdown restoration retains memory and disk recovery records if the journal cannot be saved.");
            }
            finally { Directory.Delete(temporaryFile); }
        }
        controller.RestoreManaged();
        controller.Saved.RestoreIconsOnExit = previousPreference;
    }
    static void TestExitPreferences(List<TrayEntry> entries, Action<bool, string> check, string folder)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool restore in new[] { false, true }) foreach (bool useMenu in new[] { false, true })
        {
            string state = Path.Combine(folder, $"exit-{restore}-{useMenu}");
            var controller = new Controller(state);
            check(!controller.Saved.RestoreIconsOnExit, "New settings default to leaving icons unchanged on exit.");
            controller.Saved.RestoreIconsOnExit = restore; controller.Saved.CloseToTray = false; controller.Save();
            check(new Controller(state).Saved.RestoreIconsOnExit == restore, "Restore-on-exit choice persists across reloads.");
            controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
            using var form = new MainForm(controller, initialize: false);
            _ = form.Handle;
            if (!restore) typeof(MainForm).GetField("busy", flags)!.SetValue(form, true);
            if (useMenu) form.RequestExit(); else form.Close();
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!form.IsDisposed && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            check(form.IsDisposed && entries.All(x => Native.State(x) == (restore ? 0 : 1)),
                $"Exit obeys the restore preference (restore={restore}, menu={useMenu}); default exit also bypasses busy work.");
            var loaded = new Controller(state);
            check(loaded.Saved.Recovery.Count == (restore ? 0 : entries.Count), "Non-restoring exit keeps recovery records available.");
            loaded.RestoreManaged();
        }
        string legacyState = Path.Combine(folder, "legacy-exit"); Directory.CreateDirectory(legacyState);
        File.WriteAllText(Path.Combine(legacyState, "settings.json"), "{\"CloseToTray\":true}");
        check(!new Controller(legacyState).Saved.RestoreIconsOnExit, "Existing settings without the new option default to no restoration.");
        foreach (nint reason in new nint[] { 0, unchecked((nint)0x80000000L) })
        {
            string state = Path.Combine(folder, "exit-session-" + reason);
            var controller = new Controller(state);
            controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
            using var form = new MainForm(controller, initialize: false);
            var window = form.Handle;
            check(Native.SendMessageW(window, 0x0011, 0, reason) == 1, "Default exit accepts shutdown/logoff queries.");
            Native.SendMessageW(window, 0x0016, 1, reason);
            check(entries.All(x => Native.State(x) == 1), "Default shutdown/logoff does not restore managed icons.");
            new Controller(state).RestoreManaged();
        }
    }
}
