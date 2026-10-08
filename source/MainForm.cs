namespace TrayPilot;
internal sealed partial class MainForm : Form
{
    readonly Controller controller;
    readonly StartupRegistration startup;
    readonly Func<List<TrayEntry>> visibilityScanner;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    List<TrayEntry> entries = new();
    bool autoRefreshEnabled = true;
    string lastStatus = "";
    readonly ScanSession scanSession = new();
    readonly bool initialized;
    bool ListVisible => Visible && WindowState != FormWindowState.Minimized;
    bool RulesActive => !controller.Saved.RulesPaused && (controller.Saved.HiddenPaths.Count != 0 || controller.Saved.HiddenIcons.Count != 0);
    bool busy, closing;
    internal bool WebDiagnosticsSuspendScanning;
    internal MainForm(Controller controller, bool initialize = true, Func<List<TrayEntry>>? visibilityScanner = null, StartupRegistration? startup = null, bool startInTray = false, bool restoreOnStartup = false)
    {
        this.controller = controller;
        initialized = initialize;
        busy = restoreOnStartup;
        systemIconsRequested = controller.Saved.HiddenSystemIcons & SystemIconCatalog.All;
        this.startup = startup ?? new StartupRegistration();
        this.visibilityScanner = visibilityScanner ?? Scanner.Scan;
        L.Set(controller.Saved.Language); UiTheme.Set(controller.Saved.Theme);
        Text = L.T("mainWindowTitle"); AutoScaleMode = AutoScaleMode.Dpi; Width = 1380; Height = 920; MinimumSize = new(1020, 740);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Microsoft YaHei UI", 9.5f);
        BackColor = UiTheme.Canvas; ForeColor = UiTheme.Ink;
        SetupShell(initialize); ApplyTheme();
        if (initialize)
        {
            PrepareWebStartup();
            _ = EnableWebInterfaceAsync();
        }
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        timer.Tick += async (_, _) => await RefreshSnapshotAsync(false);
        VisibleChanged += (_, _) => { if (initialize) { UpdateSystemPageActivation(); UpdateTimer(); if (ListVisible) { RenderList(); _ = RefreshAsync(); } } };
        Resize += (_, _) => { if (initialize) { UpdateSystemPageActivation(); UpdateTimer(); if (ListVisible) RenderList(); } };
        Shown += async (_, _) =>
        {
            if (startInTray && trayIcon?.Visible == true) Hide();
            if (restoreOnStartup)
            {
                EnsureUiContext();
                lastStatus = L.T("restoringStartupIcons");
                await Task.Yield();
                try { await controller.RestoreManagedAsync(); }
                catch (OperationCanceledException) when (closing) { }
                catch (Exception ex)
                {
                    if (!closing && !IsDisposed)
                        MessageBox.Show(this, ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage"), L.T("restoreIncomplete"));
                }
                finally { CompleteOperation(); }
                if (!closing && !IsDisposed && controller.LoadWarning is string warning)
                    MessageBox.Show(this, warning, L.T("mainWindowTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (initialize && !closing && !IsDisposed) { await RefreshAsync(); UpdateTimer(); }
        };
        FormClosing += (_, e) =>
        {
            if (closing) return;
            // WM_QUERYENDSESSION asks for consent, not for an immediate exit.
            // Leave the window and ongoing work intact in case shutdown is canceled.
            if (e.CloseReason == CloseReason.WindowsShutDown) return;
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && controller.Saved.CloseToTray && trayIcon != null)
            { e.Cancel = true; Hide(); return; }
            if (e.CloseReason == CloseReason.UserClosing && webReady && webPresented && !webExitConfirmed)
            { e.Cancel = true; RequestHtmlExit(); return; }
            if (e.CloseReason == CloseReason.UserClosing && !TryLeaveSettings()) { e.Cancel = true; exitRequested = false; return; }
            if (!controller.Saved.RestoreIconsOnExit)
            {
                closing = true; controller.StopOperations(); timer.Stop(); systemIconWatch.Stop();
                return;
            }
            e.Cancel = true; exitRequested = true;
            if (!busy) _ = ExitAsync();
        };
        FormClosed += (_, _) => timer.Stop();
        if (initialize) SetupSystemIconWatch();
    }
    void UpdateTimer()
    {
        if (WebDiagnosticsSuspendScanning) { timer.Stop(); return; }
        bool rulesVisible = ListVisible && WebRulesVisible;
        timer.Interval = (ListVisible && (autoRefreshEnabled || rulesVisible)) || RulesActive || controller.Saved.Recovery.Count != 0 ? 2500 : 15000;
        timer.Enabled = !closing && !IsDisposed && (autoRefreshEnabled || rulesVisible || RulesActive || controller.Saved.Recovery.Count != 0);
    }
    void EnsureUiContext()
    {
        if (InvokeRequired) throw new InvalidOperationException("UI operations must start on the window thread.");
        // Diagnostics also invoke handlers outside Application.Run. Install the UI
        // context there so asynchronous controller mutations remain serialized.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    async Task ExitAsync()
    {
        EnsureUiContext();
        busy = true; timer.Stop();
        try { await controller.RestoreManagedAsync(); closing = true; Close(); }
        catch (Exception ex)
        {
            exitRequested = false; webExitConfirmed = false; OpenMainWindow();
            if (webReady) webInterface?.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "operationError", error = ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage") }));
            else MessageBox.Show(this, ex.Message + "\n" + L.T("recoveryRecordsRetainedMessage"), L.T("restoreIncomplete"));
        }
        finally { CompleteOperation(); UpdateTimer(); }
    }
    void EndWindowsSession()
    {
        closing = true;
        timer.Stop(); systemIconWatch.Stop();
        // WM_ENDSESSION(TRUE) is the final notification: do not schedule an async
        // continuation or display a dialog. Failed recovery keeps its journal.
        controller.StopOperations();
        try { if (controller.Saved.RestoreIconsOnExit) controller.RestoreManaged(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
    Task RefreshAsync() => RefreshSnapshotAsync(true);
    async Task RefreshSnapshotAsync(bool forceFull, bool operationOwned = false)
    {
        if ((!operationOwned && busy) || closing) return;
        EnsureUiContext();
        busy = true;
        try
        {
            bool full = forceFull || (ListVisible && (autoRefreshEnabled || WebRulesVisible));
            var scanned = await Task.Run(() => scanSession.Scan(full));
            if (closing || IsDisposed) return;
            entries = scanned.Where(x => x.Pid != Environment.ProcessId).ToList();
            var matches = controller.RuleMatcher();
            var affected = controller.Saved.RulesPaused ? new HashSet<string>() : entries
                .Where(x => x.State != 1 && matches(x) && !controller.IsTemporarilyShown(x)).Select(x => x.Key).ToHashSet();
            try { await controller.ApplyAsync(entries, freshSnapshot: true); }
            finally
            {
                if (!closing && !IsDisposed)
                {
                    entries = entries.Select(x => affected.Contains(x.Key) ? x with { State = Native.State(x) } : x)
                        .Where(x => x.State is 0 or 1).ToList();
                    if (forceFull || autoRefreshEnabled || WebRulesVisible) RenderList();
                }
            }
            UpdateStatus();
        }
        catch (Exception ex) { lastStatus = L.T("refreshFailedPrefix") + ex.Message; }
        finally { if (!operationOwned) { UpdateTimer(); CompleteOperation(); } }
    }
    void UpdateEntryStates() => UpdateEntryStatesCore(true);
    void UpdateEntryStatesCore(bool render)
    {
        if (closing || IsDisposed) return;
        entries = entries.Select(x => x with { State = Native.State(x) }).Where(x => x.State is 0 or 1).ToList();
        if (render) RenderList();
    }
    async Task ChangeEntriesAsync(IEnumerable<TrayEntry> selected, bool hide, bool asynchronous = true)
    {
        var targets = selected.DistinctBy(x => x.Key).ToList();
        foreach (var entry in targets) controller.SetManualVisibility(entry, hide);
        await controller.ChangeManyAsync(targets, hide, asynchronous);
    }
    void ChangeEntries(IEnumerable<TrayEntry> selected, bool hide)
        => ChangeEntriesAsync(selected, hide, asynchronous: false).GetAwaiter().GetResult();
    void UpdateStatus() => PublishWebState();
    void RenderList() => PublishWebState();
    void UpdateDashboard() => PublishWebState();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeWebInterface();
            if (closing) systemIconSession?.Stop(controller.Saved.RestoreIconsOnExit); else systemIconSession?.Dispose();
            systemIconSession = null; systemIconWatch.Dispose(); timer.Dispose();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            showAllHotkey?.Dispose(); hideRulesHotkey?.Dispose(); mainHotkey?.Dispose();
            if (trayIcon != null) { trayIcon.Visible = false; var icon = trayIcon.Icon; trayIcon.Dispose(); icon?.Dispose(); }
        }
        base.Dispose(disposing);
    }
}
