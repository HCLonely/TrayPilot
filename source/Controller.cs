using System.Text.Json;

namespace TrayPilot;
internal sealed class SavedState
{
    public List<string> HiddenPaths { get; set; } = new();
    public List<TrayEntry> Recovery { get; set; } = new();
    public string Language { get; set; } = L.SystemLanguage;
    public bool CloseToTray { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public bool MainHotkeyEnabled { get; set; }
    public int MainHotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.M);
    public string Theme { get; set; } = "system";
    public bool RulesPaused { get; set; }
    public bool ShowAllHotkeyEnabled { get; set; }
    public int ShowAllHotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.S);
    public bool HideRulesHotkeyEnabled { get; set; }
    public int HideRulesHotkey { get; set; } = (int)(Keys.Control | Keys.Alt | Keys.H);
}
internal sealed class Controller
{
    readonly string file;
    readonly HashSet<string> manuallyShown = new(StringComparer.OrdinalIgnoreCase);
    internal SavedState Saved { get; }
    internal Controller(string folder)
    {
        Directory.CreateDirectory(folder);
        file = System.IO.Path.Combine(folder, "settings.json");
        // Never silently discard an unreadable recovery journal.
        Saved = File.Exists(file) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(file)) ?? throw new IOException(L.T("emptySettingsFileMessage")) : new();
    }
    internal static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path.Trim()).Replace('/', '\\'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path.Trim(); }
    }
    internal static bool SamePath(string first, string second) => string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
    internal bool HasRule(string path) => Saved.HiddenPaths.Any(x => SamePath(x, path));
    internal bool IsTemporarilyShown(string path) => manuallyShown.Contains(NormalizePath(path));
    internal void SetManualVisibility(string path, bool hidden)
    {
        if (hidden) manuallyShown.Remove(NormalizePath(path));
        else if (HasRule(path)) manuallyShown.Add(NormalizePath(path));
    }
    internal void ResetManualVisibility() => manuallyShown.Clear();
    internal void Save()
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
    internal void AddRule(string path)
    {
        path = NormalizePath(path);
        if (HasRule(path)) return;
        Saved.HiddenPaths.Add(path);
        try { Save(); } catch { Saved.HiddenPaths.Remove(path); throw; }
        manuallyShown.Remove(path);
    }
    internal void RemoveRule(string path)
    {
        var previous = Saved.HiddenPaths.ToList();
        if (Saved.HiddenPaths.RemoveAll(x => SamePath(x, path)) == 0) return;
        try { Save(); } catch { Saved.HiddenPaths = previous; throw; }
        manuallyShown.Remove(NormalizePath(path));
    }
    internal void ClearRules()
    {
        var previous = Saved.HiddenPaths.ToList(); Saved.HiddenPaths.Clear();
        try { Save(); } catch { Saved.HiddenPaths = previous; throw; }
        manuallyShown.Clear();
    }
    internal void Hide(TrayEntry entry)
    {
        if (!Scanner.SameOwner(entry)) return;
        int before = Native.State(entry);
        if (before != 0) return;
        if (!Saved.Recovery.Any(x => x.Key == entry.Key))
        {
            Saved.Recovery.Add(entry);
            try { Save(); } catch { Saved.Recovery.Remove(entry); throw; }
        }
        if (!Native.SetHidden(entry, true) || !WaitForState(entry, 1)) throw new IOException(L.F("iconHideFailedMessage", entry.Name, entry.Id, entry.Guid));
    }
    internal void Show(TrayEntry entry)
    {
        if (Scanner.SameOwner(entry) && Native.State(entry) == 1)
        {
            if (!Native.SetHidden(entry, false) || !WaitForState(entry, 0)) throw new IOException(L.F("iconRestoreFailedMessage", entry.Name));
        }
        var removed = Saved.Recovery.Where(x => x.Key == entry.Key).ToList();
        if (removed.Count == 0) return;
        Saved.Recovery.RemoveAll(x => x.Key == entry.Key);
        try { Save(); } catch { Saved.Recovery.AddRange(removed); throw; }
    }
    internal void Apply(List<TrayEntry> entries)
    {
        var staleEntries = Saved.Recovery.Where(x => !Scanner.SameOwner(x) || Native.State(x) < 0).ToList();
        foreach (var stale in staleEntries) Saved.Recovery.Remove(stale);
        if (staleEntries.Count > 0) Save();
        if (Saved.RulesPaused) return;
        var paths = Saved.HiddenPaths.Select(NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var entry in entries.Where(x => paths.Contains(NormalizePath(x.Path)) && !IsTemporarilyShown(x.Path)).DistinctBy(x => x.Key))
            try { Hide(entry); } catch (Exception ex) { errors.Add(ex.Message); }
        if (errors.Count > 0) throw new IOException(string.Join(Environment.NewLine, errors));
    }
    static bool WaitForState(TrayEntry entry, int state)
    {
        for (int i = 0; i < 20; i++) { if (Native.State(entry) == state) return true; Thread.Sleep(40); }
        return Native.State(entry) == state;
    }
    internal void RestoreManaged()
    {
        var errors = new List<string>();
        foreach (var entry in Saved.Recovery.ToList())
            try { Show(entry); } catch (Exception e) { errors.Add(e.Message); }
        if (errors.Count > 0) throw new IOException(string.Join(Environment.NewLine, errors));
    }
}
