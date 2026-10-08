namespace TrayPilot;
internal sealed partial class MainForm
{
    SystemIconsSnapshot ReadSystemIconsSnapshot()
    {
        var session = systemIconSession;
        bool connected = session is { IsDisposed: false } && session.Ticks > 0 && session.Error == 0 && session.Pid == SystemIconSession.ExplorerPid();
        return new(connected, systemIconsConnecting, systemIconsRequested, connected ? session!.Found : 0,
            connected ? session!.Hidden : 0, connected && session!.SharedMicrophoneLocation, controller.Saved.UseLegacySystemIconDiscovery,
            connected || session == null || session.IsDisposed ? "" : L.F("systemNativeFailed", $"0x{session.Error:X8}"),
            connected ? session!.DebugInfo() : "", connected ? session!.ReadAppearances() : new (string, string)[12]);
    }

    void UpdateSystemPageActivation()
    {
        if (!initialized || closing || IsDisposed || !WebSystemVisible) return;
        bool active = ListVisible;
        if (active == systemIconDialogOpen) return;
        if (active) StartWebSystemPage(); else DeactivateSystemPage();
    }
    void DeactivateSystemPage()
    {
        webSystemPoll.Stop(); systemIconDialogOpen = false; systemIconSession?.SetAppearanceCapture(false);
        if (initialized) _ = ReleaseUnusedSystemIcons();
    }
    async Task<SystemIconsSnapshot> ReconnectSystemIconsPageAsync()
    {
        if (closing || systemIconsChanging) throw new IOException(L.T("ruleOperationBusy"));
        await ConnectSystemIcons(force: true); return ReadSystemIconsSnapshot();
    }

    async Task<SystemIconsSnapshot> ChangeSystemDiscoveryPageAsync(bool legacy)
    {
        if (closing || systemIconsConnecting || systemIconsChanging) throw new IOException(L.T("ruleOperationBusy"));
        bool previous = controller.Saved.UseLegacySystemIconDiscovery;
        controller.Saved.UseLegacySystemIconDiscovery = legacy;
        try { controller.Save(); } catch { controller.Saved.UseLegacySystemIconDiscovery = previous; throw; }
        return await ReconnectSystemIconsPageAsync();
    }

    async Task<SystemIconsSnapshot> ChangeSystemIconsPageAsync(int mask, int restoreMask)
    {
        if (closing || systemIconsChanging || systemIconsConnecting || !ReadSystemIconsSnapshot().Connected || systemIconSession is not { } session)
            throw new IOException(L.T("systemNativeUnavailable"));
        int previous = systemIconsRequested; systemIconsChanging = true;
        try
        {
            int request = session.Set(mask, restoreMask);
            await AwaitSystemIconRequest(session, request);
            int expectedHidden = mask & session.Found;
            if (session.SharedMicrophoneLocation && (mask & 48) != 48) expectedHidden &= ~48;
            if ((session.Hidden & expectedHidden) != expectedHidden || (session.Hidden & session.Found & restoreMask) != 0)
                throw new IOException(L.F("systemNativeFailed", $"0x{session.Error:X8}"));
            controller.SetHiddenSystemIcons(mask); systemIconsRequested = mask;
            return ReadSystemIconsSnapshot();
        }
        catch (Exception operationError)
        {
            if (!IsDisposed && !closing && ReferenceEquals(systemIconSession, session) && !session.IsDisposed)
            {
                try { await AwaitSystemIconRequest(session, session.Set(previous, mask & ~previous)); }
                catch (Exception rollbackError) { throw new IOException(L.T("systemPageRollbackFailed") + " " + operationError.Message + " · " + rollbackError.Message, operationError); }
            }
            throw;
        }
        finally { systemIconsChanging = false; if (!systemIconDialogOpen) _ = ReleaseUnusedSystemIcons(); }
    }

    async Task AwaitSystemIconRequest(SystemIconSession session, int request)
    {
        for (int i = 0; i < 30 && !session.IsDisposed && !closing && session.Acknowledged != request && session.Error == 0; i++) await Task.Delay(100);
        if (session.IsDisposed || closing) throw new ObjectDisposedException(nameof(SystemIconSession));
        if (session.Acknowledged != request || session.Error != 0) throw new IOException(L.F("systemNativeFailed", $"0x{session.Error:X8}"));
    }
}
