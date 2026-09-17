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
        Add("软件名称", entry.Name); Add("进程名称", Path.GetFileName(entry.Path)); Add("PID", entry.Pid);
        Add("运行状态", L.T(running ? "运行中" : "已退出或进程已变化"));
        Add("图标状态", L.T(state == 0 ? "正常" : state == 1 ? "完全隐藏" : "不可用"));
        Add("自动隐藏规则", L.T(hasRule ? "是" : "否")); Add("完整路径", entry.Path);
        Add("托盘提示（Windows 缓存）", entry.Tooltip);
        Add("图标标识", entry.Guid == Guid.Empty ? entry.Id.ToString() : entry.Guid.ToString());
        if (entry.Started > 0 && entry.Started <= DateTime.MaxValue.Ticks)
            Add("启动时间", new DateTime(entry.Started, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            var version = FileVersionInfo.GetVersionInfo(entry.Path);
            Add("产品名称", version.ProductName); Add("文件版本", version.FileVersion);
            Add("产品版本", version.ProductVersion); Add("公司", version.CompanyName); Add("文件描述", version.FileDescription);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { Add("文件版本信息", L.T("无法读取")); }
        return rows;
    }
    internal static string Describe(TrayEntry entry, bool hasRule) =>
        string.Join(Environment.NewLine, Properties(entry, hasRule).Select(x => x.Key + "：" + x.Value));

    internal static Task EndAsync(TrayEntry entry) => Task.Run(() =>
    {
        if (Scanner.IsShellEntry(entry)) throw new InvalidOperationException(L.T("系统托盘图标由资源管理器托管，不支持结束任务。"));
        if (entry.Pid == Environment.ProcessId) throw new InvalidOperationException(L.T("请通过退出菜单结束 TrayPilot，以便恢复隐藏图标。"));
        using var process = Process.GetProcessById(checked((int)entry.Pid));
        // Pin the process handle and verify its identity before terminating; never act on a reused PID.
        _ = process.Handle;
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != entry.Started
            || !string.Equals(Native.ProcessPath(entry.Pid), entry.Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("进程已退出或已变化，请刷新后重试。"));
        process.Kill();
        if (!process.WaitForExit(5000)) throw new IOException(L.T("已发送结束请求，但进程尚未退出，请稍后刷新。"));
    });
}
