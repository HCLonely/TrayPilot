namespace TrayPilot;

internal sealed partial class MainForm
{
    SettingsPage? dashboardSettingsPage;
    AboutPage? dashboardAboutPage;
    ThemeButton? dashboardSettingsNav, dashboardAboutNav;
    string? dashboardSettingsLanguage, dashboardAboutLanguage;

    bool TryLeaveSettings() => dashboardSettingsPage?.Visible != true || dashboardSettingsPage.CanLeave(() =>
        MessageBox.Show(this, L.T("settingsDiscardPrompt"), L.T("settingsUnsaved"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2));
    void HidePreferencesPages() { dashboardSettingsPage?.Hide(); dashboardAboutPage?.Hide(); }
    void ShowSettings()
    {
        if (busy || closing || dashboardRulesPage?.Working == true || dashboardSystemPage?.NavigationAllowed == false || dashboardSettingsPage?.Working == true) return;
        trayMenu.Close(); DeactivateSystemPage(); dashboardIconPage?.Hide(); dashboardRulesPage?.Hide(); dashboardAboutPage?.Hide();
        if (dashboardSettingsPage == null || dashboardSettingsLanguage != L.Current)
        {
            dashboardSettingsPage?.Dispose();
            dashboardSettingsPage = new SettingsPage(SettingsPreferences.Read(controller.Saved, null), SaveSettingsPreferencesAsync,
                PreviewSettingsTheme, SettingsApplied, Font);
            dashboardSettingsLanguage = L.Current; dashboardPageHost.Controls.Add(dashboardSettingsPage);
            if (initialized) _ = ReadSettingsStartupAsync(dashboardSettingsPage);
        }
        else if (!dashboardSettingsPage.Visible && !dashboardSettingsPage.Dirty)
        {
            dashboardSettingsPage.Reload(SettingsPreferences.Read(controller.Saved, null));
            if (initialized) _ = ReadSettingsStartupAsync(dashboardSettingsPage);
        }
        dashboardSettingsPage.Show(); dashboardSettingsPage.BringToFront(); SetDashboardNavigation(false, settingsActive: true);
        if (initialized && (!Visible || WindowState == FormWindowState.Minimized)) OpenMainWindow();
    }
    async Task ReadSettingsStartupAsync(SettingsPage page)
    {
        try
        {
            bool enabled = await Task.Run(() => startup.Enabled);
            if (closing || IsDisposed || page.IsDisposed) return;
            CacheStartup(enabled); page.SetStartup(enabled);
        }
        catch (Exception ex) { if (!closing && !IsDisposed && !page.IsDisposed) { CacheStartup(null, ex.Message); page.SetStartup(null, ex.Message); } }
    }
    async Task SaveSettingsPreferencesAsync(SettingsPreferences draft, bool changeStartup)
    {
        if (busy || closing) throw new IOException(L.T("ruleOperationBusy"));
        EnsureUiContext(); busy = true;
        try
        {
            await Task.Yield();
            SettingsTransaction.Save(controller, draft, changeStartup, startup, () => { RegisterShortcuts(); return !hotkeyWarning; });
            if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon;
            if (draft.Startup.HasValue) CacheStartup(draft.Startup.Value);
        }
        finally { CompleteOperation(); }
    }
    void SettingsApplied(SettingsPreferences before, SettingsPreferences after)
    {
        if (after.Language != L.Current) { L.Set(after.Language); ApplyLanguage(); }
        if (after.Theme != before.Theme) ApplyTheme();
        UpdateStatus();
    }
    void PreviewSettingsTheme(string mode)
    {
        UiTheme.Set(mode); UiTheme.Apply(this); UiTheme.Apply(trayMenu); UiTheme.Apply(itemMenu);
        dashboardDetailSignature = null; UpdateDashboard(); dashboardRulesPage?.Render();
        dashboardSystemPage?.RefreshState(ReadSystemIconsSnapshot()); dashboardSettingsPage?.Render();
    }
    void ShowAbout()
    {
        if (busy || closing || dashboardRulesPage?.Working == true || dashboardSystemPage?.NavigationAllowed == false || !TryLeaveSettings()) return;
        trayMenu.Close(); DeactivateSystemPage(); dashboardIconPage?.Hide(); dashboardRulesPage?.Hide(); dashboardSettingsPage?.Hide();
        if (dashboardAboutPage == null || dashboardAboutLanguage != L.Current)
        {
            dashboardAboutPage?.Dispose(); dashboardAboutPage = new AboutPage(controller.SettingsFolder, Font);
            dashboardAboutLanguage = L.Current; dashboardPageHost.Controls.Add(dashboardAboutPage);
        }
        dashboardAboutPage.Show(); dashboardAboutPage.BringToFront(); SetDashboardNavigation(false, aboutActive: true);
        if (initialized && (!Visible || WindowState == FormWindowState.Minimized)) OpenMainWindow();
    }
}
