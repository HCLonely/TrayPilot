using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace TrayPilot;

internal sealed partial class MainForm
{
    const string WebDocument = "about:blank";
    WebView2? webInterface;
    WebStartupSurface? webStartupCover;
    bool webPresented;
    bool webReady, webPageActive = true;
    string webPage = "icons";
    bool WebRulesVisible => webReady && webPresented && webPageActive && webPage == "rules";
    bool webForegroundOperation;
    internal bool WebReadOnlyPreview;
    readonly Dictionary<string, (string Signature, string Image, bool DarkPlate, bool LightPlate)> webImages = new();
    string? lastWebState;
    readonly System.Diagnostics.Stopwatch webStartupWatch = System.Diagnostics.Stopwatch.StartNew();
    internal readonly Dictionary<string, double> WebStartupTimings = new();
    internal TaskCompletionSource<bool> WebLoaded { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void PrepareWebStartup()
    {
        if (webStartupCover != null || webPresented || closing || IsDisposed) return;
        webStartupCover = new WebStartupSurface { Dock = DockStyle.Fill, AccessibleRole = AccessibleRole.StatusBar,
            AccessibleName = L.Current == "en-US" ? "Loading TrayPilot" : "正在加载 TrayPilot" };
        Controls.Add(webStartupCover); webStartupCover.BringToFront();
    }

    void RemoveWebStartupCover()
    {
        if (webStartupCover == null) return;
        Controls.Remove(webStartupCover); webStartupCover.Dispose(); webStartupCover = null;
    }

    void ShowWebStartupError(Exception error)
    {
        DisposeWebTray();
        DeactivateSystemPage();
        webReady = webPresented = false;
        if (WebSettingsDirty && !webSettingsSaving) CancelWebSettingsChanges();
        webInterface?.Hide();
        PrepareWebStartup();
        webStartupCover!.Error = error.Message;
        webStartupCover.Invalidate();
        if (webStartupCover.Controls.Count == 0)
        {
            var retry = new Button { Text = L.Current == "en-US" ? "Retry" : "重试", AutoSize = true, Anchor = AnchorStyles.None };
            retry.Click += async (_, _) =>
            {
                DisposeWebInterface(); WebLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await EnableWebInterfaceAsync();
            };
            webStartupCover.Controls.Add(retry);
            webStartupCover.Resize += (_, _) => retry.Location = new((webStartupCover.Width - retry.Width) / 2, webStartupCover.Height / 2 + 95);
            retry.Location = new((webStartupCover.Width - retry.Width) / 2, webStartupCover.Height / 2 + 95);
        }
        lastStatus = "WebView2: " + error.Message;
        WebLoaded.TrySetException(error);
    }

    internal async Task EnableWebInterfaceAsync()
    {
        if (webInterface != null || closing || IsDisposed) return;
        PrepareWebStartup();
        // Chromium can paint while the themed startup surface covers its first frame.
        var view = new WebView2 { Name = "mainWebView", Dock = DockStyle.Fill, DefaultBackgroundColor = UiTheme.Canvas };
        webInterface = view;
        Controls.Add(view); view.BringToFront(); webStartupCover?.BringToFront();
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayPilot", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            WebStartupTimings["environmentMs"] = webStartupWatch.Elapsed.TotalMilliseconds;
            if (IsDisposed || view.IsDisposed) return;
            await view.EnsureCoreWebView2Async(environment);
            WebStartupTimings["controllerMs"] = webStartupWatch.Elapsed.TotalMilliseconds;
            if (IsDisposed || view.IsDisposed) return;
            var core = view.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = WebReadOnlyPreview;
            core.Settings.IsWebMessageEnabled = true;
            string bootstrap = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(WebDocumentBytes("index.html"));
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != bootstrap;
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => BlockWebResource(core.Environment, e);
            core.WebMessageReceived += OnWebMessage;
            core.ProcessFailed += (_, e) =>
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                { ShowWebStartupError(new IOException(L.T("operationIncomplete") + ": WebView2")); }
            };
            core.NavigateToString(Encoding.UTF8.GetString(WebDocumentBytes("index.html")));
            _ = PrepareWebTrayAsync();
            await WebLoaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
        }
        catch (Exception ex)
        {
            if (!closing && !IsDisposed) ShowWebStartupError(ex);
            if (!view.IsDisposed) { Controls.Remove(view); view.Dispose(); }
            webInterface = null;
        }
    }

    static void BlockWebResource(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // All page resources are supplied from memory; browser requests need no network access.
        e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "Content-Type: text/plain\r\nCache-Control: no-store");
    }

    static readonly Dictionary<string, byte[]> webDocuments = new();
    internal static byte[] WebDocumentBytes(string file)
    {
        if (webDocuments.TryGetValue(file, out var cached)) return cached;
        static string Read(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayPilot.web." + name)
                ?? throw new IOException("Missing embedded web resource: " + name);
            using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd().Replace("\r\n", "\n").Replace('\r', '\n');
        }
        var cssHashes = new List<string>(); var jsHashes = new List<string>();
        string Hash(string content) => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content))) + "'";
        string html = Regex.Replace(Read(file), "<link rel=\"stylesheet\" href=\"([a-z]+\\.css)\">", match =>
        {
            string css = Read(match.Groups[1].Value); cssHashes.Add(Hash(css)); return "<style>" + css + "</style>";
        });
        html = Regex.Replace(html, "<script src=\"([a-z]+\\.js)\"></script>", match =>
        {
            string script = Read(match.Groups[1].Value); jsHashes.Add(Hash(script)); return "<script>" + script + "</script>";
        });
        html = html.Replace("style-src 'self'", "style-src 'self' " + string.Join(" ", cssHashes));
        if (file == "tray.html") html = html.Replace("script-src 'self'", "script-src 'self' " + string.Join(" ", jsHashes));
        cached = Encoding.UTF8.GetBytes(html);
        if (cached.Length >= 2 * 1024 * 1024) throw new IOException("Embedded page exceeds the WebView2 document limit.");
        webDocuments[file] = cached; return cached;
    }

    void SetWebPageVisibility(bool active, string page = "icons")
    {
        webPageActive = active;
        if (active) webPage = page;
        if (!webReady || !webPresented || webInterface == null || webInterface.IsDisposed) return;
        webInterface.Visible = active;
        if (active) { webInterface.BringToFront(); PublishWebState(force: true); }
    }

    (string Image, bool DarkPlate, bool LightPlate) WebImage(TrayEntry entry)
    {
        string signature = entry.Path + ":" + entry.Started + ":" + (entry.IconSnapshot is { Length: > 0 } bytes ? Convert.ToHexString(SHA256.HashData(bytes)) : "file");
        if (webImages.TryGetValue(entry.Key, out var cached) && cached.Signature == signature) return (cached.Image, cached.DarkPlate, cached.LightPlate);
        using var bitmap = TrayImages.Create(entry with { State = 0 }, 64);
        int opaque = 0, white = 0, black = 0;
        for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A < 64) continue;
            opaque++; if (pixel.R > 210 && pixel.G > 210 && pixel.B > 210) white++;
            if (pixel.R < 70 && pixel.G < 70 && pixel.B < 70) black++;
        }
        bool darkPlate = opaque > 0 && white > opaque * .8;
        bool lightPlate = opaque > 0 && black > opaque * .8;
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        var image = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        webImages[entry.Key] = (signature, image, darkPlate, lightPlate);
        return (image, darkPlate, lightPlate);
    }

    void PublishWebState(bool force = false)
    {
        if (!webReady || webInterface?.CoreWebView2 == null || webInterface.IsDisposed || IsDisposed || closing) return;
        var webEntries = WebEntries();
        var ruleRecords = WebRuleRecords(webEntries);
        var live = webEntries.Select(e => e.Key).Concat(RuleEditing.Catalog(controller).Select(WebRuleImageKey)).ToHashSet();
        foreach (var key in webImages.Keys.Where(k => !live.Contains(k)).ToArray()) webImages.Remove(key);
        var state = JsonSerializer.Serialize(new
        {
            type = "state", page = webPage, language = L.Current, theme = UiTheme.Dark ? "dark" : "light", busy = busy && webForegroundOperation,
            paused = controller.Saved.RulesPaused, autoRefresh = autoRefreshEnabled,
            rules = controller.Saved.HiddenPaths.Count + controller.Saved.HiddenIcons.Count,
            ruleCatalog = ruleRecords, ruleUndo = CanUndoWebRules(), system = WebSystemState(), settings = WebSettingsState(), about = WebAboutState(),
            operation = WebVisibilityState(), welcome = initialized && !controller.Saved.WelcomeDismissed && controller.Saved.HiddenPaths.Count == 0 && controller.Saved.HiddenIcons.Count == 0 && controller.Saved.Recovery.Count == 0,
            entries = webEntries.Select(e =>
            {
                var image = WebImage(e);
                return new { id = e.Key, name = e.Name, proc = Path.GetFileName(e.Path), path = e.Path,
                    tooltip = e.Tooltip, identity = IconRule.From(e).Label, pid = e.Pid, hidden = e.State == 1, image = image.Image, darkPlate = image.DarkPlate, lightPlate = image.LightPlate,
                    rule = controller.HasRule(e) ? controller.HasRule(e.Path) ? "program" : "single" : "", temporary = controller.IsTemporarilyShown(e), canTerminate = e.Pid != Environment.ProcessId && !Scanner.IsShellEntry(e) };
            })
        });
        if (!force && state == lastWebState) return;
        lastWebState = state; webInterface.CoreWebView2.PostWebMessageAsJson(state); PublishTrayState();
    }

    async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebStartupTimings.TryAdd("message:" + e.Source, webStartupWatch.Elapsed.TotalMilliseconds);
        if (e.Source != WebDocument || sender != webInterface?.CoreWebView2 || closing || IsDisposed) return;
        await HandleWebMessageAsync(e.WebMessageAsJson, webInterface!.CoreWebView2);
    }

    async Task HandleWebMessageAsync(string json, CoreWebView2 responseCore)
    {
        string? requestId = null;
        void Reply(string? id, bool success, string error = "", object? data = null)
        { if (!closing && !IsDisposed) responseCore.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "result", requestId = id, success, error, data })); }
        try
        {
            if (json.Length > 128_000) throw new InvalidDataException("Message too large.");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string action = root.GetProperty("action").GetString() ?? "";
            requestId = root.TryGetProperty("requestId", out var request) ? request.GetString() : null;
            if (action == "ready")
            {
                WebStartupTimings["documentMs"] = webStartupWatch.Elapsed.TotalMilliseconds;
                webReady = true; PublishWebState(force: true);
                WebStartupTimings["firstStateMs"] = webStartupWatch.Elapsed.TotalMilliseconds; return;
            }
            if (action == "presented" && webReady)
            {
                WebStartupTimings["presentedMs"] = webStartupWatch.Elapsed.TotalMilliseconds;
                webPresented = true;
                if (webPage == "settings" && webSettingsDraft == null) StartWebSettingsPage();
                SetWebPageVisibility(webPageActive, webPage); RemoveWebStartupCover(); WebLoaded.TrySetResult(true); return;
            }
            // Let an in-flight background scan finish before a user command. A
            // periodic scan should not flash disabled controls or reject a click.
            for (int i = 0; busy && !webForegroundOperation && i < 40 && !closing && !IsDisposed; i++) await Task.Delay(50);
            if (closing || IsDisposed) return;
            if (busy) throw new IOException(L.T("ruleOperationBusy"));
            if (action == "navigate")
            {
                HideWebTray();
                switch (root.GetProperty("page").GetString())
                { case "icons": ShowIconsPage(); break; case "rules": ShowRulesPage(); break; case "system": ShowSystemIcons(); break; case "settings": ShowSettings(); break; case "about": ShowAbout(); break; default: throw new InvalidDataException("Unknown page."); }
            }
            else if (action == "theme")
            {
                string theme = root.GetProperty("value").GetString() ?? "";
                if (theme is not ("light" or "dark" or "system")) throw new InvalidDataException("Unknown theme.");
                string previous = controller.Saved.Theme;
                controller.Saved.Theme = theme;
                try { if (!WebReadOnlyPreview) controller.Save(); } catch { controller.Saved.Theme = previous; throw; }
                ApplyTheme();
            }
            else if (action == "copyPath" || action == "properties" || action == "rule")
            {
                var entry = ResolveWebEntry(root.GetProperty("id").GetString());
                if (action == "copyPath") Clipboard.SetText(entry.Path);
                if (action == "properties") { Reply(requestId, true, data: new { rows = ProgramActions.Properties(entry, controller.HasRule(entry)), name = entry.Name }); return; }
                if (action == "rule")
                {
                    ShowRulesPage();
                    webInterface?.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "ruleTarget", id = entry.Key,
                        ruleId = RuleEditing.Catalog(controller).FirstOrDefault(r => r.Matches(entry))?.Key }));
                }
            }
            else if (action is "settingsDraft" or "settingsSave" or "settingsCancel" or "settingsStartupRetry" or "aboutCheck" or "aboutLink" or "aboutCopy")
            {
                var data = await ExecuteWebPreferenceCommand(action, root);
                PublishWebState(force: true); Reply(requestId, true, data: data); return;
            }
            else if (action is "ruleValidate" or "ruleSave" or "ruleDelete" or "rulePause" or "ruleApply" or "ruleTemporary" or "ruleUndo")
            {
                var data = await ExecuteWebRuleCommand(action, root);
                PublishWebState(force: true); Reply(requestId, true, data: data); return;
            }
            else if (action is "systemToggle" or "systemRestore" or "systemUndo" or "systemScheme" or "systemReconnect")
            {
                var data = await ExecuteWebSystemCommand(action, root);
                PublishWebState(force: true); Reply(requestId, true, data: data); return;
            }
            else if (action is "visibilityUndo" or "visibilityRetry" or "terminatePrepare" or "terminate" or "exitPrepare" or "exitConfirm" or "welcomeDismiss" or "trayStartup" or "traySelf" or "trayClose" or "trayOpen")
            { var data = await ExecuteWebFeedbackCommand(action, root); PublishWebState(force: true); Reply(requestId, true, data: data); return; }
            else if (action == "autoRefresh") { autoRefreshEnabled = root.GetProperty("value").GetBoolean(); UpdateTimer(); }
            else if (action is "change" or "restore" or "refresh")
            {
                if (WebReadOnlyPreview && action != "refresh") throw new InvalidOperationException("Read-only preview.");
                if (WebReadOnlyPreview) { PublishWebState(force: true); Reply(requestId, true); return; }
                var targets = new List<TrayEntry>();
                bool hidden = false;
                if (action == "change")
                {
                    var ids = root.GetProperty("ids");
                    if (ids.GetArrayLength() is < 1 or > 512) throw new InvalidDataException("Invalid target count.");
                    targets = ids.EnumerateArray().Select(id => ResolveWebEntry(id.GetString())).DistinctBy(x => x.Key).ToList();
                    hidden = root.GetProperty("hidden").GetBoolean();
                }
                if (action is "change" or "restore")
                { var data = await RunWebVisibilityAsync(action == "restore" ? controller.Saved.Recovery.ToList() : targets, action == "restore" ? false : hidden); PublishWebState(force: true); Reply(requestId, true, data: data); return; }
                EnsureUiContext(); busy = true; webForegroundOperation = false; PublishWebState();
                try
                {
                    if (action == "change") await ChangeEntriesAsync(targets, hidden);
                    else if (action == "restore") await controller.RestoreManagedAsync(temporarilyShow: true);
                    await RefreshSnapshotAsync(true, operationOwned: true);
                }
                finally { webForegroundOperation = false; UpdateTimer(); CompleteOperation(); }
            }
            else throw new InvalidDataException("Unknown action.");
            PublishWebState(force: true); Reply(requestId, true);
        }
        catch (RuleSavedException ex)
        {
            if (closing || IsDisposed) return;
            PublishWebState(force: true);
            Reply(requestId, false, L.T("ruleSavedApplyFailed") + " " + ex.InnerException?.Message, new { id = ex.Rule.Key, saved = true });
        }
        catch (Exception ex)
        {
            if (closing || IsDisposed) return;
            if (webReady) { PublishWebState(force: true); Reply(requestId, false, ex.Message); }
        }
    }

    // A hidden icon can disappear from the shell's discovery snapshot. Keep its
    // verified recovery identity addressable until it is restored or its owner exits.
    List<TrayEntry> WebEntries() => entries.Concat(controller.Saved.Recovery
        .Where(e => Scanner.SameOwner(e))
        .Select(e => e with { State = Native.State(e) }).Where(e => e.State is 0 or 1))
        .DistinctBy(e => e.Key).ToList();

    TrayEntry ResolveWebEntry(string? key) => WebEntries().FirstOrDefault(e => e.Key == key)
        ?? throw new IOException(L.T("processUnavailableMessage"));

    void ReplyWeb(string? id, bool success, string error = "", object? data = null)
    {
        if (webReady && webInterface?.CoreWebView2 != null && !webInterface.IsDisposed && !closing)
            webInterface.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "result", requestId = id, success, error, data }));
    }

    void DisposeWebInterface()
    {
        DisposeWebTray(); CancelWebUpdate(); webStartupGeneration++;
        webReady = webPresented = false; webImages.Clear(); webSystemPoll.Dispose(); webSystemImages.Clear(); RemoveWebStartupCover();
        webInterface?.Dispose(); webInterface = null;
    }
}

