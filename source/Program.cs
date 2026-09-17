namespace TrayPilot;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        L.Set("zh-CN");
        bool startup = args.Length == 1 && args[0] == "--startup";
        if (args.Length > 0 && !startup) { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); return Diagnostics.Run(args); }
        try
        {
            var controller = new Controller(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayPilot"));
            L.Set(controller.Saved.Language);
            using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrayPilot-Activate-v1");
            using var mutex = new Mutex(true, @"Local\TrayPilot-Manager-v1", out var created);
            if (!created) { if (!startup) activation.Set(); return 0; }
            controller.RestoreManaged();
            using var form = new MainForm(controller, startInTray: startup);
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
