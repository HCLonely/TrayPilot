namespace TrayPilot;

internal sealed partial class MainForm
{
    SystemIconsPage? dashboardSystemPage;
    ThemeButton? dashboardSystemNav;
    string? dashboardSystemLanguage;

    SystemIconsSnapshot ReadSystemIconsSnapshot()
    {
        var session = systemIconSession;
        bool connected = session is { IsDisposed: false } && session.Ticks > 0 && session.Error == 0 && session.Pid == SystemIconSession.ExplorerPid();
        return new(connected, systemIconsConnecting, systemIconsRequested, connected ? session!.Found : 0,
            connected ? session!.Hidden : 0, connected && session!.SharedMicrophoneLocation, controller.Saved.UseLegacySystemIconDiscovery,
            connected || session == null || session.IsDisposed ? "" : L.F("systemNativeFailed", $"0x{session.Error:X8}"),
            connected ? session!.DebugInfo() : "", connected ? session!.ReadAppearances() : new (string, string)[12]);
    }

    void ShowSystemIcons()
    {
        if (busy || closing || dashboardRulesPage?.Working == true || dashboardSystemPage?.Working == true || !TryLeaveSettings()) return;
        HidePreferencesPages();
        EnsureUiContext();
        if (dashboardSystemPage == null || dashboardSystemLanguage != L.Current)
        {
            dashboardSystemPage?.Dispose();
            dashboardSystemPage = new SystemIconsPage(ReadSystemIconsSnapshot(), ReadSystemIconsSnapshot, ReconnectSystemIconsPageAsync,
                ChangeSystemIconsPageAsync, ChangeSystemDiscoveryPageAsync, Font);
            dashboardSystemLanguage = L.Current; dashboardPageHost.Controls.Add(dashboardSystemPage);
        }
        dashboardIconPage?.Hide(); dashboardRulesPage?.Hide();
        systemIconDialogOpen = true; systemIconSession?.SetAppearanceCapture(true);
        dashboardSystemPage.RefreshState(ReadSystemIconsSnapshot()); dashboardSystemPage.Show(); dashboardSystemPage.BringToFront();
        SetDashboardNavigation(false, systemActive: true);
        if (initialized && (!Visible || WindowState == FormWindowState.Minimized)) OpenMainWindow();
        if (initialized) { dashboardSystemPage.StartPolling(); _ = dashboardSystemPage.EnsureConnectedAsync(); }
    }

    void UpdateSystemPageActivation()
    {
        if (!initialized || dashboardSystemPage == null || closing || IsDisposed) return;
        bool active = Visible && WindowState != FormWindowState.Minimized && dashboardSystemNav?.Selected == true && dashboardSystemPage.Visible;
        if (active == systemIconDialogOpen) return;
        systemIconDialogOpen = active; systemIconSession?.SetAppearanceCapture(active);
        if (active)
        {
            dashboardSystemPage.RefreshState(ReadSystemIconsSnapshot()); dashboardSystemPage.StartPolling();
            _ = dashboardSystemPage.EnsureConnectedAsync();
        }
        else { dashboardSystemPage.StopPolling(); _ = ReleaseUnusedSystemIcons(); }
    }

    void DeactivateSystemPage()
    {
        dashboardSystemPage?.StopPolling(); dashboardSystemPage?.Hide(); systemIconDialogOpen = false;
        systemIconSession?.SetAppearanceCapture(false);
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
