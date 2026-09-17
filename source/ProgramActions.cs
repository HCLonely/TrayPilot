using System.Diagnostics;

namespace TrayPilot;

internal static class ProgramActions
{
    internal static List<KeyValuePair<string, string>> Properties(TrayEntry entry, bool hasRule)
    {
        bool running = Scanner.SameOwner(entry);
        int state = running ? Native.State(entry) : -1;
        var rows = new List<KeyValuePair<string, string>>();
        void Add(string key, object? value) => rows.Add(new(L.T(key), Convert.ToString(value) ?? ""));
        Add("softwareName", entry.Name); Add("processName", Path.GetFileName(entry.Path)); Add("PID", entry.Pid);
        Add("processState", L.T(running ? "running" : "processExitedOrChanged"));
        Add("iconState", L.T(state == 0 ? "normal" : state == 1 ? "fullyHidden" : "unavailable"));
        Add("autoHideRule", L.T(hasRule ? "yes" : "no")); Add("fullPath", entry.Path);
        Add("cachedTrayTooltip", entry.Tooltip);
        Add("iconId", entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString());
        if (entry.Started > 0 && entry.Started <= DateTime.MaxValue.Ticks)
            Add("processStartTime", new DateTime(entry.Started, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            var version = FileVersionInfo.GetVersionInfo(entry.Path);
            Add("productName", version.ProductName); Add("fileVersion", version.FileVersion);
            Add("productVersion", version.ProductVersion); Add("company", version.CompanyName); Add("fileDescription", version.FileDescription);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { Add("fileVersionInformation", L.T("cannotRead")); }
        return rows;
    }
    internal static string Describe(TrayEntry entry, bool hasRule) =>
        string.Join(Environment.NewLine, Properties(entry, hasRule).Select(x => x.Key + "：" + x.Value));

    internal static Task EndAsync(TrayEntry entry) => Task.Run(() =>
    {
        if (Scanner.IsShellEntry(entry)) throw new InvalidOperationException(L.T("systemTrayEndTaskUnsupportedMessage"));
        if (entry.Pid == Environment.ProcessId) throw new InvalidOperationException(L.T("exitViaMenuMessage"));
        using var process = Process.GetProcessById(checked((int)entry.Pid));
        // Pin the process handle and verify its identity before terminating; never act on a reused PID.
        _ = process.Handle;
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != entry.Started
            || !string.Equals(Native.ProcessPath(entry.Pid), entry.Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("processUnavailableMessage"));
        process.Kill();
        if (!process.WaitForExit(5000)) throw new IOException(L.T("processTerminationPendingMessage"));
    });
}
