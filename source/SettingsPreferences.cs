namespace TrayPilot;

internal sealed record SettingsPreferences(string Language, string Theme, bool CloseToTray, bool ShowTrayIcon,
    bool RestoreOnExit, bool? Startup, bool MainEnabled, Keys MainKeys, bool ShowEnabled, Keys ShowKeys, bool HideEnabled, Keys HideKeys)
{
    internal static SettingsPreferences Read(SavedState state, bool? startup) => new(state.Language, state.Theme,
        state.CloseToTray, state.ShowTrayIcon, state.RestoreIconsOnExit, startup, state.MainHotkeyEnabled, (Keys)state.MainHotkey,
        state.ShowAllHotkeyEnabled, (Keys)state.ShowAllHotkey, state.HideRulesHotkeyEnabled, (Keys)state.HideRulesHotkey);
    internal (bool Enabled, Keys Keys)[] Hotkeys => [(MainEnabled, MainKeys), (ShowEnabled, ShowKeys), (HideEnabled, HideKeys)];
    internal int[] InvalidHotkeys => Hotkeys.Select((item, index) => (item, index)).Where(entry => entry.item.Enabled &&
        (!GlobalHotkey.Valid(entry.item.Keys) || Hotkeys.Where((_, index) => index != entry.index).Any(other => other.Enabled && other.Keys == entry.item.Keys)))
        .Select(entry => entry.index).ToArray();
    internal void Write(SavedState state)
    {
        state.Language = Language; state.Theme = Theme; state.CloseToTray = CloseToTray; state.ShowTrayIcon = ShowTrayIcon;
        state.RestoreIconsOnExit = RestoreOnExit;
        state.MainHotkeyEnabled = MainEnabled; state.MainHotkey = (int)MainKeys;
        state.ShowAllHotkeyEnabled = ShowEnabled; state.ShowAllHotkey = (int)ShowKeys;
        state.HideRulesHotkeyEnabled = HideEnabled; state.HideRulesHotkey = (int)HideKeys;
    }
}

internal static class SettingsTransaction
{
    internal static void Save(Controller controller, SettingsPreferences draft, bool changeStartup, StartupRegistration startup,
        Func<bool> registerShortcuts)
    {
        if (draft.InvalidHotkeys.Length > 0) throw new IOException(L.T("invalidHotkeyMessage"));
        var before = SettingsPreferences.Read(controller.Saved, null);
        bool hotkeysChanged = !before.Hotkeys.SequenceEqual(draft.Hotkeys);
        StartupRegistration.Snapshot? previousStartup = null;
        try
        {
            draft.Write(controller.Saved);
            if (hotkeysChanged && !registerShortcuts()) throw new IOException(L.T("settingsShortcutOccupied"));
            if (changeStartup && draft.Startup.HasValue)
            {
                previousStartup = startup.Capture(); startup.SetEnabled(draft.Startup.Value);
            }
            controller.Save();
        }
        catch (Exception operation)
        {
            before.Write(controller.Saved);
            var errors = new List<string>();
            if (hotkeysChanged)
                try { if (!registerShortcuts()) errors.Add(L.T("settingsShortcutRollbackFailed")); }
                catch (Exception rollback) { errors.Add(rollback.Message); }
            if (previousStartup != null)
                try { startup.Restore(previousStartup); } catch (Exception rollback) { errors.Add(rollback.Message); }
            if (errors.Count > 0) throw new IOException(operation.Message + "\n" + L.T("settingsRollbackFailed") + " " + string.Join(" · ", errors), operation);
            throw;
        }
    }
}
