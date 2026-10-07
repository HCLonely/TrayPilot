namespace TrayPilot;
internal sealed partial class MainForm
{
    bool? pendingVisibilityPreset;
    void CompleteOperation()
    {
        busy = false;
        UpdateDashboard();
        if (exitRequested && !closing && !IsDisposed)
        {
            BeginInvoke(() => { if (!busy && !closing && !IsDisposed && exitRequested) _ = ExitAsync(); });
            return;
        }
        if (pendingVisibilityPreset == null || closing || IsDisposed) return;
        BeginInvoke(() =>
        {
            if (IsDisposed || closing || busy || pendingVisibilityPreset is not bool next) return;
            pendingVisibilityPreset = null; _ = ApplyVisibilityPresetAsync(next);
        });
    }
    internal async Task ApplyVisibilityPresetAsync(bool hideMatches)
    {
        if (closing || IsDisposed) return;
        if (busy) { pendingVisibilityPreset = hideMatches; return; }
        EnsureUiContext();
        pendingVisibilityPreset = null; busy = true;
        try
        {
            var scanned = await Task.Run(visibilityScanner);
            if (IsDisposed || closing) return;
            entries = scanned.Where(x => x.Pid != Environment.ProcessId).ToList();
            bool previous = controller.Saved.RulesPaused, previousTray = controller.Saved.ShowTrayIcon;
            controller.Saved.RulesPaused = !hideMatches;
            if (!hideMatches) controller.Saved.ShowTrayIcon = true;
            try { controller.Save(); } catch { controller.Saved.RulesPaused = previous; controller.Saved.ShowTrayIcon = previousTray; throw; }
            if (!hideMatches && trayIcon != null) trayIcon.Visible = true;
            try
            {
                if (hideMatches) { controller.ResetManualVisibility(); await controller.ApplyAsync(entries); }
                else
                {
                    await controller.ChangeManyAsync(entries.Where(x => x.State == 1), false);
                    RestoreLiveSystemIcons();
                }
            }
            finally { UpdateEntryStates(); }
            UpdateStatus();
        }
        catch (Exception ex) { status.Text = L.T("operationIncomplete") + ": " + ex.Message; }
        finally { CompleteOperation(); UpdateTimer(); }
    }
    void ApplyTheme()
    {
        UiTheme.Set(dashboardSettingsPage?.Dirty == true ? dashboardSettingsPage.Draft.Theme : controller.Saved.Theme);
        UiTheme.Apply(this); UiTheme.Apply(trayMenu); UiTheme.Apply(itemMenu); dashboardDetailSignature = null; UpdateDashboard();
        dashboardRulesPage?.Render();
        dashboardSystemPage?.RefreshState(ReadSystemIconsSnapshot());
        dashboardSettingsPage?.Render();
        foreach (Form form in Application.OpenForms) if (form != this) UiTheme.Apply(form);
    }
    void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        string mode = dashboardSettingsPage?.Dirty == true ? dashboardSettingsPage.Draft.Theme : controller.Saved.Theme;
        if (IsDisposed || !IsHandleCreated || mode != "system") return;
        try { BeginInvoke(() => { if (!IsDisposed) ApplyTheme(); }); } catch (InvalidOperationException) { }
    }
}
