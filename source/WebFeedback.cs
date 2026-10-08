using System.Text.Json;

namespace TrayPilot;

internal sealed partial class MainForm
{
    sealed record VisibilityTarget(TrayEntry Entry, int Before, bool Hidden);
    sealed record VisibilityFailure(VisibilityTarget Target, string Error);
    readonly List<VisibilityTarget> webVisibilityUndo = new();
    readonly List<VisibilityFailure> webVisibilityFailures = new();
    (string[] Paths, TrayEntry[] Icons) webVisibilityManualBefore;
    string? webVisibilitySignature;
    long webVisibilitySerial;
    int webVisibilityTotal, webVisibilityCompleted;
    string webVisibilityPhase = "idle";
    bool webVisibilityRetryUndo;
    readonly Dictionary<string, (TrayEntry Entry, long Expires)> webTerminateTokens = new();
    string? webExitToken;
    bool webExitConfirmed;
    internal Func<TrayEntry, bool, Task>? WebVisibilityChangeForDiagnostics;
    internal Func<TrayEntry, Task>? WebTerminateForDiagnostics;
    internal Action? WebExitForDiagnostics;

    string VisibilitySignature() => JsonSerializer.Serialize(new { rules = WebRuleSignature(), manual = controller.CaptureManualVisibility() }, new JsonSerializerOptions { IncludeFields = true });
    bool CanUndoWebVisibility()
    {
        if (webVisibilityUndo.Count == 0) return false;
        if (webVisibilityPhase == "working") return false;
        var processes = new Dictionary<uint, (string Path, long Start)>();
        bool statesMatch = WebVisibilityChangeForDiagnostics != null || webVisibilityUndo.All(x => Scanner.SameOwner(x.Entry, processes) && Native.State(x.Entry) == (x.Hidden ? 1 : 0));
        if (statesMatch && webVisibilitySignature == VisibilitySignature()) return true;
        webVisibilityUndo.Clear(); return false;
    }
    object WebVisibilityState() => new { serial = webVisibilitySerial, phase = webVisibilityPhase, total = webVisibilityTotal,
        completed = webVisibilityCompleted, failed = webVisibilityFailures.Select(f => new { id = f.Target.Entry.Key, name = f.Target.Entry.Name, error = f.Error }),
        canRetry = webVisibilityPhase == "failed" && webVisibilityFailures.Count != 0, canUndo = CanUndoWebVisibility() };

