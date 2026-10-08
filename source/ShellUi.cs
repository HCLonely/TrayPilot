namespace TrayPilot;

internal sealed partial class MainForm
{
    NotifyIcon? trayIcon;
    GlobalHotkey? showAllHotkey, hideRulesHotkey, mainHotkey;
    bool? cachedStartup;
    string? startupQueryError;
    bool startupQueryPending;
    int startupQueryVersion;
    internal const int TrayPageSize = 10;
    bool exitRequested, hotkeyWarning;

    void SetupShell(bool initialize) { if (initialize) InitializeTray(); }
    void InitializeTray()
    {
        if (trayIcon != null) return;
        _ = Handle;
        trayIcon = new NotifyIcon { Icon = AppIcon.Create(), Text = "TrayPilot" };
        trayIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenMainWindow();
            else if (e.Button == MouseButtons.Right) _ = ShowWebTrayAsync(Cursor.Position);
        };
        trayIcon.Visible = controller.Saved.ShowTrayIcon;
        RegisterShortcuts(); _ = RefreshStartupMenuAsync();
    }
    void CacheStartup(bool? enabled, string? error = null)
    {
        startupQueryVersion++; cachedStartup = enabled; startupQueryError = error; PublishTrayState();
    }
    async Task RefreshStartupMenuAsync()
    {
        if (startupQueryPending || closing || IsDisposed) return;
        startupQueryPending = true;
        int version = startupQueryVersion;
        try
        {
            bool enabled = await Task.Run(() => startup.Enabled);
            if (!closing && !IsDisposed && version == startupQueryVersion) CacheStartup(enabled);
        }
        catch (Exception ex)
        {
            if (!closing && !IsDisposed && version == startupQueryVersion) CacheStartup(null, ex.Message);
        }
        finally { startupQueryPending = false; }
    }

    void ApplyLanguage() { Text = L.T("mainWindowTitle"); PublishWebState(force: true); }
    void ShowMainMenu() { if (!closing && !IsDisposed) _ = ShowWebTrayAsync(Cursor.Position); }
    internal void ActivateMainWindow() => OpenMainWindow();
    void OpenMainWindow()
    {
        if (IsDisposed) return;
        HideWebTray();
        Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); Native.SetForegroundWindow(Handle);
        if (initialized) _ = RefreshAsync();
    }

    internal void RequestExit()
    {
        if (webReady && webPresented && !webExitConfirmed) { RequestHtmlExit(); return; }
        exitRequested = true; Close();
    }
    void RegisterShortcuts()
    {
        showAllHotkey ??= new GlobalHotkey(Handle, 0x4A01);
        mainHotkey ??= new GlobalHotkey(Handle, 0x4A11);
        hideRulesHotkey ??= new GlobalHotkey(Handle, 0x4A21);
        showAllHotkey.Dispose(); mainHotkey.Dispose(); hideRulesHotkey.Dispose();
        hotkeyWarning = !showAllHotkey.TrySet(controller.Saved.ShowAllHotkeyEnabled, (Keys)controller.Saved.ShowAllHotkey);
        hotkeyWarning |= !mainHotkey.TrySet(controller.Saved.MainHotkeyEnabled, (Keys)controller.Saved.MainHotkey);
        hotkeyWarning |= !hideRulesHotkey.TrySet(controller.Saved.HideRulesHotkeyEnabled, (Keys)controller.Saved.HideRulesHotkey);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0016 && message.WParam != 0) EndWindowsSession(); // WM_ENDSESSION
        if (message.Msg == 0x0312 && showAllHotkey?.Matches(message.WParam) == true) { _ = ApplyVisibilityPresetAsync(false); return; }
        if (message.Msg == 0x0312 && mainHotkey?.Matches(message.WParam) == true) { OpenMainWindow(); return; }
        if (message.Msg == 0x0312 && hideRulesHotkey?.Matches(message.WParam) == true) { _ = ApplyVisibilityPresetAsync(true); return; }
        base.WndProc(ref message);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        showAllHotkey?.Dispose(); showAllHotkey = null; hideRulesHotkey?.Dispose(); hideRulesHotkey = null; mainHotkey?.Dispose(); mainHotkey = null;
        base.OnHandleDestroyed(e);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (trayIcon != null && controller != null)
        {
            RegisterShortcuts();
        }
    }
}
