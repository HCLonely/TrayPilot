using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestSettingsRefresh(string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var log = new List<string>();
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            log.Add("PASS: " + message);
        }
        var stateFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "settings-refresh-" + Guid.NewGuid().ToString("N"));
        var controller = new Controller(stateFolder);
        controller.Saved.Language = "en-US";
        controller.Saved.Theme = "light";
        var startup = new StartupRegistration(@"Software\TrayPilotSettingsRefreshTest-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(controller, initialize: false, startup: startup);
        form.Show(); Application.DoEvents();
        var menu = form.MainMenuStrip!;
        var originalItems = menu.Items.Cast<ToolStripItem>().ToArray();
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        var bounds = list.Bounds;
        int sizeChanges = 0;
        list.SizeChanged += (_, _) => sizeChanges++;
        for (int scenario = 0; scenario < 4; scenario++)
        {
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            var page = DashboardField<SettingsPage>(form, "dashboardSettingsPage"); page.SetStartup(false);
            var renderer = menu.Renderer;
            if (scenario < 2)
                Descendants(page).OfType<CheckBox>().Single(control => control.Name == (scenario == 0 ? "showOwnTrayIcon" : "restoreIconsOnExit")).Checked ^= true;
            else page.SetDraft(scenario == 2 ? page.Draft with { Language = "zh-CN" } : page.Draft with { Theme = "dark" });
            sizeChanges = 0;
            var saving = page.SaveChangesAsync();
            for (int i = 0; i < 400 && !saving.IsCompleted; i++) { Application.DoEvents(); Thread.Sleep(5); }
            Check(saving.IsCompleted && saving.GetAwaiter().GetResult(), $"Scenario {scenario}: settings save succeeds.");
            Check(originalItems.SequenceEqual(menu.Items.Cast<ToolStripItem>()), $"Scenario {scenario}: menu items are preserved.");
            Check(sizeChanges == 0 && list.Bounds == bounds, $"Scenario {scenario}: list geometry remains stable throughout save.");
            if (scenario < 2) Check(ReferenceEquals(renderer, menu.Renderer), $"Scenario {scenario}: unchanged appearance is not reapplied.");
        }
        Check(L.Current == "zh-CN" && menu.Items[2].Text == L.T("settings"), "Language changes still translate the menu.");
        Check(UiTheme.Dark && form.BackColor == UiTheme.Canvas, "Theme changes still update the window palette.");
        Check(new Controller(stateFolder).Saved.Theme == "dark", "Settings persist to disk.");
        controller.Saved.RestoreIconsOnExit = false; form.Close();
        File.WriteAllLines(report, log);
        return 0;
    }
}