    async Task<object> RunWebVisibilityAsync(List<TrayEntry> entriesToChange, bool hidden, bool retry = false, bool undo = false)
    {
        if (WebReadOnlyPreview) throw new InvalidOperationException("Read-only preview.");
        if (busy || webSettingsSaving) throw new IOException(L.T("ruleOperationBusy"));
        EnsureUiContext(); busy = webForegroundOperation = true; timer.Stop();
        List<VisibilityTarget> targets;
        if (undo)
        {
            if (!CanUndoWebVisibility()) { busy = webForegroundOperation = false; throw new IOException(L.T("operationIncomplete")); }
            targets = webVisibilityUndo.Select(x => x with { Hidden = x.Before == 1 }).ToList();
        }
        else if (retry) { targets = webVisibilityFailures.Select(f => f.Target).ToList(); undo = webVisibilityRetryUndo; }
        else
        {
            webVisibilityUndo.Clear(); webVisibilityManualBefore = controller.CaptureManualVisibility();
            targets = entriesToChange.DistinctBy(e => e.Key).Select(e => new VisibilityTarget(e, WebVisibilityChangeForDiagnostics == null ? Native.State(e) : e.State, hidden)).ToList();
        }
        webVisibilityRetryUndo = undo;
        webVisibilityFailures.Clear(); webVisibilitySerial++; webVisibilityTotal = targets.Count; webVisibilityCompleted = 0; webVisibilityPhase = "working"; PublishWebState(force: true);
        try
        {
            foreach (var target in targets)
            {
                var manual = controller.CaptureManualVisibility();
                try
                {
                    if (WebVisibilityChangeForDiagnostics == null && (!Scanner.SameOwner(target.Entry) || Native.State(target.Entry) is not (0 or 1)))
                        throw new IOException(L.T("processUnavailableMessage"));
                    controller.SetManualVisibility(target.Entry, target.Hidden);
                    if (WebVisibilityChangeForDiagnostics != null) await WebVisibilityChangeForDiagnostics(target.Entry, target.Hidden);
                    else await controller.ChangeManyAsync(new[] { target.Entry }, target.Hidden);
                    int after = WebVisibilityChangeForDiagnostics == null ? Native.State(target.Entry) : target.Hidden ? 1 : 0;
                    if (after != (target.Hidden ? 1 : 0)) throw new IOException(L.T("iconUnavailableMessage"));
                    // A restore which changed the shell but failed to save its journal remains retryable.
                    if (!target.Hidden && controller.Saved.Recovery.Any(e => e.Key == target.Entry.Key)) throw new IOException(L.T("recoveryRecordsRetainedMessage"));
                    webVisibilityCompleted++;
                    if (!undo && !webVisibilityUndo.Any(x => x.Entry.Key == target.Entry.Key)) webVisibilityUndo.Add(target);
                    if (undo) webVisibilityUndo.RemoveAll(x => x.Entry.Key == target.Entry.Key);
                    entries = entries.Select(e => e.Key == target.Entry.Key ? e with { State = after } : e).ToList();
                }
                catch (Exception ex)
                {
                    controller.RestoreManualVisibility(manual.Paths, manual.Icons);
                    webVisibilityFailures.Add(new(target, ex.Message));
                    if (WebVisibilityChangeForDiagnostics == null)
                        entries = entries.Select(e => e.Key == target.Entry.Key ? e with { State = Native.State(e) } : e).Where(e => e.State is 0 or 1).ToList();
                }
                PublishWebState(force: true);
            }
            if (undo && webVisibilityFailures.Count == 0)
            { controller.RestoreManualVisibility(webVisibilityManualBefore.Paths, webVisibilityManualBefore.Icons); webVisibilityUndo.Clear(); }
            webVisibilityPhase = webVisibilityFailures.Count == 0 ? undo ? "undone" : "done" : "failed";
            webVisibilitySignature = VisibilitySignature(); RenderList(); UpdateStatus();
            return WebVisibilityState();
        }
        finally { webForegroundOperation = false; CompleteOperation(); UpdateTimer(); PublishWebState(force: true); }
    }

