using System.Text.Json;

namespace TrayPilot;

internal sealed partial class MainForm
{
    SettingsPreferences? webSettingsBaseline, webSettingsDraft;
    long webSettingsVersion;
    bool webSettingsSaving, webStartupLoading;
    string webSettingsError = "", webStartupError = "";
    int webStartupGeneration, webUpdateGeneration;
    string webUpdateState = "idle";
    UpdateChecker.Release? webUpdateRelease;
    CancellationTokenSource? webUpdateCancellation;
    internal Func<Task<bool>>? WebStartupReadForDiagnostics;
    internal Func<CancellationToken, Task<UpdateChecker.Release?>>? WebUpdateCheckForDiagnostics;
    internal Action<string>? WebOpenLinkForDiagnostics;
    internal Func<Task>? WebSettingsBeforeSaveForDiagnostics;
    bool WebSettingsVisible => webReady && webPresented && webPageActive && webPage == "settings";
    bool WebSettingsDirty => webSettingsDraft != null && webSettingsDraft != webSettingsBaseline;

    void PreviewSettingsTheme(string mode) => ApplyTheme();
    void SettingsApplied(SettingsPreferences before, SettingsPreferences after)
    {
        if (after.Language != L.Current) { L.Set(after.Language); ApplyLanguage(); }
        if (after.Theme != before.Theme) ApplyTheme();
        UpdateStatus();
    }
    async Task SaveSettingsPreferencesAsync(SettingsPreferences draft, bool changeStartup)
    {
        if (busy || closing) throw new IOException(L.T("ruleOperationBusy"));
        EnsureUiContext(); busy = true;
        try
        {
            await Task.Yield();
            SettingsTransaction.Save(controller, draft, changeStartup, startup, () => { RegisterShortcuts(); return !hotkeyWarning; });
            if (trayIcon != null) trayIcon.Visible = controller.Saved.ShowTrayIcon;
            if (draft.Startup.HasValue) CacheStartup(draft.Startup.Value);
        }
        finally { CompleteOperation(); }
    }

    static object WebPreferenceValue(SettingsPreferences value) => new
    {
        language = value.Language, theme = value.Theme, closeToTray = value.CloseToTray, showTrayIcon = value.ShowTrayIcon,
        restoreOnExit = value.RestoreOnExit, startup = value.Startup,
        hotkeys = value.Hotkeys.Select(h => new { enabled = h.Enabled, keys = (int)h.Keys, text = new KeysConverter().ConvertToString(h.Keys) ?? h.Keys.ToString() })
    };

    object? WebSettingsState() => webSettingsDraft == null || webSettingsBaseline == null ? null : new
    {
        baseline = WebPreferenceValue(webSettingsBaseline), draft = WebPreferenceValue(webSettingsDraft), version = webSettingsVersion,
        dirty = WebSettingsDirty, saving = webSettingsSaving, error = webSettingsError,
        startupLoading = webStartupLoading, startupError = webStartupError,
        invalid = webSettingsDraft.InvalidHotkeys,
        languages = L.Packs().Select(p => new { code = p.Key, name = p.Value.Name })
    };

    object WebAboutState() => new
    {
        version = UpdateChecker.CurrentVersionText, updateState = webUpdateState,
        release = webUpdateRelease?.Tag ?? "", download = webUpdateState == "new",
        diagnostic = "TrayPilot " + UpdateChecker.CurrentVersionText + "\nHCLonely\n" + UpdateChecker.ProjectUrl
            + "\n\n" + L.T("operatingSystem") + ": " + Environment.OSVersion.VersionString
            + "\n" + L.T("applicationPath") + ": " + (Environment.ProcessPath ?? AppContext.BaseDirectory)
            + "\n" + L.T("settingsFolder") + ": " + controller.SettingsFolder
    };

    void StartWebSettingsPage()
    {
        if (webSettingsSaving) return;
        if (!WebSettingsDirty)
        {
            webSettingsBaseline = webSettingsDraft = SettingsPreferences.Read(controller.Saved, cachedStartup);
            webSettingsVersion++; webSettingsError = "";
        }
        if (!webStartupLoading) _ = ReadWebStartupAsync();
    }

