using Microsoft.Win32;

namespace TrayPilot;

internal sealed class StartupRegistration
{
    const string ValueName = "TrayPilot";
    readonly string runKey, approvalKey;
    readonly StartupTask task;
    readonly string executable;
    internal string Command { get; }
    internal StartupRegistration(string? testKey = null, string? executable = null)
    {
        runKey = testKey ?? @"Software\Microsoft\Windows\CurrentVersion\Run";
        approvalKey = testKey == null ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" : testKey + @"\Approval";
        this.executable = executable ?? Environment.ProcessPath ?? Application.ExecutablePath;
        task = new StartupTask(testKey);
        Command = "\"" + this.executable + "\" --startup";
    }
    internal sealed record Value(object Data, RegistryValueKind Kind);
    internal sealed record Snapshot(Value? Run, Value? Approval, string? TaskXml = null);
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
    internal Snapshot Capture() => new(Read(runKey), Read(approvalKey), task.Read());
    internal bool Enabled
    {
        get
        {
            var state = Capture();
            if (state.TaskXml != null) return StartupTask.Enabled(state.TaskXml, executable);
            return state.Run?.Data is string command && command.Equals(Command, StringComparison.OrdinalIgnoreCase)
                && !(state.Approval?.Data is byte[] bytes && bytes.Length > 0 && bytes[0] is 3 or 7);
        }
    }
    internal void Restore(Snapshot state) { task.Write(state.TaskXml); Write(runKey, state.Run); Write(approvalKey, state.Approval); }
    internal void UpgradeLegacy()
    {
        if (Read(runKey)?.Data is string command && command.Equals(Command, StringComparison.OrdinalIgnoreCase)
            && !(Read(approvalKey)?.Data is byte[] bytes && bytes.Length > 0 && bytes[0] is 3 or 7)
            && task.Read() == null) SetEnabled(true);
    }
    internal void SetEnabled(bool enabled)
    {
        var previous = Capture();
        try
        {
            task.Write(enabled ? task.CreateXml(executable) : null);
            Write(runKey, null);
            // A previous Task Manager disable must not silently defeat an explicit enable.
            Write(approvalKey, null);
        }
        catch
        {
            Restore(previous); throw;
        }
    }
}
