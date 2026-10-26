namespace TrayPilot;

internal static partial class Diagnostics
{
    static void TestSystemVisibilityPreferences(string folder, List<string> checks)
    {
        void Check(bool passed, string message)
        {
            if (!passed) throw new IOException(message);
            checks.Add(message);
        }
        var preferences = new Controller(Path.Combine(folder, "visibility-preferences"));
        Check(preferences.Saved.ForcedShownSystemIcons == 0, "Existing settings default force show to off.");
        preferences.SetSystemIconVisibility(256, 512);
        var reloaded = new Controller(preferences.SettingsFolder);
        Check(reloaded.Saved.HiddenSystemIcons == 256 && reloaded.Saved.ForcedShownSystemIcons == 512,
            "Hide and force-show selections persist independently for the two language controls.");
        foreach (var (hidden, forced) in new[] { (256, 256), (0, 1), (0, 4096), (4096, 0) })
        {
            bool rejected = false;
            try { preferences.SetSystemIconVisibility(hidden, forced); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && preferences.Saved.HiddenSystemIcons == 256 && preferences.Saved.ForcedShownSystemIcons == 512,
                $"Settings API rejects invalid visibility pair {hidden}/{forced} without mutation.");
            rejected = false;
            try { using var invalid = new SystemIconSession(initialMask: hidden, initialForcedShown: forced); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, $"Session API rejects invalid visibility pair {hidden}/{forced} before attaching to Explorer.");
        }
        preferences.SetHiddenSystemIcons(0);
        Check(preferences.Saved.ForcedShownSystemIcons == 512, "Clearing hide preferences preserves force-show selections.");
        Directory.CreateDirectory(Path.Combine(preferences.SettingsFolder, "settings.json.tmp"));
        try
        {
            bool rejected = false;
            try { preferences.SetSystemIconVisibility(512, 256); }
            catch (IOException) { rejected = true; }
            catch (UnauthorizedAccessException) { rejected = true; }
            reloaded = new Controller(preferences.SettingsFolder);
            Check(rejected && preferences.Saved.HiddenSystemIcons == 0 && preferences.Saved.ForcedShownSystemIcons == 512 &&
                reloaded.Saved.HiddenSystemIcons == 0 && reloaded.Saved.ForcedShownSystemIcons == 512,
                "Failed settings save restores both visibility masks in memory and on disk.");
        }
        finally { Directory.Delete(Path.Combine(preferences.SettingsFolder, "settings.json.tmp")); }
        preferences.Saved.HiddenSystemIcons = 256;
        preferences.Saved.ForcedShownSystemIcons = SystemIconCatalog.All;
        preferences.Save();
        reloaded = new Controller(preferences.SettingsFolder);
        Check(reloaded.Saved.HiddenSystemIcons == 256 && reloaded.Saved.ForcedShownSystemIcons == 512,
            "Settings loading removes unsupported force-show bits and resolves overlap in favor of hiding.");
        using var form = new MainForm(reloaded, initialize: false);
        Check(DashboardField<int>(form, "systemIconsRequested") == 256 && DashboardField<int>(form, "systemIconsForcedShown") == 512,
            "New windows initialize both saved visibility preferences for background enforcement.");
        using var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(null, SystemIconSession.MappingSize);
        using var view = mapping.CreateViewAccessor();
        const int offset = SystemIconSession.LanguageTraceOffset;
        Check(SystemIconSession.ReadLanguageTrace(view) == "", "Unpublished language diagnostic history is ignored.");
        byte[] trace = System.Text.Encoding.Unicode.GetBytes("force=512 found=0\nremoved\0");
        view.WriteArray(offset + 4, trace, 0, trace.Length);
        view.Write(offset, 1);
        Check(SystemIconSession.ReadLanguageTrace(view) == "", "In-progress language diagnostic history is ignored.");
        view.Write(offset, 2);
        Check(SystemIconSession.ReadLanguageTrace(view) == "force=512 found=0\nremoved", "Coherent language diagnostic history crosses the native/managed layout.");
    }
}
