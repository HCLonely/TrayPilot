namespace TrayPilot;
internal sealed partial class MainForm
{
    bool? pendingVisibilityPreset;
    void CompleteOperation()
    {
        busy = false;
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
            if (hideMatches) controller.Apply(entries);
            else foreach (var entry in entries.Where(x => x.State == 1)) controller.Show(entry);
            entries = entries.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
            RenderList(); UpdateStatus();
        }
        catch (Exception ex) { status.Text = L.T("操作未完成") + ": " + ex.Message; }
        finally { CompleteOperation(); }
    }
    void ApplyTheme()
    {
        UiTheme.Set(controller.Saved.Theme);
        UiTheme.Apply(this); UiTheme.Apply(trayMenu); UiTheme.Apply(itemMenu);
        foreach (Form form in Application.OpenForms) if (form != this) UiTheme.Apply(form);
    }
    void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || controller.Saved.Theme != "system") return;
        try { BeginInvoke(() => { if (!IsDisposed) ApplyTheme(); }); } catch (InvalidOperationException) { }
    }
}
