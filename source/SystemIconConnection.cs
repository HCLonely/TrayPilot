namespace TrayPilot;

internal sealed partial class MainForm
{
    SystemIconSession? systemIconSession;
    internal SystemIconSession? systemIconSessionForDiagnostics => systemIconSession is { Ticks: > 0 } ? systemIconSession : null;
    bool systemIconsConnecting;
    bool systemIconsChanging;
    bool systemIconDialogOpen, systemIconsReleasing;
    int systemIconsRequested;
    readonly System.Windows.Forms.Timer systemIconWatch = new() { Interval = 5000 };
    void SetupSystemIconWatch()
    {
        systemIconWatch.Tick += async (_, _) => await ApplySavedSystemIcons();
        Shown += async (_, _) => await ApplySavedSystemIcons();
        systemIconWatch.Start();
    }
    async Task ApplySavedSystemIcons()
    {
        if (closing || IsDisposed) return;
        if (systemIconsRequested == 0) { await ReleaseUnusedSystemIcons(); return; }
        if (systemIconsConnecting || systemIconsChanging) return;
        if (systemIconSession is { Ticks: > 0, Error: 0, IsDisposed: false } current &&
            current.Pid == SystemIconSession.ExplorerPid() && current.Requested == systemIconsRequested &&
            current.Legacy == controller.Saved.UseLegacySystemIconDiscovery) return;
        try { var session = await ConnectSystemIcons(); session.Set(systemIconsRequested); }
        catch (Exception ex)
        {
            if (!IsDisposed && !closing)
            {
                lastStatus = ex.Message;
                if (WebSystemVisible) { webSystemError = ex.Message; PublishWebState(); }
            }
        }
    }
    void RestoreLiveSystemIcons()
    {
        controller.SetHiddenSystemIcons(0);
        systemIconsRequested = 0;
        systemIconSession?.Set(0);
        _ = ReleaseUnusedSystemIcons();
    }
    async Task ReleaseUnusedSystemIcons()
    {
        if (systemIconsRequested != 0 || systemIconDialogOpen || systemIconsConnecting || systemIconsChanging ||
            systemIconsReleasing || systemIconSession is not { IsDisposed: false } session) return;
        systemIconsReleasing = true;
        try
        {
            int request = session.Set(0);
            for (int i = 0; i < 30 && !session.IsDisposed && session.Acknowledged != request && session.Error == 0; i++)
                await Task.Delay(100);
            if (systemIconsRequested == 0 && !systemIconDialogOpen && !systemIconsConnecting && !systemIconsChanging &&
                ReferenceEquals(systemIconSession, session))
            { session.Dispose(); systemIconSession = null; }
        }
        finally { systemIconsReleasing = false; }
    }
    async Task<SystemIconSession> ConnectSystemIcons(bool force = false)
    {
        if (!force && systemIconSession != null && systemIconSession.Pid == SystemIconSession.ExplorerPid() && systemIconSession.Ticks > 0 && systemIconSession.Error == 0 &&
            systemIconSession.Legacy == controller.Saved.UseLegacySystemIconDiscovery) return systemIconSession;
        if (systemIconsConnecting) throw new IOException(L.T("systemNativeConnecting"));
        systemIconsConnecting = true;
        try
        {
            if (systemIconSession != null) { systemIconSession.Dispose(); systemIconSession = null; await Task.Delay(500); }
            bool legacy = controller.Saved.UseLegacySystemIconDiscovery;
            int initialMask = systemIconsRequested;
            var session = await Task.Run(() => new SystemIconSession(legacy, initialMask));
            if (IsDisposed || closing) { session.Stop(!closing || controller.Saved.RestoreIconsOnExit); throw new ObjectDisposedException(nameof(MainForm)); }
            systemIconSession = session;
            session.SetAppearanceCapture(systemIconDialogOpen);
            for (int i = 0; i < 40 && session.Ticks == 0 && session.Error == 0; i++) await Task.Delay(100);
            if (session.Ticks == 0 || session.Error != 0)
            {
                int error = session.Error; session.Dispose(); systemIconSession = null;
                throw new IOException(L.F("systemNativeFailed", $"0x{error:X8}"));
            }
            if (systemIconsRequested != 0) session.Set(systemIconsRequested);
            return session;
        }
        finally { systemIconsConnecting = false; _ = ReleaseUnusedSystemIcons(); }
    }
}
