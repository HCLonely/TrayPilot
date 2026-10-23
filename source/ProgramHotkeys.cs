namespace TrayPilot;

internal sealed partial class MainForm
{
    readonly Dictionary<string, GlobalHotkey> programHotkeys = new(StringComparer.OrdinalIgnoreCase);

    void DisposeProgramHotkeys()
    {
        foreach (var hotkey in programHotkeys.Values) hotkey.Dispose();
        programHotkeys.Clear();
    }

    void RegisterProgramHotkeys()
    {
        int id = 0x5000;
        var global = SettingsPreferences.Read(controller.Saved, null).Hotkeys;
        var used = global.Where(h => h.Enabled).Select(h => (int)h.Keys).ToHashSet();
        foreach (var (path, keys) in controller.Saved.ProgramHotkeys)
        {
            var hotkey = new GlobalHotkey(Handle, id);
            id += 2;
            programHotkeys[path] = hotkey;
            if (!used.Add(keys) || !hotkey.TrySet(true, (Keys)keys)) hotkeyWarning = true;
        }
    }

    void SaveProgramHotkey(string path, int keys)
    {
        path = Controller.NormalizePath(path);
        var global = SettingsPreferences.Read(controller.Saved, null).Hotkeys;
        if (keys != 0 && (!GlobalHotkey.Valid((Keys)keys) ||
            global.Any(h => h.Enabled && (int)h.Keys == keys) ||
            controller.Saved.ProgramHotkeys.Any(h => !Controller.SamePath(h.Key, path) && h.Value == keys)))
            throw new IOException(L.T("invalidHotkeyMessage"));
        var before = new Dictionary<string, int>(controller.Saved.ProgramHotkeys, StringComparer.OrdinalIgnoreCase);
        if (keys != 0 && !before.ContainsKey(path) && before.Count >= 512) throw new IOException(L.T("invalidHotkeyMessage"));
        try
        {
            if (keys == 0) controller.Saved.ProgramHotkeys.Remove(path);
            else controller.Saved.ProgramHotkeys[path] = keys;
            RegisterShortcuts();
            if (keys != 0 && !programHotkeys[path].TrySet(true, (Keys)keys))
                throw new IOException(L.T("invalidHotkeyMessage"));
            controller.Save();
        }
        catch
        {
            controller.Saved.ProgramHotkeys = before;
            RegisterShortcuts();
            throw;
        }
    }

    bool HandleProgramHotkey(nint id)
    {
        var path = programHotkeys.FirstOrDefault(h => h.Value.Matches(id)).Key;
        if (path == null) return false;
        _ = ToggleProgramIconsAsync(path);
        return true;
    }

    async Task ToggleProgramIconsAsync(string path)
    {
        if (busy || closing || IsDisposed) return;
        EnsureUiContext();
        busy = true;
        webForegroundOperation = true;
        try
        {
            entries = (await Task.Run(visibilityScanner)).Where(e => e.Pid != Environment.ProcessId).ToList();
            if (closing || IsDisposed) return;
            var targets = WebEntries().Where(e => Controller.SamePath(e.Path, path)).ToList();
            if (targets.Count == 0) return;
            // Mixed visibility hides the entire program; the next press restores it.
            bool hidden = targets.Any(e => e.State == 0);
            await controller.ChangeManyAsync(targets, hidden, completed: e => controller.SetManualVisibility(e, hidden));
            controller.SetManualVisibility(path, hidden);
        }
        catch (Exception ex) { lastStatus = L.T("operationIncomplete") + ": " + ex.Message; }
        finally
        {
            UpdateEntryStates();
            UpdateStatus();
            webForegroundOperation = false;
            CompleteOperation();
            UpdateTimer();
        }
    }
}
