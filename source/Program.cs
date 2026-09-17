namespace TrayPilot;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        L.Set("zh-CN");
        if (args.Length > 0) return Diagnostics.Run(args);
        try
        {
            var controller = new Controller(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayPilot"));
            L.Set(controller.Saved.Language);
            using var mutex = new Mutex(true, @"Local\TrayPilot-Manager-v1", out var created);
            if (!created) { MessageBox.Show(L.T("TrayPilot 已经在运行，请从托盘打开现有窗口。"), "TrayPilot"); return 0; }
            controller.RestoreManaged();
            Application.Run(new MainForm(controller));
            return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, L.T("TrayPilot 启动失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
