using System.Text.Json;

namespace TrayPilot;
internal sealed class SavedState
{
    public List<string> HiddenPaths { get; set; } = new();
    public List<TrayEntry> Recovery { get; set; } = new();
    public string Language { get; set; } = "zh-CN";
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
    internal SavedState Saved { get; }
    internal Controller(string folder)
    {
        Directory.CreateDirectory(folder);
        file = System.IO.Path.Combine(folder, "settings.json");
        // Never silently discard an unreadable recovery journal.
        Saved = File.Exists(file) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(file)) ?? throw new IOException(L.T("设置文件为空。")) : new();
    }
    internal bool HasRule(string path) => Saved.HiddenPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
    internal void Save()
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
    internal void AddRule(string path) { if (!HasRule(path)) Saved.HiddenPaths.Add(path); Save(); }
    internal void RemoveRule(string path) { Saved.HiddenPaths.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)); Save(); }
    internal void Hide(TrayEntry entry)
    {
        if (!Scanner.SameOwner(entry)) return;
        int before = Native.State(entry);
        if (before != 0) return;
        if (!Saved.Recovery.Any(x => x.Key == entry.Key)) { Saved.Recovery.Add(entry); Save(); }
        if (!Native.SetHidden(entry, true) || !WaitForState(entry, 1)) throw new IOException(L.F("无法隐藏 {0}（ID={1}, GUID={2}），系统未确认隐藏状态。", entry.Name, entry.Id, entry.Guid));
    }
    internal void Show(TrayEntry entry)
    {
        if (Scanner.SameOwner(entry) && Native.State(entry) is 0 or 1)
        {
            if (!Native.SetHidden(entry, false) || !WaitForState(entry, 0)) throw new IOException(L.F("无法恢复 {0}。", entry.Name));
        }
        Saved.Recovery.RemoveAll(x => x.Key == entry.Key); Save();
    }
    internal void Apply(List<TrayEntry> entries)
    {
        var staleEntries = Saved.Recovery.Where(x => !Scanner.SameOwner(x) || Native.State(x) < 0).ToList();
        foreach (var stale in staleEntries) Saved.Recovery.Remove(stale);
        if (staleEntries.Count > 0) Save();
        foreach (var entry in entries.Where(x => HasRule(x.Path))) Hide(entry);
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
