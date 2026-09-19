using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace TrayPilot;

internal sealed class StartupTask
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    readonly string name;
    readonly string user = CurrentUser();
    static string CurrentUser() { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; }
    internal StartupTask(string? testKey) => name = testKey == null
        ? "TrayPilot-" + user : "TrayPilot-Test-" + testKey.Split('\\').Last();

    // Scope every COM reference; settings are changed infrequently and need no resident service object.
    sealed class Connection : IDisposable
    {
        readonly List<object> references = new();
        internal dynamic Keep(object value) { references.Add(value); return value; }
        internal dynamic Folder { get; }
        internal Connection()
        {
            try
            {
                dynamic service = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!);
                service.Connect();
                Folder = Keep(service.GetFolder("\\"));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            for (int i = references.Count - 1; i >= 0; i--) Marshal.ReleaseComObject(references[i]);
        }
    }
    internal string? Read()
    {
        using var connection = new Connection();
        try { return (string)connection.Keep(connection.Folder.GetTask(name)).Xml; }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
    }
    internal void Write(string? xml)
    {
        using var connection = new Connection();
        if (xml == null)
        {
            try { connection.Folder.DeleteTask(name, 0); }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { }
        }
        else connection.Keep(connection.Folder.RegisterTask(name, xml, 6, user, null, 3, null));
    }
    internal static bool Enabled(string xml, string executable)
    {
        var task = XElement.Parse(xml);
        var action = task.Element(Ns + "Actions")?.Element(Ns + "Exec");
        return (string?)task.Element(Ns + "Settings")?.Element(Ns + "Enabled") != "false"
            && task.Element(Ns + "Triggers")?.Elements(Ns + "LogonTrigger")
                .Any(t => (string?)t.Element(Ns + "Enabled") != "false") == true
            && string.Equals((string?)action?.Element(Ns + "Command"), executable, StringComparison.OrdinalIgnoreCase)
            && (string?)action?.Element(Ns + "Arguments") == "--startup";
    }
    internal string CreateXml(string executable) => new XElement(Ns + "Task", new XAttribute("version", "1.2"),
        new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", "Start TrayPilot immediately when its user signs in.")),
        new XElement(Ns + "Triggers", new XElement(Ns + "LogonTrigger",
            new XElement(Ns + "Enabled", true), new XElement(Ns + "UserId", user), new XElement(Ns + "Delay", "PT0S"))),
        new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XAttribute("id", "User"),
            new XElement(Ns + "UserId", user), new XElement(Ns + "LogonType", "InteractiveToken"),
            new XElement(Ns + "RunLevel", "LeastPrivilege"))),
        new XElement(Ns + "Settings",
            new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
            new XElement(Ns + "DisallowStartIfOnBatteries", false), new XElement(Ns + "StopIfGoingOnBatteries", false),
            new XElement(Ns + "StartWhenAvailable", true), new XElement(Ns + "RunOnlyIfNetworkAvailable", false),
            new XElement(Ns + "Enabled", true), new XElement(Ns + "RunOnlyIfIdle", false),
            new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
            // Task Scheduler priority 3 maps to ABOVE_NORMAL_PRIORITY_CLASS.
            new XElement(Ns + "Priority", 3)),
        new XElement(Ns + "Actions", new XAttribute("Context", "User"), new XElement(Ns + "Exec",
            new XElement(Ns + "Command", executable), new XElement(Ns + "Arguments", "--startup"),
            new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))
        .ToString(SaveOptions.DisableFormatting);
}