// Opaque, theme-matched first paint and recoverable startup errors.
internal sealed class WebStartupSurface : Panel
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 30 };
    string? error;
    bool animationAllowed = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? Error
    {
        get => error;
        set { error = value; UpdateAnimation(); Invalidate(); }
    }
    internal WebStartupSurface()
    {
        DoubleBuffered = true; ResizeRedraw = true; BackColor = UiTheme.Canvas;
        AccessibleDescription = L.Current == "en-US" ? "Loading your workspace" : "正在加载工作空间";
        animation.Tick += (_, _) => Invalidate(SpinnerBounds(inflate: true));
    }
    Rectangle SpinnerBounds(bool inflate = false)
    {
        int scale(int value) => value * DeviceDpi / 96;
        int top = Math.Max(scale(32), (Height - scale(130)) / 2);
        var bounds = new Rectangle((Width - scale(22)) / 2, top + scale(105), scale(22), scale(22));
        if (inflate) bounds.Inflate(scale(4), scale(4));
        return bounds;
    }
    void UpdateAnimation()
    {
        animationAllowed = !Native.GetSystemAnimationSetting(0x1042, 0, out int enabled, 0) || enabled != 0;
        animation.Enabled = Visible && !IsDisposed && error == null && animationAllowed;
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 1 || Height < 1) return;
        using var backdrop = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, UiTheme.Header, UiTheme.Canvas, 35f);
        e.Graphics.FillRectangle(backdrop, ClientRectangle);
        int scale(int value) => value * DeviceDpi / 96;
        int center = Width / 2, top = Math.Max(scale(32), (Height - scale(130)) / 2);
        if (e.ClipRectangle.Top < top + scale(100))
        {
            using var logo = AppIcon.Draw(scale(48));
            e.Graphics.DrawImage(logo, center - scale(24), top, scale(48), scale(48));
            using var title = new Font(Font.FontFamily, 17, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "TrayPilot", title, new Rectangle(0, top + scale(62), Width, scale(34)), UiTheme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        if (error != null)
        {
            using var caption = new Font(Font.FontFamily, 10);
            TextRenderer.DrawText(e.Graphics, error, caption, new Rectangle(0, top + scale(101), Width, scale(28)), UiTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var track = new Pen(Color.FromArgb(40, UiTheme.Muted), Math.Max(2, scale(2)));
        using var arc = new Pen(UiTheme.Dark ? Color.FromArgb(156, 187, 255) : Color.FromArgb(36, 88, 211), Math.Max(2, scale(2)))
        { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        var ring = SpinnerBounds();
        e.Graphics.DrawEllipse(track, ring);
        float angle = animationAllowed ? Environment.TickCount64 % 1000 * .36f - 90 : -90;
        e.Graphics.DrawArc(arc, ring, angle, 100);
    }
}
