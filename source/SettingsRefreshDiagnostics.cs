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
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        for (int scenario = 0; scenario < 4; scenario++)
        {
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var dialog = Application.OpenForms.Cast<Form>().Single(x => x != form);
                try
                {
                    var controls = Descendants(dialog).ToList();
                    if (scenario == 1) controls.OfType<CheckBox>().Single(x => x.Name == "restoreIconsOnExit").Checked = !controller.Saved.RestoreIconsOnExit;
                    if (scenario >= 2)
                    {
                        var combo = controls.OfType<ComboBox>().Single(x => x.Name == (scenario == 2 ? "language" : "theme"));
                        combo.SelectedItem = combo.Items.Cast<MainForm.LanguageChoice>().Single(x => x.Code == (scenario == 2 ? "zh-CN" : "dark"));
                    }
                    var renderer = menu.Renderer;
                    sizeChanges = 0;
                    controls.OfType<Button>().Single(x => x.Text == L.T("save")).PerformClick();
                    Check(dialog.DialogResult == DialogResult.OK, $"Scenario {scenario}: settings save succeeds.");
                    Check(originalItems.SequenceEqual(menu.Items.Cast<ToolStripItem>()), $"Scenario {scenario}: menu items are preserved.");
                    Check(sizeChanges == 0 && list.Bounds == bounds, $"Scenario {scenario}: list geometry remains stable throughout save.");
                    if (scenario < 2) Check(ReferenceEquals(renderer, menu.Renderer), $"Scenario {scenario}: unchanged appearance is not reapplied.");
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start();
            typeof(MainForm).GetMethod("ShowSettings", flags)!.Invoke(form, null);
            if (failure != null) throw failure;
        }
        Check(L.Current == "zh-CN" && menu.Items[2].Text == L.T("settings"), "Language changes still translate the menu.");
        Check(UiTheme.Dark && form.BackColor == UiTheme.Canvas, "Theme changes still update the window palette.");
        Check(new Controller(stateFolder).Saved.Theme == "dark", "Settings persist to disk.");
        File.WriteAllLines(report, log);
        return 0;
    }
}
