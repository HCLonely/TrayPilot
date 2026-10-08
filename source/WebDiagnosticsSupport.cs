using System.Reflection;
namespace TrayPilot;
internal static partial class Diagnostics
{
    static readonly BindingFlags DashboardFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T DashboardField<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, DashboardFlags)!.GetValue(form)!;
    static void DashboardCall(MainForm form, string name) => typeof(MainForm).GetMethod(name, DashboardFlags)!.Invoke(form, null);
    static SystemIconsSnapshot SystemDashboardSample(bool loading = false, bool failed = false) => new(!loading && !failed, loading,
        16 | 1024 | 2048, 1 | 2 | 8 | 256 | 1024 | 2048, 1024 | 2048, false, false,
        failed ? "Preview: connection unavailable (no native session started)" : "", "",
        new (string, string)[12]);

}
