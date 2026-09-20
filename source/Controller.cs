using System.Text.Json;

namespace TrayPilot;
internal sealed record IconRule(string Path, uint Id, Guid Guid, string WindowClass)
{
    internal static IconRule From(TrayEntry entry) => new(Controller.NormalizePath(entry.Path), entry.Id, entry.Guid,
        entry.Guid == Guid.Empty ? Native.WindowClass((nint)entry.Window) : "");
    internal bool Matches(TrayEntry entry) => Controller.SamePath(Path, entry.Path) &&
        (Guid != System.Guid.Empty ? Guid == entry.Guid : entry.Guid == System.Guid.Empty && Id == entry.Id &&
            WindowClass == Native.WindowClass((nint)entry.Window));
    internal string Label => Guid != System.Guid.Empty ? Guid.ToString() : $"UID {Id} · {WindowClass}";
}
internal sealed class SavedState
{
    public List<string> HiddenPaths { get; set; } = new();
    public List<IconRule> HiddenIcons { get; set; } = new();
    public List<TrayEntry> Recovery { get; set; } = new();
    public int HiddenSystemIcons { get; set; }
    public bool UseLegacySystemIconDiscovery { get; set; }
    public string Language { get; set; } = L.SystemLanguage;
    public bool CloseToTray { get; set; } = true;
    public bool RestoreIconsOnExit { get; set; }
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
    static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };
    bool stopping;
    internal void StopOperations() => stopping = true;
    void CheckStopping(bool asynchronous) { if (asynchronous && stopping) throw new OperationCanceledException(); }
    readonly HashSet<string> manuallyShown = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TrayEntry> individuallyShown = new();
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
    internal bool HasIconRule(TrayEntry entry) => Saved.HiddenIcons.Any(x => x.Matches(entry));
    internal bool HasRule(TrayEntry entry) => HasRule(entry.Path) || HasIconRule(entry);
    internal Func<TrayEntry, bool> RuleMatcher()
    {
        var paths = Saved.HiddenPaths.Select(NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var icons = Saved.HiddenIcons.ToLookup(x => NormalizePath(x.Path), StringComparer.OrdinalIgnoreCase);
        return entry =>
        {
            string path = NormalizePath(entry.Path);
            if (paths.Contains(path)) return true;
            string? windowClass = null;
            return icons[path].Any(rule => rule.Guid != Guid.Empty ? rule.Guid == entry.Guid :
                entry.Guid == Guid.Empty && rule.Id == entry.Id && rule.WindowClass == (windowClass ??= Native.WindowClass((nint)entry.Window)));
        };
    }
    internal bool IsTemporarilyShown(TrayEntry entry) => IsTemporarilyShown(entry.Path) || individuallyShown.ContainsKey(entry.Key);
    internal void SetManualVisibility(TrayEntry entry, bool hidden)
    {
        if (hidden) individuallyShown.Remove(entry.Key);
        else if (HasRule(entry)) individuallyShown[entry.Key] = entry;
    }
    internal bool IsTemporarilyShown(string path) => manuallyShown.Contains(NormalizePath(path));
    internal void SetManualVisibility(string path, bool hidden)
    {
        foreach (var key in individuallyShown.Where(x => SamePath(x.Value.Path, path)).Select(x => x.Key).ToList()) individuallyShown.Remove(key);
        if (hidden) manuallyShown.Remove(NormalizePath(path));
        else if (HasRule(path) || Saved.HiddenIcons.Any(x => SamePath(x.Path, path))) manuallyShown.Add(NormalizePath(path));
    }
    internal void ResetManualVisibility() { manuallyShown.Clear(); individuallyShown.Clear(); }
    internal void AddIconRule(TrayEntry entry)
    {
        if (HasIconRule(entry)) return;
        var rule = IconRule.From(entry);
        Saved.HiddenIcons.Add(rule);
        try { Save(); } catch { Saved.HiddenIcons.Remove(rule); throw; }
        individuallyShown.Remove(entry.Key);
        manuallyShown.Remove(NormalizePath(entry.Path));
    }
    internal void RemoveIconRule(IconRule rule)
    {
        var previous = Saved.HiddenIcons.ToList();
        if (!Saved.HiddenIcons.Remove(rule)) return;
        try { Save(); } catch { Saved.HiddenIcons = previous; throw; }
    }
    internal void Save()
    {
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Saved, SaveOptions));
        File.Move(file + ".tmp", file, true);
    }
    internal void SetHiddenSystemIcons(int mask)
    {
        if ((mask & ~SystemIconCatalog.All) != 0) throw new ArgumentOutOfRangeException(nameof(mask));
        int previous = Saved.HiddenSystemIcons;
        Saved.HiddenSystemIcons = mask;
        try { Save(); } catch { Saved.HiddenSystemIcons = previous; throw; }
    }
    internal void AddRule(string path)
    {
        path = NormalizePath(path);
        if (HasRule(path)) return;
        Saved.HiddenPaths.Add(path);
        try { Save(); } catch { Saved.HiddenPaths.Remove(path); throw; }
        SetManualVisibility(path, hidden: true);
    }
    internal void RemoveRule(string path)
    {
        var previous = Saved.HiddenPaths.ToList();
        if (Saved.HiddenPaths.RemoveAll(x => SamePath(x, path)) == 0) return;
        try { Save(); } catch { Saved.HiddenPaths = previous; throw; }
        manuallyShown.Remove(NormalizePath(path));
    }
    internal void Hide(TrayEntry entry)
        => HideCore(entry, false).GetAwaiter().GetResult();
    async Task HideCore(TrayEntry entry, bool asynchronous)
    {
        CheckStopping(asynchronous);
        if (!Scanner.SameOwner(entry)) return;
        int before = Native.State(entry);
        if (before != 0) return;
        if (!Saved.Recovery.Any(x => x.Key == entry.Key))
        {
            Saved.Recovery.Add(entry);
            try { Save(); } catch { Saved.Recovery.Remove(entry); throw; }
        }
        if (!Native.SetHidden(entry, true) || !await WaitForState(entry, 1, asynchronous)) throw new IOException(L.F("iconHideFailedMessage", entry.Name, entry.Id, entry.Guid));
    }
    internal void Show(TrayEntry entry)
        => ShowCore(entry, false).GetAwaiter().GetResult();
    async Task ShowCore(TrayEntry entry, bool asynchronous, bool save = true)
    {
        CheckStopping(asynchronous);
        if (Scanner.SameOwner(entry) && Native.State(entry) == 1)
        {
            if (!Native.SetHidden(entry, false) || !await WaitForState(entry, 0, asynchronous)) throw new IOException(L.F("iconRestoreFailedMessage", entry.Name));
        }
        var removed = Saved.Recovery.Where(x => x.Key == entry.Key).ToList();
        if (removed.Count == 0) return;
        Saved.Recovery.RemoveAll(x => x.Key == entry.Key);
        try { if (save) Save(); } catch { Saved.Recovery.AddRange(removed); throw; }
    }
    internal void Apply(List<TrayEntry> entries)
        => ApplyAsync(entries, false).GetAwaiter().GetResult();
    internal async Task ApplyAsync(List<TrayEntry> entries, bool asynchronous = true)
    {
        var staleEntries = Saved.Recovery.Where(x => !Scanner.SameOwner(x) || Native.State(x) < 0).ToList();
        foreach (var stale in staleEntries) Saved.Recovery.Remove(stale);
        if (staleEntries.Count > 0)
            try { Save(); } catch { Saved.Recovery.AddRange(staleEntries); throw; }
        if (Saved.RulesPaused) return;
        var matches = RuleMatcher();
        await ChangeManyAsync(entries.Where(x => matches(x) && !IsTemporarilyShown(x)), true, asynchronous);
    }
    async Task<bool> WaitForState(TrayEntry entry, int state, bool asynchronous)
    {
        for (int i = 0; i < 20; i++)
        {
            CheckStopping(asynchronous);
            if (Native.State(entry) == state) return true;
            if (asynchronous) await Task.Delay(40); else Thread.Sleep(40);
        }
        return Native.State(entry) == state;
    }
    internal async Task ChangeManyAsync(IEnumerable<TrayEntry> entries, bool hidden, bool asynchronous = true,
        Action<TrayEntry>? completed = null)
    {
        CheckStopping(asynchronous);
        var targets = entries.DistinctBy(x => x.Key).ToList();
        var before = Saved.Recovery.ToList();
        if (hidden)
        {
            var recorded = Saved.Recovery.Select(x => x.Key).ToHashSet();
            foreach (var entry in targets)
                if (!recorded.Contains(entry.Key) && Scanner.SameOwner(entry) && Native.State(entry) == 0)
                { Saved.Recovery.Add(entry); recorded.Add(entry.Key); }
            // The entire batch is journaled before the first icon is hidden.
            if (Saved.Recovery.Count != before.Count)
                try { Save(); } catch { Saved.Recovery = before; throw; }
        }
        var errors = new List<string>();
        foreach (var entry in targets)
            try
            {
                if (hidden) await HideCore(entry, asynchronous); else await ShowCore(entry, asynchronous, save: false);
                completed?.Invoke(entry);
            }
            catch (OperationCanceledException) when (stopping) { throw; }
            catch (Exception ex) { errors.Add(ex.Message); }
        if (!hidden && Saved.Recovery.Count != before.Count)
            try { Save(); }
            catch (Exception ex) { Saved.Recovery = before; errors.Add(ex.Message); }
        if (errors.Count != 0) throw new IOException(string.Join(Environment.NewLine, errors));
    }
    internal void RestoreManaged(bool temporarilyShow = false)
        => RestoreManagedAsync(temporarilyShow, false).GetAwaiter().GetResult();
    internal Task RestoreManagedAsync(bool temporarilyShow = false, bool asynchronous = true)
    {
        return ChangeManyAsync(Saved.Recovery.ToList(), false, asynchronous,
            entry => { if (temporarilyShow) SetManualVisibility(entry.Path, hidden: false); });
    }
}
