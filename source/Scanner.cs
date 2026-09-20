using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrayPilot;
public sealed record TrayEntry
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public long Window { get; init; }
    public uint Pid { get; init; }
    public long Started { get; init; }
    public uint Id { get; init; }
    public Guid Guid { get; init; }
    public int State { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? IconSnapshot { get; init; }
    public string Key => Guid != Guid.Empty ? Guid.ToString() : $"{Window}:{Id}:{Pid}:{Started}";
}
internal static class Scanner
{
    internal static readonly Guid HardwareRemovalGuid = new("7820AE78-23E3-4229-82C1-E41CB67D5B9C");
    internal static bool IsShellEntry(TrayEntry entry) => Controller.SamePath(entry.Path, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    internal static string ExpandPath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith('{') && path.IndexOf('}') is int end && end > 0 && Guid.TryParse(path[..(end + 1)], out var guid))
        {
            if (Native.SHGetKnownFolderPath(ref guid, 0, 0, out var p) == 0)
                try { path = Marshal.PtrToStringUni(p) + path[(end + 1)..]; } finally { Marshal.FreeCoTaskMem(p); }
        }
        return path;
    }
    static string Name(string path)
    {
        if (Names.TryGetValue(path, out var value)) return value;
        try { value = FileVersionInfo.GetVersionInfo(path).FileDescription ?? ""; } catch { value = ""; }
        if (string.IsNullOrWhiteSpace(value)) value = System.IO.Path.GetFileNameWithoutExtension(path);
        return Names[path] = value;
    }
    internal static bool SameOwner(TrayEntry entry, Dictionary<uint, (string Path, long Start)>? processes = null)
    {
        uint pid = entry.Pid;
        if (entry.Guid == Guid.Empty)
        {
            if (!Native.IsWindow((nint)entry.Window)) return false;
            Native.GetWindowThreadProcessId((nint)entry.Window, out pid);
        }
        if (pid != entry.Pid) return false;
        if (processes == null)
            return entry.Started > 0 && string.Equals(Native.ProcessPath(pid), entry.Path, StringComparison.OrdinalIgnoreCase) && Native.ProcessStarted(pid) == entry.Started;
        if (!processes.TryGetValue(pid, out var info))
            processes[pid] = info = (Native.ProcessPath(pid), Native.ProcessStarted(pid));
        return entry.Started > 0 && info.Start == entry.Started && string.Equals(info.Path, entry.Path, StringComparison.OrdinalIgnoreCase);
    }
    internal static List<TrayEntry> Scan()
    {
        var windows = new HashSet<nint>();
        Native.EnumProc child = (h, _) => { windows.Add(h); return true; };
        Native.EnumWindows((h, _) => { windows.Add(h); Native.EnumChildWindows(h, child, 0); return true; }, 0);
        nint message = 0;
        var seen = new HashSet<nint>();
        while ((message = Native.FindWindowExW(-3, message, null, null)) != 0 && seen.Add(message))
        { windows.Add(message); Native.EnumChildWindows(message, child, 0); }
        var processes = new Dictionary<uint, (string Path, long Start)>();
        Dictionary<uint, long>? fallbackStarts = null;
        Dictionary<uint, long> LoadFallbackStarts() => fallbackStarts ??= Native.SystemProcessStarts();
        var owners = new Dictionary<string, List<(nint Window, uint Pid, long Start)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (!processes.TryGetValue(pid, out var info))
            {
                info = (Native.ProcessPath(pid), Native.ProcessStarted(pid, LoadFallbackStarts)); processes[pid] = info;
            }
            if (info.Path.Length == 0 || info.Start == 0) continue;
            if (!owners.TryGetValue(info.Path, out var list)) owners[info.Path] = list = new();
            list.Add((window, pid, info.Start));
        }
        var result = new Dictionary<string, TrayEntry>();
        using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
        foreach (var subkey in root?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var key = root!.OpenSubKey(subkey);
            if (key == null) continue;
            var path = ExpandPath(key.GetValue("ExecutablePath") as string ?? "");
            Guid.TryParse(key.GetValue("IconGuid") as string, out var guid);
            bool shell = IsShellEntry(new TrayEntry { Path = path });
            // The removable-hardware icon is independently addressable. Keep other
            // shell controls out of executable-wide actions and hide rules.
            if (shell && guid != HardwareRemovalGuid) continue;
            if (!owners.TryGetValue(path, out var list)) continue;
            string tooltip = key.GetValue("InitialTooltip") as string ?? "";
            string name = shell ? L.T("safelyRemoveHardware") :
                System.IO.Path.GetFileName(path).Equals("NVDisplay.Container.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tooltip)
                    ? tooltip : Name(path);
            var uidValue = key.GetValue("UID");
            if (guid == Guid.Empty && uidValue is not int) continue;
            var uid = uidValue is int v ? unchecked((uint)v) : 0;
            // Task Manager's cache records UID -1, but its live CPU icon can use
            // UID 0 on the same TrayiconMessageWindow. Discover both identities;
            // never replace the cached UID, which may also still be registered.
            bool taskManager = guid == Guid.Empty && Controller.SamePath(path,
                System.IO.Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"));
            byte[]? snapshot = null;
            bool snapshotRead = false;
            foreach (var owner in shell ? list.Where(x => Native.WindowClass(x.Window) == "SystemTray_Main") : list)
            {
                var ids = taskManager && uid != 0 && Native.WindowClass(owner.Window) == "TrayiconMessageWindow"
                    ? new[] { uid, 0u } : new[] { uid };
                foreach (var iconId in ids)
                {
                    var entry = new TrayEntry { Path = path, Name = name, Tooltip = tooltip,
                        Window = owner.Window, Pid = owner.Pid, Started = owner.Start, Id = iconId, Guid = guid };
                    int state = Native.State(entry);
                    // On tested Windows 11 25H2: S_OK = displayed (possibly in overflow), S_FALSE = NIS_HIDDEN.
                    if (state is not (0 or 1)) continue;
                    if (!snapshotRead) { snapshot = key.GetValue("IconSnapshot") as byte[]; snapshotRead = true; }
                    result[entry.Key] = entry with { State = state, IconSnapshot = snapshot };
                }
                if (guid != Guid.Empty && result.ContainsKey(guid.ToString())) break;
            }
        }
        // This system icon can be live even when its Windows cache is absent.
        // Probe only its known GUID and actual shell host; do not expose arbitrary
        // Explorer controls as ordinary notification icons.
        string explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!result.ContainsKey(HardwareRemovalGuid.ToString()) && owners.TryGetValue(explorer, out var shellOwners))
        {
            foreach (var owner in shellOwners.Where(x => Native.WindowClass(x.Window) == "SystemTray_Main"))
            {
                var entry = new TrayEntry { Path = explorer, Name = L.T("safelyRemoveHardware"),
                    Window = owner.Window, Pid = owner.Pid, Started = owner.Start, Guid = HardwareRemovalGuid };
                int state = Native.State(entry);
                if (state is not (0 or 1)) continue;
                result[entry.Key] = entry with { State = state };
                break;
            }
        }
        return result.Values.OrderBy(x => x.Name).ThenBy(x => x.Pid).ToList();
    }
}