    async Task ReadWebStartupAsync()
    {
        int generation = ++webStartupGeneration;
        webStartupLoading = true; webStartupError = ""; PublishWebState();
        try
        {
            bool enabled = WebStartupReadForDiagnostics != null ? await WebStartupReadForDiagnostics() : await Task.Run(() => startup.Enabled);
            if (closing || IsDisposed || generation != webStartupGeneration) return;
            CacheStartup(enabled);
            bool unchanged = webSettingsDraft?.Startup == webSettingsBaseline?.Startup;
            if (webSettingsBaseline != null) webSettingsBaseline = webSettingsBaseline with { Startup = enabled };
            if (unchanged && webSettingsDraft != null) webSettingsDraft = webSettingsDraft with { Startup = enabled };
        }
        catch (Exception ex)
        {
            if (closing || IsDisposed || generation != webStartupGeneration) return;
            webStartupError = ex.Message; CacheStartup(null, ex.Message);
            if (webSettingsBaseline != null) webSettingsBaseline = webSettingsBaseline with { Startup = null };
            if (webSettingsDraft != null) webSettingsDraft = webSettingsDraft with { Startup = null };
        }
        finally
        {
            if (!closing && !IsDisposed && generation == webStartupGeneration) { webStartupLoading = false; PublishWebState(force: true); }
        }
    }

    SettingsPreferences ParseWebPreferences(JsonElement value)
    {
        string language = value.GetProperty("language").GetString() ?? "";
        string theme = value.GetProperty("theme").GetString() ?? "";
        if (!L.Packs().Any(p => p.Key == language) || theme is not ("light" or "dark" or "system")) throw new InvalidDataException("Unknown preference value.");
        var hotkeys = value.GetProperty("hotkeys");
        if (hotkeys.ValueKind != JsonValueKind.Array || hotkeys.GetArrayLength() != 3) throw new InvalidDataException("Invalid shortcut count.");
        var keys = hotkeys.EnumerateArray().Select(h =>
        {
            int key = h.GetProperty("keys").GetInt32();
            if ((key & ~((int)Keys.KeyCode | (int)Keys.Control | (int)Keys.Alt | (int)Keys.Shift)) != 0 || (key & (int)Keys.KeyCode) > 254)
                throw new InvalidDataException("Invalid shortcut code.");
            return (Enabled: h.GetProperty("enabled").GetBoolean(), Key: (Keys)key);
        }).ToArray();
        var startupValue = value.GetProperty("startup");
        bool? enabled = startupValue.ValueKind == JsonValueKind.Null ? null : startupValue.GetBoolean();
        if (webSettingsBaseline?.Startup == null && enabled.HasValue) throw new IOException(L.T("settingsStartupUnavailable"));
        if (webSettingsBaseline?.Startup != null && !enabled.HasValue) enabled = webSettingsBaseline.Startup;
        return new(language, theme, value.GetProperty("closeToTray").GetBoolean(), value.GetProperty("showTrayIcon").GetBoolean(),
            value.GetProperty("restoreOnExit").GetBoolean(), enabled, keys[0].Enabled, keys[0].Key, keys[1].Enabled, keys[1].Key, keys[2].Enabled, keys[2].Key);
    }

    void CancelWebSettingsChanges()
    {
        if (webSettingsSaving || webSettingsBaseline == null) return;
        webSettingsDraft = webSettingsBaseline; webSettingsVersion++; webSettingsError = "";
        PreviewSettingsTheme(webSettingsBaseline.Theme);
    }

    bool RequestWebSettingsLeave(string destination)
    {
        if (initialized && (!Visible || WindowState == FormWindowState.Minimized)) OpenMainWindow();
        webInterface?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settingsLeave", page = destination }));
        return false;
    }