    async Task<object?> ExecuteWebFeedbackCommand(string action, JsonElement root)
    {
        if (action == "trayClose") { HideWebTray(); return null; }
        if (action == "trayOpen") { HideWebTray(); OpenMainWindow(); return null; }
        if (action == "exitPrepare") return PrepareWebExit();
        if (action == "terminatePrepare")
        {
            var entry = ResolveWebEntry(root.GetProperty("id").GetString());
            if (entry.Pid == Environment.ProcessId || Scanner.IsShellEntry(entry)) throw new InvalidOperationException(L.T("systemTrayEndTaskUnsupportedMessage"));
            webTerminateTokens.Clear(); string token = Guid.NewGuid().ToString("N");
            webTerminateTokens[token] = (entry, Environment.TickCount64 + 120000);
            var image = WebImage(entry);
            return new { token, id = entry.Key, name = entry.Name, proc = Path.GetFileName(entry.Path), pid = entry.Pid, image = image.Image, darkPlate = image.DarkPlate, lightPlate = image.LightPlate };
        }
        if (WebReadOnlyPreview) throw new InvalidOperationException("Read-only preview.");
        if (webSettingsSaving) throw new IOException(L.T("ruleOperationBusy"));
        if (action == "visibilityUndo") return await RunWebVisibilityAsync(new(), false, undo: true);
        if (action == "visibilityRetry")
        {
            if (webVisibilityFailures.Count == 0) throw new InvalidOperationException(L.T("operationIncomplete"));
            return await RunWebVisibilityAsync(new(), false, retry: true);
        }
        if (action == "welcomeDismiss")
        {
            bool before = controller.Saved.WelcomeDismissed; controller.Saved.WelcomeDismissed = true;
            try { controller.Save(); } catch { controller.Saved.WelcomeDismissed = before; throw; }
        }
        else if (action == "trayStartup")
        {
            if (WebSettingsDirty) throw new IOException(L.Current == "en-US" ? "Save or discard your settings draft first." : "请先保存或放弃设置页的更改。");
            bool value = root.GetProperty("value").GetBoolean();
            EnsureUiContext(); busy = webForegroundOperation = true; PublishWebState(force: true);
            try { await Task.Run(() => startup.SetEnabled(value)); CacheStartup(value); }
            finally { webForegroundOperation = false; CompleteOperation(); }
        }
        else if (action == "traySelf")
        {
            if (WebSettingsDirty) throw new IOException(L.Current == "en-US" ? "Save or discard your settings draft first." : "请先保存或放弃设置页的更改。");
            bool before = controller.Saved.ShowTrayIcon; controller.Saved.ShowTrayIcon = root.GetProperty("value").GetBoolean();
            try { controller.Save(); } catch { controller.Saved.ShowTrayIcon = before; throw; }
            if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon;
        }
        else if (action == "terminate")
        {
            string token = root.GetProperty("token").GetString() ?? "";
            if (!webTerminateTokens.Remove(token, out var target) || target.Expires < Environment.TickCount64 || root.GetProperty("id").GetString() != target.Entry.Key)
                throw new InvalidDataException(L.T("processUnavailableMessage"));
            EnsureUiContext(); busy = webForegroundOperation = true; PublishWebState(force: true);
            try
            {
                if (WebTerminateForDiagnostics != null) await WebTerminateForDiagnostics(target.Entry); else await ProgramActions.EndAsync(target.Entry);
                controller.Saved.Recovery.RemoveAll(e => e.Pid == target.Entry.Pid && e.Started == target.Entry.Started);
                entries.RemoveAll(e => e.Pid == target.Entry.Pid && e.Started == target.Entry.Started);
                webVisibilityUndo.Clear(); RenderList();
                try { controller.Save(); }
                catch (Exception ex) { return new { ended = true, persistenceError = ex.Message }; }
            }
            finally { webForegroundOperation = false; CompleteOperation(); }
        }
        else if (action == "exitConfirm")
        {
            string token = root.GetProperty("token").GetString() ?? "";
            if (webExitToken == null || token != webExitToken || WebSettingsDirty) throw new InvalidDataException(L.T("operationIncomplete"));
            webExitToken = null;
            if (WebExitForDiagnostics != null) WebExitForDiagnostics();
            else { webExitConfirmed = true; HideWebTray(); exitRequested = true; BeginInvoke(Close); }
        }
        else throw new InvalidDataException("Unknown feedback action.");
        return null;
    }

    object PrepareWebExit()
    {
        if (WebSettingsDirty) throw new IOException(L.Current == "en-US" ? "Save or discard your settings draft first." : "请先保存或放弃设置页的更改。");
        webExitToken = Guid.NewGuid().ToString("N");
        return new { token = webExitToken, restore = controller.Saved.RestoreIconsOnExit, ordinary = controller.Saved.Recovery.Count, system = controller.Saved.HiddenSystemIcons != 0 };
    }
    void RequestHtmlExit()
    {
        HideWebTray(); OpenMainWindow();
        webInterface?.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "exitPrompt" }));
    }
    internal void CloseWebDiagnosticsWindow()
    { webExitConfirmed = exitRequested = true; controller.Saved.RestoreIconsOnExit = false; if (!webSettingsSaving) CancelWebSettingsChanges(); Close(); }
}
