using System.Drawing.Imaging;
using System.Text.Json;

namespace TrayPilot;

internal sealed partial class MainForm
{
    readonly System.Windows.Forms.Timer webSystemPoll = new() { Interval = 1000 };
    readonly Dictionary<int, (string Signature, string Image)> webSystemImages = new();
    bool webSystemWorking;
    bool webSystemReconnecting;
    string webSystemError = "";
    int? webSystemUndo;
    int webSystemUndoExpected;
    internal Func<SystemIconsSnapshot>? WebSystemReadForDiagnostics;
    internal Func<int, int, Task<SystemIconsSnapshot>>? WebSystemChangeForDiagnostics;
    internal Func<bool?, Task<SystemIconsSnapshot>>? WebSystemConnectForDiagnostics;
    bool WebSystemVisible => webReady && webPresented && webPageActive && webPage == "system";

    SystemIconsSnapshot ReadWebSystemSnapshot() => WebSystemReadForDiagnostics?.Invoke() ?? ReadSystemIconsSnapshot();

    object WebSystemState()
    {
        var snapshot = ReadWebSystemSnapshot();
        if (webSystemReconnecting) snapshot = snapshot with { Connecting = true };
        if (webSystemUndo.HasValue && snapshot.Requested != webSystemUndoExpected) webSystemUndo = null;
        return new
        {
            connected = snapshot.Connected, connecting = snapshot.Connecting, working = webSystemWorking,
            requested = snapshot.Requested, found = snapshot.Found, hidden = snapshot.Hidden,
            shared = snapshot.Shared, legacy = snapshot.Legacy, error = webSystemError.Length > 0 ? webSystemError : snapshot.Error,
            diagnostic = snapshot.Diagnostic, undo = webSystemUndo.HasValue,
            items = SystemIconCatalog.Items.Select((item, index) =>
            {
                var appearance = index < snapshot.Appearances.Length ? snapshot.Appearances[index] : default;
                string signature = appearance.Text + "|" + appearance.Font;
                if (!webSystemImages.TryGetValue(item.Mask, out var cached) || cached.Signature != signature)
                {
                    using var bitmap = SystemIconImages.Create(item.Mask, 64, appearance);
                    using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
                    cached = (signature, "data:image/png;base64," + Convert.ToBase64String(stream.ToArray()));
                    webSystemImages[item.Mask] = cached;
                }
                return new { bit = item.Mask, name = L.T(item.Name), description = L.T("systemPageDescription" + item.Mask),
                    group = item.Mask <= 8 ? "common" : item.Mask is 256 or 512 or 2048 ? "taskbar" : "indicators",
                    state = snapshot.StateKey(item.Mask), status = L.T(snapshot.StateKey(item.Mask)), image = cached.Image };
            })
        };
    }

    void StartWebSystemPage()
    {
        systemIconDialogOpen = Visible && WindowState != FormWindowState.Minimized;
        systemIconSession?.SetAppearanceCapture(systemIconDialogOpen);
        webSystemPoll.Stop();
        webSystemPoll.Tick -= PollWebSystem;
        webSystemPoll.Tick += PollWebSystem;
        if (systemIconDialogOpen) webSystemPoll.Start();
        if (initialized && systemIconDialogOpen) _ = EnsureWebSystemConnectedAsync();
    }

    void PollWebSystem(object? sender, EventArgs e)
    {
        if (WebSystemVisible && systemIconDialogOpen && !webSystemWorking) PublishWebState();
    }

    async Task EnsureWebSystemConnectedAsync()
    {
        if (webSystemWorking || systemIconsConnecting || systemIconsChanging || closing || ReadWebSystemSnapshot().Connected) return;
        webSystemWorking = webSystemReconnecting = true; webSystemError = ""; PublishWebState();
        try { await ReconnectSystemIconsPageAsync(); }
        catch (Exception ex) { if (!closing && !IsDisposed) webSystemError = ex.Message; }
        finally { webSystemWorking = webSystemReconnecting = false; PublishWebState(); }
    }

    async Task<object?> ExecuteWebSystemCommand(string action, JsonElement root)
    {
        if (WebReadOnlyPreview) throw new InvalidOperationException("Read-only preview.");
        if (!WebSystemVisible || webSystemWorking || systemIconsChanging || systemIconsConnecting) throw new IOException(L.T("ruleOperationBusy"));
        var before = ReadWebSystemSnapshot();
        int mask = before.Requested, restore = 0;
        bool? legacy = null;
        if (action == "systemToggle")
        {
            int bit = root.GetProperty("bit").GetInt32();
            if (!SystemIconCatalog.Items.Any(item => item.Mask == bit)) throw new InvalidDataException("Unknown system control.");
            bool show = root.GetProperty("show").GetBoolean();
            mask = show ? mask & ~bit : mask | bit; restore = show ? bit : 0;
        }
        else if (action == "systemRestore") { mask = 0; restore = SystemIconCatalog.All; }
        else if (action == "systemUndo")
        {
            if (!webSystemUndo.HasValue || webSystemUndoExpected != before.Requested) throw new IOException(L.T("ruleOperationBusy"));
            mask = webSystemUndo.Value; restore = before.Requested & ~mask;
        }
        else if (action == "systemScheme") legacy = root.GetProperty("legacy").GetBoolean();
        else if (action != "systemReconnect") throw new InvalidDataException("Unknown system action.");
        bool reconnecting = action is "systemScheme" or "systemReconnect";
        if (!reconnecting && (!before.Connected || before.Connecting)) throw new IOException(L.T("systemNativeUnavailable"));
        EnsureUiContext(); busy = true; webForegroundOperation = true; webSystemWorking = true; webSystemReconnecting = reconnecting; webSystemError = ""; PublishWebState();
        try
        {
            var result = reconnecting
                ? WebSystemConnectForDiagnostics != null ? await WebSystemConnectForDiagnostics(legacy)
                    : legacy.HasValue ? await ChangeSystemDiscoveryPageAsync(legacy.Value) : await ReconnectSystemIconsPageAsync()
                : WebSystemChangeForDiagnostics != null ? await WebSystemChangeForDiagnostics(mask, restore) : await ChangeSystemIconsPageAsync(mask, restore);
            if (!reconnecting)
            {
                webSystemUndo = action == "systemUndo" ? null : before.Requested;
                webSystemUndoExpected = result.Requested;
            }
            return null;
        }
        catch (Exception ex) { webSystemError = ex.Message; throw; }
        finally { webSystemWorking = webSystemReconnecting = false; webForegroundOperation = false; UpdateTimer(); CompleteOperation(); }
    }
}
