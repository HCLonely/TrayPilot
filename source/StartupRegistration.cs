using Microsoft.Win32;

namespace TrayPilot;

internal sealed class StartupRegistration
{
    const string ValueName = "TrayPilot";
    readonly string runKey, approvalKey;
    internal string Command { get; }
    internal StartupRegistration(string? testKey = null, string? executable = null)
    {
        runKey = testKey ?? @"Software\Microsoft\Windows\CurrentVersion\Run";
        approvalKey = testKey == null ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" : testKey + @"\Approval";
        Command = "\"" + (executable ?? Environment.ProcessPath ?? Application.ExecutablePath) + "\" --startup";
    }
    internal sealed record Value(object Data, RegistryValueKind Kind);
    internal sealed record Snapshot(Value? Run, Value? Approval);
    static Value? Read(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        var data = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return data == null ? null : new(data, key!.GetValueKind(ValueName));
    }
    static void Write(string path, Value? value)
    {
        if (value == null) { using var key = Registry.CurrentUser.OpenSubKey(path, writable: true); key?.DeleteValue(ValueName, false); }
        else { using var key = Registry.CurrentUser.CreateSubKey(path, writable: true); key.SetValue(ValueName, value.Data, value.Kind); }
    }
    internal Snapshot Capture() => new(Read(runKey), Read(approvalKey));
    internal bool Enabled
    {
        get
        {
            var state = Capture();
            return state.Run?.Data is string command && command.Equals(Command, StringComparison.OrdinalIgnoreCase)
                && !(state.Approval?.Data is byte[] bytes && bytes.Length > 0 && bytes[0] is 3 or 7);
        }
    }
    internal void Restore(Snapshot state) { Write(runKey, state.Run); Write(approvalKey, state.Approval); }
    internal void SetEnabled(bool enabled)
    {
        if (enabled && Command.Length > 260) throw new IOException(L.T("程序路径过长，无法设置开机启动。"));
        var previous = Capture();
        try
        {
            Write(runKey, enabled ? new Value(Command, RegistryValueKind.String) : null);
            // A previous Task Manager disable must not silently defeat an explicit enable.
            Write(approvalKey, null);
        }
        catch
        {
            Restore(previous); throw;
        }
    }
}
