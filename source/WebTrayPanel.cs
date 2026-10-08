using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace TrayPilot;

internal sealed partial class MainForm
{
    const string WebTrayDocument = "about:blank";
    TrayWebForm? webTrayPanel;
    WebView2? webTrayView;
    bool webTrayReady, webTrayInitializing;
    TaskCompletionSource<bool>? webTrayPresented;
    string? lastTrayState;
    int webTrayCapacity = 6;
    internal bool WebTrayKeepOpenForDiagnostics;

    Task? webTrayPreparation;
    bool webTrayRequested;

    Task PrepareWebTrayAsync()
    {
        if (webTrayPanel == null) webTrayPreparation = null;
        return webTrayPreparation ??= PrepareWebTrayCoreAsync();
    }

    async Task PrepareWebTrayCoreAsync()
    {
        if (closing || IsDisposed || webInterface?.CoreWebView2 == null) return;
        webTrayInitializing = true;
        var presented = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        webTrayPresented = presented;
        var panel = new TrayWebForm { BackColor = UiTheme.Canvas, Font = Font };
        webTrayPanel = panel;
        panel.Deactivate += (_, _) => { if (!WebTrayKeepOpenForDiagnostics && !busy && !webTrayInitializing) HideWebTray(); };
        panel.FormClosing += (_, e) => { if (!closing) { e.Cancel = true; HideWebTray(); } };
        // Prepare the controller in the existing host without displaying a popup.
        // Reparent this same control on first use; the browser and DOM are retained.
        var view = new WebView2 { Name = "trayWebView", Size = new(380, 708), Location = new(-4096, -4096), DefaultBackgroundColor = UiTheme.Canvas };
        webTrayView = view; Controls.Add(view); view.CreateControl();
        panel.Controls.Add(new Label { Name = "trayLoading", Dock = DockStyle.Fill, Text = "TrayPilot\n" + (L.Current == "en-US" ? "Preparing quick controls…" : "正在准备快捷面板…"), ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleCenter });
        try
        {
            await view.EnsureCoreWebView2Async(webInterface.CoreWebView2.Environment);
            if (closing || view.IsDisposed || !ReferenceEquals(view, webTrayView)) return;
            var core = view.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreDevToolsEnabled = WebReadOnlyPreview;
            string bootstrap = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(WebDocumentBytes("tray.html"));
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != bootstrap;
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => BlockWebResource(core.Environment, e);
            core.WebMessageReceived += OnTrayWebMessage;
            core.ProcessFailed += (_, _) => { HideWebTray(); DisposeWebTray(); };
            core.NavigateToString(System.Text.Encoding.UTF8.GetString(WebDocumentBytes("tray.html")));
            await presented.Task.WaitAsync(TimeSpan.FromSeconds(25));
        }
        catch (Exception ex)
        {
            if (!closing && !IsDisposed && ReferenceEquals(view, webTrayView)) { lastStatus = ex.Message; DisposeWebTray(); }
        }
        finally { webTrayInitializing = false; }
    }

