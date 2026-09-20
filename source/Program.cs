namespace TrayPilot;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        bool startup = args.Length == 1 && args[0] == "--startup";
        if (startup)
        {
            try { using var process = System.Diagnostics.Process.GetCurrentProcess(); process.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal; }
            catch (System.ComponentModel.Win32Exception) { } // A policy may prohibit a priority boost.
        }
        ApplicationConfiguration.Initialize();
        L.Set(L.SystemLanguage);
        if (args.Length > 0 && !startup) { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); return Diagnostics.Run(args); }
        try
        {
            using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrayPilot-Activate-v1");
            using var mutex = new Mutex(true, @"Local\TrayPilot-Manager-v1", out var created);
            if (!created) { if (!startup) activation.Set(); return 0; }
            var controller = new Controller(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayPilot"));
            L.Set(controller.Saved.Language);
            using var form = new MainForm(controller, startInTray: startup, restoreOnStartup: true);
            form.Shown += (_, _) => _ = Task.Run(() =>
            {
                try { new StartupRegistration().UpgradeLegacy(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); } // Keep an existing Run entry if migration fails.
            });
            _ = form.Handle;
            var listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
            {
                try { form.BeginInvoke(() => form.ActivateMainWindow()); }
                catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, false);
            try { Application.Run(form); } finally { listener.Unregister(null); }
            return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, L.T("startupFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