    async Task<object?> ExecuteWebPreferenceCommand(string action, JsonElement root)
    {
        if (action.StartsWith("settings", StringComparison.Ordinal))
        {
            if (!WebSettingsVisible || webSettingsSaving || webSettingsDraft == null || webSettingsBaseline == null) throw new IOException(L.T("ruleOperationBusy"));
            if (action == "settingsStartupRetry") { if (!webStartupLoading) await ReadWebStartupAsync(); return null; }
            if (action == "settingsCancel") { CancelWebSettingsChanges(); return null; }
            if (action is not ("settingsDraft" or "settingsSave")) throw new InvalidDataException("Unknown settings action.");
            var submitted = ParseWebPreferences(root.GetProperty("value"));
            long version = root.GetProperty("version").GetInt64();
            if (version < 0 || version > 1_000_000_000) throw new InvalidDataException("Invalid draft version.");
            if (version < webSettingsVersion) { if (action == "settingsSave") throw new IOException(L.T("settingsUnsaved")); return null; }
            string previousTheme = webSettingsDraft.Theme;
            webSettingsVersion = version; webSettingsDraft = submitted; webSettingsError = "";
            if (action == "settingsDraft") { if (previousTheme != submitted.Theme) PreviewSettingsTheme(submitted.Theme); return null; }
            if (WebReadOnlyPreview) throw new InvalidOperationException("Read-only preview.");
            if (webStartupLoading) throw new IOException(L.T("settingsStartupLoading"));
            if (submitted.InvalidHotkeys.Length > 0) throw new IOException(L.T("settingsHotkeyConflict"));
            var before = webSettingsBaseline;
            webSettingsSaving = true; webForegroundOperation = true; PublishWebState();
            try
            {
                if (WebSettingsBeforeSaveForDiagnostics != null) await WebSettingsBeforeSaveForDiagnostics();
                await SaveSettingsPreferencesAsync(submitted, submitted.Startup != before.Startup);
                webSettingsBaseline = webSettingsDraft = submitted; webSettingsError = "";
                SettingsApplied(before, submitted);
            }
            catch (Exception ex) { webSettingsError = ex.Message; throw; }
            finally { webSettingsSaving = false; webForegroundOperation = false; PublishWebState(force: true); }
            return null;
        }
        if (action == "aboutCheck")
        {
            if (WebReadOnlyPreview || webPage != "about" || !webPageActive || webUpdateState == "checking") throw new IOException(L.T("ruleOperationBusy"));
            await CheckWebUpdateAsync(); return null;
        }
        if (action == "aboutLink")
        {
            if (WebReadOnlyPreview || webPage != "about" || !webPageActive) throw new InvalidOperationException("Read-only or inactive page.");
            string url = root.GetProperty("kind").GetString() switch
            {
                "project" => UpdateChecker.ProjectUrl, "usage" => UpdateChecker.ProjectUrl + "#readme",
                "issues" => UpdateChecker.ProjectUrl + "/issues", "releases" => UpdateChecker.ProjectUrl + "/releases",
                "credits" => "https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-tray-system-icon-tweaks.wh.cpp",
                "download" when webUpdateState == "new" && webUpdateRelease != null => webUpdateRelease.Url,
                _ => throw new InvalidDataException("Unknown link.")
            };
            if (WebOpenLinkForDiagnostics != null) WebOpenLinkForDiagnostics(url);
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        if (action == "aboutCopy")
        {
            if (WebReadOnlyPreview || webPage != "about" || !webPageActive) throw new InvalidOperationException("Read-only or inactive page.");
            using var value = JsonDocument.Parse(JsonSerializer.Serialize(WebAboutState()));
            Clipboard.SetText(value.RootElement.GetProperty("diagnostic").GetString()!); return null;
        }
        throw new InvalidDataException("Unknown preferences action.");
    }

    async Task CheckWebUpdateAsync()
    {
        CancelWebUpdate(); int generation = webUpdateGeneration;
        var cancellation = webUpdateCancellation = new CancellationTokenSource();
        webUpdateState = "checking"; webUpdateRelease = null; PublishWebState();
        try
        {
            var release = WebUpdateCheckForDiagnostics != null ? await WebUpdateCheckForDiagnostics(cancellation.Token) : await UpdateChecker.CheckAsync(cancellation.Token);
            if (generation != webUpdateGeneration || closing || IsDisposed || cancellation.IsCancellationRequested) return;
            webUpdateRelease = release; webUpdateState = release == null ? "noRelease" : release.IsNewer ? "new" : "current";
        }
        catch (Exception ex)
        {
            if (generation != webUpdateGeneration || closing || IsDisposed || cancellation.IsCancellationRequested) return;
            webUpdateState = ex is OperationCanceledException ? "timeout" : "failed";
        }
        finally
        {
            if (generation == webUpdateGeneration) { cancellation.Dispose(); webUpdateCancellation = null; PublishWebState(); }
        }
    }

    void CancelWebUpdate()
    {
        webUpdateGeneration++;
        if (webUpdateCancellation == null) return;
        webUpdateCancellation.Cancel(); webUpdateCancellation.Dispose(); webUpdateCancellation = null;
        webUpdateState = "idle"; webUpdateRelease = null;
    }
}