    async Task ShowWebTrayAsync(Point anchor)
    {
        if (closing || IsDisposed) return;
        webTrayRequested = true;
        if (webInterface?.CoreWebView2 == null)
        {
            OpenMainWindow(); await EnableWebInterfaceAsync();
            if (!webReady || closing || IsDisposed) return;
        }
        var prepare = PrepareWebTrayAsync();
        var panel = webTrayPanel;
        var view = webTrayView;
        if (panel == null || view == null) { OpenMainWindow(); return; }
        var screen = Screen.FromPoint(anchor).WorkingArea;
        int scale = panel.DeviceDpi;
        int px(int value) => value * scale / 96;
        webTrayCapacity = Math.Clamp((int)((screen.Height / (scale / 96d) - 420) / 48), 1, TrayPageSize);
        panel.Size = new Size(Math.Min(px(380), screen.Width), Math.Min(px(420 + webTrayCapacity * 48), screen.Height));
        panel.Location = new Point(Math.Clamp(anchor.X - panel.Width, screen.Left, screen.Right - panel.Width), Math.Clamp(anchor.Y - panel.Height - px(8), screen.Top, screen.Bottom - panel.Height));
        if (view.Parent != panel) { panel.Controls.Add(view); view.Dock = DockStyle.Fill; }
        panel.Controls["trayLoading"]!.Visible = !webTrayReady;
        if (!webTrayReady) panel.Controls["trayLoading"]!.BringToFront(); else view.BringToFront();
        PublishTrayState(force: true);
        panel.Show(); panel.Activate(); Native.SetForegroundWindow(panel.Handle);
        await prepare;
        if (closing || IsDisposed) return;
        if (panel.IsDisposed)
        {
            OpenMainWindow();
            if (webReady) webInterface?.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "operationError", error = lastStatus }));
            return;
        }
        if (!webTrayRequested) return;
        PublishTrayState(force: true); _ = RefreshStartupMenuAsync();
        if (initialized && !busy) _ = RefreshAsync();
    }

    async void OnTrayWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != WebTrayDocument || sender != webTrayView?.CoreWebView2 || closing || IsDisposed) return;
        try
        {
            if (e.WebMessageAsJson.Length > 128000) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            string action = doc.RootElement.GetProperty("action").GetString() ?? "";
            if (action == "ready") { webTrayReady = true; PublishTrayState(force: true); return; }
            if (action == "presented")
            { if (webTrayReady) { webTrayPanel!.Controls["trayLoading"]?.Hide(); webTrayView!.Show(); webTrayView.BringToFront(); if (webTrayPanel.Visible) webTrayView.Focus(); webTrayPresented?.TrySetResult(true); } return; }
            if (action is not ("change" or "restore" or "refresh" or "navigate" or "rulePause" or "visibilityUndo" or "visibilityRetry" or "exitPrepare" or "exitConfirm" or "trayStartup" or "traySelf" or "trayClose" or "trayOpen"))
                throw new InvalidDataException("Unknown tray action.");
            await HandleWebMessageAsync(e.WebMessageAsJson, webTrayView!.CoreWebView2);
        }
        catch (Exception ex)
        {
            if (webTrayView?.CoreWebView2 != null)
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                string? id = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("requestId", out var request) && request.ValueKind == JsonValueKind.String ? request.GetString() : null;
                webTrayView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "result", requestId = id, success = false, error = ex.Message }));
            }
        }
    }
    void PublishTrayState(bool force = false)
    {
        if (!webTrayReady || webTrayView?.CoreWebView2 == null || closing || IsDisposed) return;
        var data = JsonSerializer.Serialize(new { type = "state", theme = UiTheme.Dark ? "dark" : "light", language = L.Current,
            busy = busy && webForegroundOperation || webSettingsSaving, paused = controller.Saved.RulesPaused, capacity = webTrayCapacity,
            startup = cachedStartup, startupError = startupQueryError, showTray = controller.Saved.ShowTrayIcon, operation = WebVisibilityState(),
            entries = WebEntries().Where(e => e.Pid != Environment.ProcessId).Select(e => { var image = WebImage(e); return new { id = e.Key, name = e.Name, path = e.Path, proc = Path.GetFileName(e.Path), tooltip = e.Tooltip, identity = IconRule.From(e).Label,
                hidden = e.State == 1, image = image.Image, darkPlate = image.DarkPlate, lightPlate = image.LightPlate }; }) });
        if (!force && lastTrayState == data) return;
        lastTrayState = data; webTrayView.CoreWebView2.PostWebMessageAsJson(data);
    }
    void HideWebTray() { webTrayRequested = false; webTrayPanel?.Hide(); }
    void DisposeWebTray()
    { webTrayRequested = false; webTrayPreparation = null; webTrayReady = false; lastTrayState = null; webTrayPresented?.TrySetCanceled(); webTrayPresented = null; webTrayView?.Dispose(); webTrayView = null; var panel = webTrayPanel; webTrayPanel = null; if (panel != null) { panel.Hide(); panel.Dispose(); } }
}

internal sealed class TrayWebForm : Form
{
    internal TrayWebForm()
    { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; Text = "TrayPilot"; AutoScaleMode = AutoScaleMode.Dpi; }
    protected override void OnShown(EventArgs e) { base.OnShown(e); UiTheme.Apply(this); }
}
