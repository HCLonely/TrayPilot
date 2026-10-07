using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int PreviewSettingsDashboard(string output, string option, bool dark)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(output)!, "settings-preview-" + Guid.NewGuid().ToString("N")));
        controller.Saved.Language = option.Contains("-en") ? "en-US" : "zh-CN"; controller.Saved.Theme = dark ? "dark" : "light";
        using var main = new MainForm(controller, initialize: false); if (option.Contains("-small")) main.Size = main.MinimumSize;
        bool about = option.Contains("-about");
        DashboardCall(main, about ? "ShowAbout" : "ShowSettings");
        var page = about ? null : DashboardField<SettingsPage>(main, "dashboardSettingsPage");
        if (page != null)
        {
            page.ReadOnlyPreview = true; page.SetStartup(false);
            if (option.Contains("-appearance")) page.SelectView(1);
            if (option.Contains("-shortcuts") || option.Contains("-conflict")) page.SelectView(2);
            if (option.Contains("-conflict")) page.SetDraft(page.Draft with { MainEnabled = true, ShowEnabled = true, ShowKeys = page.Draft.MainKeys });
        }
        if (about)
        {
            var aboutPage = DashboardField<AboutPage>(main, "dashboardAboutPage"); aboutPage.ReadOnlyPreview = true;
            if (option.Contains("-failed")) aboutPage.ShowPreviewFailure();
        }
        using var capture = new System.Windows.Forms.Timer { Interval = 700 };
        capture.Tick += (_, _) =>
        {
            var target = option.Contains("-hotkey") ? main.OwnedForms.FirstOrDefault() : main;
            if (target == null) return; capture.Stop();
            using var bitmap = new Bitmap(target.Width, target.Height); target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, target.Size)); bitmap.Save(output);
            if (target != main) target.Close(); page?.CancelChanges(); main.Close();
        };
        main.Shown += (_, _) =>
        {
            capture.Start();
            if (option.Contains("-hotkey")) { using var editor = new HotkeyEditor(L.T("openMainWindowHotkey"), Keys.Control | Keys.Alt | Keys.M, main.Font); editor.ShowDialog(main); }
        };
        Application.Run(main); return 0;
    }

    static int TestSettingsDashboard(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        void Complete(Task task)
        {
            for (int i = 0; i < 400 && !task.IsCompleted; i++) { Application.DoEvents(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("UI test task did not finish."); task.GetAwaiter().GetResult();
        }
        L.Set("zh-CN"); UiTheme.Set("light");
        var saved = new SavedState { Theme = "light", Language = "zh-CN" };
        var initial = SettingsPreferences.Read(saved, false);
        Check(initial.CloseToTray && initial.ShowTrayIcon && !initial.RestoreOnExit && initial.Hotkeys.All(item => !item.Enabled), "Default close, tray, exit and shortcut preferences are preserved.");
        int calls = 0; bool fail = false;
        TaskCompletionSource? pending = null;
        async Task Save(SettingsPreferences draft, bool startupChanged)
        {
            calls++; if (pending != null) await pending.Task;
            if (fail) throw new IOException("Simulated settings save failure"); draft.Write(saved);
        }
        using var font = new Font("Microsoft YaHei UI", 9.5f);
        using var host = new Form { ClientSize = new(1180, 850), Font = font };
        using var page = new SettingsPage(initial, Save, mode => { UiTheme.Set(mode); UiTheme.Apply(host); }, (_, _) => { }, font);
        host.Controls.Add(page); host.Show(); Application.DoEvents();
        var saveButton = Descendants(page).OfType<Button>().Single(button => button.Name == "settingsSave");
        Check(!page.Dirty && !saveButton.Enabled, "An unchanged settings draft disables saving.");
        var close = Descendants(page).OfType<CheckBox>().Single(control => control.Name == "closeToTray"); close.Checked = false;
        Check(page.Dirty && saved.CloseToTray && !page.Draft.CloseToTray, "General changes edit the draft without mutating saved settings.");
        page.SelectView(1); page.SelectView(2);
        Check(!page.Draft.CloseToTray && page.Dirty, "Switching settings tabs retains unsaved changes.");
        Check(!page.CanLeave(() => DialogResult.No) && page.Dirty, "Declining discard keeps the settings draft and page.");
        page.SetDraft(page.Draft with { Theme = "dark" });
        Check(UiTheme.Dark && saved.Theme == "light", "Theme preview changes the palette without writing the preference.");
        Check(page.CanLeave(() => DialogResult.Yes) && !page.Dirty && !UiTheme.Dark, "Discarding changes restores the original theme and clears the draft.");
        page.SetDraft(page.Draft with { MainEnabled = true, ShowEnabled = true, ShowKeys = initial.MainKeys }); page.SelectView(2);
        int beforeCalls = calls; Complete(page.SaveChangesAsync());
        Check(page.Draft.InvalidHotkeys.SequenceEqual(new[] { 0, 1 }) && calls == beforeCalls, "Duplicate enabled shortcuts identify both fields and block persistence.");
        Check(Descendants(page).OfType<Label>().Count(label => label.Text == L.T("settingsHotkeyFieldError")) == 2, "Both conflicting shortcut fields show inline errors.");
        Check(Descendants(page).Single(control => control.Name == "settingsErrorSummary").Visible, "Validation exposes a focusable error summary.");
        page.EditHotkey(1, false, initial.MainKeys);
        Check(page.Draft.InvalidHotkeys.Length == 0, "Disabled duplicate shortcuts do not prevent saving.");
        page.EditHotkey(0, true, Keys.Shift | Keys.M);
        Check(page.Draft.InvalidHotkeys.SequenceEqual(new[] { 0 }), "A shortcut without Ctrl or Alt is rejected.");
        page.CancelChanges(); page.SetDraft(page.Draft with { RestoreOnExit = true }); fail = true; Complete(page.SaveChangesAsync());
        Check(page.Dirty && !saved.RestoreIconsOnExit && saveButton.Enabled, "A failed save retains the draft and allows retry without changing saved values.");
        fail = false; pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = page.SaveChangesAsync(); Application.DoEvents(); beforeCalls = calls;
        Check(page.Working && !saveButton.Enabled && !page.CanLeave(() => throw new Exception("A save must not ask to discard.")), "Saving disables duplicate submission and navigation.");
        Complete(page.SaveChangesAsync()); Check(calls == beforeCalls, "An outstanding save cannot overlap a second submission.");
        pending.SetResult(); Complete(saving); pending = null;
        Check(!page.Dirty && saved.RestoreIconsOnExit && !saveButton.Enabled, "A confirmed save updates the baseline and disables saving again.");
        page.SelectView(0); host.ClientSize = new(800, 650); Application.DoEvents();
        var settingsDocument = Descendants(page).OfType<DashboardDocument>().Single();
        settingsDocument.AutoScrollPosition = new(0, int.MaxValue); Application.DoEvents();
        int bottomOffset = settingsDocument.AutoScrollPosition.Y, contentExtent = settingsDocument.DisplayRectangle.Height;
        for (int i = 0; i < 100; i++) page.Render();
        Check(settingsDocument.VerticalScroll.Visible && settingsDocument.DisplayRectangle.Height == contentExtent && settingsDocument.AutoScrollPosition.Y == bottomOffset
            && Math.Abs(settingsDocument.Content.Bottom - settingsDocument.ClientSize.Height) <= 2,
            "The narrow settings document scrolls only to its content and preserves the bottom across repeated rendering.");
        page.SetStartup(null, "Simulated startup query failure"); page.SetDraft(page.Draft with { ShowTrayIcon = false });
        Complete(page.SaveChangesAsync());
        Check(!page.Dirty && !saved.ShowTrayIcon && page.Draft.Startup == null, "Other preferences can be saved when startup cannot be read, without writing a startup choice.");
        using (var editor = new HotkeyEditor("Test", Keys.Control | Keys.Oemtilde, font))
        {
            Check(editor.Value == (Keys.Control | Keys.Oemtilde), "The shortcut editor preserves valid saved keys beyond letters and function keys.");
            Check(editor.CancelButton != null && Descendants(editor).OfType<CheckBox>().Count() == 3, "The editor provides mouse modifier choices and an Escape cancel action.");
        }
        host.Close();

        var folder = Path.Combine(Path.GetDirectoryName(output)!, "settings-test-" + Guid.NewGuid().ToString("N"));
        var controller = new Controller(folder); controller.Saved.Language = "zh-CN"; controller.Saved.Theme = "light";
        controller.Saved.HiddenPaths.Add(@"C:\TrayPilot.UI.Tests\keep.exe"); controller.Saved.HiddenSystemIcons = 16; controller.Save();
        string registryPath = @"Software\TrayPilotSettingsDashboardTest-" + Guid.NewGuid().ToString("N");
        var startup = new StartupRegistration(registryPath);
        try
        {
            var before = SettingsPreferences.Read(controller.Saved, false);
            string blocked = Path.Combine(folder, "settings.json.tmp"); Directory.CreateDirectory(blocked);
            bool failed = false;
            try { SettingsTransaction.Save(controller, before with { RestoreOnExit = true, Startup = true }, true, startup, () => true); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
            Check(failed && !controller.Saved.RestoreIconsOnExit && !startup.Enabled, "Persistence failure restores preferences and the isolated startup registration.");
            Check(controller.Saved.HiddenPaths.Count == 1 && controller.Saved.HiddenSystemIcons == 16, "Settings rollback preserves ordinary rules and system hiding choices.");
            // Remove only the deliberately created empty blocker in this isolated test directory.
            Directory.Delete(blocked);
            int registrations = 0; failed = false;
            try { SettingsTransaction.Save(controller, before with { MainEnabled = true }, false, startup, () => ++registrations > 1); }
            catch (IOException) { failed = true; }
            Check(failed && !controller.Saved.MainHotkeyEnabled && registrations == 2, "Registration failure restores the saved shortcuts and attempts re-registration of the originals.");
            SettingsTransaction.Save(controller, before with { RestoreOnExit = true }, false, startup, () => throw new Exception("Unchanged shortcuts should not be re-registered."));
            Check(new Controller(folder).Saved.RestoreIconsOnExit, "A successful preference save persists without re-registering unchanged shortcuts.");
            controller.Saved.RestoreIconsOnExit = false;
            using (var hotkeyHost = new Form())
            using (var blocker = new GlobalHotkey(hotkeyHost.Handle, 0x6201))
            using (var contender = new GlobalHotkey(hotkeyHost.Handle, 0x6301))
            {
                Keys claimed = Keys.None;
                foreach (int key in Enumerable.Range((int)Keys.F1, 11))
                    if (blocker.TrySet(true, Keys.Control | Keys.Alt | Keys.Shift | (Keys)key)) { claimed = Keys.Control | Keys.Alt | Keys.Shift | (Keys)key; break; }
                if (claimed != Keys.None)
                {
                    bool conflict = false; int attempts = 0;
                    try
                    {
                        SettingsTransaction.Save(controller, SettingsPreferences.Read(controller.Saved, false) with { MainEnabled = true, MainKeys = claimed }, false, startup,
                            () => { attempts++; return contender.TrySet(controller.Saved.MainHotkeyEnabled, (Keys)controller.Saved.MainHotkey); });
                    }
                    catch (IOException) { conflict = true; }
                    Check(conflict && attempts == 2 && !controller.Saved.MainHotkeyEnabled,
                        "A real RegisterHotKey conflict rejects the save and restores the disabled original shortcut.");
                }
                else checks.Add("Native conflict check skipped: no free test combination was available; no existing registration was replaced.");
            }

            using var main = new MainForm(controller, initialize: false, startup: startup); main.Show(); Application.DoEvents();
            int forms = Application.OpenForms.Count; DashboardCall(main, "ShowSettings");
            var embedded = DashboardField<SettingsPage>(main, "dashboardSettingsPage"); embedded.SetStartup(false);
            Check(embedded.Visible && embedded.Parent == DashboardField<Panel>(main, "dashboardPageHost") && main.OwnedForms.Length == 0 && Application.OpenForms.Count == forms,
                "Settings open inside the main window without adding a top-level form.");
            Check(DashboardField<ThemeButton>(main, "dashboardSettingsNav").Selected && !DashboardField<ThemeButton>(main, "dashboardIconNav").Selected, "Sidebar selection follows the settings page.");
            DashboardCall(main, "ShowAbout"); var about = DashboardField<AboutPage>(main, "dashboardAboutPage");
            Check(about.Visible && !embedded.Visible && DashboardField<ThemeButton>(main, "dashboardAboutNav").Selected && Application.OpenForms.Count == forms, "About opens in the same content host with the correct navigation state.");
            DashboardCall(main, "ShowRulesPage"); Check(!about.Visible, "Switching to rules hides the About page.");
            DashboardCall(main, "ShowSystemIcons"); Check(!about.Visible && !embedded.Visible, "Switching to system icons hides preferences pages.");
            DashboardCall(main, "ShowSettings"); embedded = DashboardField<SettingsPage>(main, "dashboardSettingsPage"); embedded.SetStartup(false);
            embedded.SetDraft(embedded.Draft with { Language = "en-US" }); Complete(embedded.SaveChangesAsync());
            var translated = DashboardField<SettingsPage>(main, "dashboardSettingsPage");
            Check(L.Current == "en-US" && translated.Visible && !translated.Dirty && !ReferenceEquals(translated, embedded), "Saving a language change rebuilds the active settings page with translated text and a clean draft.");
            Check(controller.Saved.HiddenPaths.Count == 1 && controller.Saved.HiddenSystemIcons == 16, "Page navigation and saving language leave hiding rules unchanged.");
            main.Close();
        }
        finally { startup.SetEnabled(false); Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }

        using var updateHost = new Form { ClientSize = new(1180, 850), Font = font };
        int updateCalls = 0; TaskCompletionSource<UpdateChecker.Release?>? updatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool updateFails = false, updateTimeout = false; UpdateChecker.Release? updateResult = null;
        using var updatePage = new AboutPage(folder, font, token => { updateCalls++; return updateTimeout ? Task.FromException<UpdateChecker.Release?>(new OperationCanceledException()) : updateFails ? Task.FromException<UpdateChecker.Release?>(new IOException("offline")) : updatePending?.Task ?? Task.FromResult(updateResult); });
        updateHost.Controls.Add(updatePage); updateHost.Show(); Application.DoEvents();
        Check(updateCalls == 0 && updatePage.UpdateStatus == L.T("aboutNotChecked"), "About never checks updates automatically.");
        var updating = updatePage.CheckAsync(); Complete(updatePage.CheckAsync());
        Check(updatePage.Checking && updateCalls == 1 && !Descendants(updatePage).OfType<Button>().Single(button => button.Name == "checkForUpdates").Enabled, "An update check displays loading and prevents duplicate requests.");
        updatePending.SetResult(new(new Version(999, 0, 0, 0), "v999.0.0")); Complete(updating); updatePending = null;
        Check(!updatePage.Checking && updatePage.UpdateStatus.Contains("v999.0.0"), "A newer release displays its actual release tag.");
        Check(Descendants(updatePage).OfType<Button>().Single(button => button.Text == L.T("aboutDownloadRelease")).Visible, "A newer release exposes a separate download-page action.");
        updateResult = new(UpdateChecker.CurrentVersion, "current"); Complete(updatePage.CheckAsync());
        Check(updatePage.UpdateStatus == L.F("alreadyUpToDate", UpdateChecker.CurrentVersionText), "An up-to-date result names the running version.");
        updateResult = null; Complete(updatePage.CheckAsync()); Check(updatePage.UpdateStatus == L.T("noPublishedRelease"), "No published release has a distinct result.");
        updateFails = true; Complete(updatePage.CheckAsync());
        Check(updatePage.UpdateStatus == L.T("updateCheckFailed") && !updatePage.Checking, "Update failures are inline and restore the retry action.");
        updateTimeout = true; Complete(updatePage.CheckAsync());
        Check(updatePage.UpdateStatus == L.T("updateCheckTimedOut") && !updatePage.Checking, "An update timeout has a distinct inline result and allows retry.");
        Check(!Descendants(updatePage).OfType<TextBox>().Single().Visible, "Runtime and settings paths start collapsed.");
        Descendants(updatePage).OfType<Button>().Single(button => button.Text == L.T("aboutShowDiagnostics")).PerformClick(); Application.DoEvents();
        Check(Descendants(updatePage).OfType<TextBox>().Single().Visible && Descendants(updatePage).OfType<TextBox>().Single().Text.Contains(folder), "Expanded diagnostics show the actual isolated configuration folder.");
        updateHost.Close();
        using var cancelled = new AboutPage(folder, font, token => Task.Delay(Timeout.Infinite, token).ContinueWith<UpdateChecker.Release?>(task => { task.GetAwaiter().GetResult(); return null; }, TaskScheduler.Default));
        var cancelledTask = cancelled.CheckAsync(); cancelled.Dispose(); Complete(cancelledTask);
        Check(!cancelled.Checking, "Disposing About cancels an outstanding update check safely.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
}
