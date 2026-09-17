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
    internal static bool SameOwner(TrayEntry entry)
    {
        uint pid = entry.Pid;
        if (entry.Guid == Guid.Empty)
        {
            if (!Native.IsWindow((nint)entry.Window)) return false;
            Native.GetWindowThreadProcessId((nint)entry.Window, out pid);
        }
        if (pid != entry.Pid || !string.Equals(Native.ProcessPath(pid), entry.Path, StringComparison.OrdinalIgnoreCase)) return false;
        try { using var p = Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks == entry.Started; } catch { return false; }
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
        var owners = new Dictionary<string, List<(nint Window, uint Pid, long Start)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (!processes.TryGetValue(pid, out var info))
            {
                long start = 0;
                try { using var process = Process.GetProcessById((int)pid); start = process.StartTime.ToUniversalTime().Ticks; } catch { }
                info = (Native.ProcessPath(pid), start); processes[pid] = info;
            }
            if (info.Path.Length == 0 || info.Start == 0) continue;
            if (!owners.TryGetValue(info.Path, out var list)) owners[info.Path] = list = new();
            list.Add((window, pid, info.Start));
        }
        var result = new Dictionary<string, TrayEntry>();
        using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
        if (root == null) return new();
        foreach (var subkey in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(subkey);
            if (key == null) continue;
            var path = ExpandPath(key.GetValue("ExecutablePath") as string ?? "");
            // Shell-owned controls have different lifetime and callback semantics; exclude them.
            if (string.Equals(System.IO.Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!owners.TryGetValue(path, out var list)) continue;
            Guid.TryParse(key.GetValue("IconGuid") as string, out var guid);
            var uidValue = key.GetValue("UID");
            if (guid == Guid.Empty && uidValue is not int) continue;
            var uid = uidValue is int v ? unchecked((uint)v) : 0;
            foreach (var owner in list)
            {
                var entry = new TrayEntry { Path = path, Name = Name(path), Tooltip = key.GetValue("InitialTooltip") as string ?? "",
                    Window = owner.Window, Pid = owner.Pid, Started = owner.Start, Id = uid, Guid = guid,
                    IconSnapshot = key.GetValue("IconSnapshot") as byte[] };
                int state = Native.State(entry);
                // On tested Windows 11 25H2: S_OK = displayed (possibly in overflow), S_FALSE = NIS_HIDDEN.
                if (state is not (0 or 1)) continue;
                result[entry.Key] = entry with { State = state };
                if (guid != Guid.Empty) break;
            }
        }
        return result.Values.OrderBy(x => x.Name).ThenBy(x => x.Pid).ToList();
    }
}
