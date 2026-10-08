using System.Diagnostics;
using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static void TestBatchRecovery(Controller controller, List<TrayEntry> entries, Action<bool, string> check, string temporaryFile)
    {
        void RejectSave(Action action)
        {
            Directory.CreateDirectory(temporaryFile);
            bool rejected = false;
            try { action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
            finally { Directory.Delete(temporaryFile); }
            check(rejected, "Batch operation reports a recovery-journal write failure.");
        }
        RejectSave(() => controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult());
        check(entries.All(x => Native.State(x) == 0) && controller.Saved.Recovery.Count == 0,
            "Failed batch journaling leaves every icon visible and rolls back in-memory records.");
        controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
        check(entries.All(x => Native.State(x) == 1) && new Controller(Path.GetDirectoryName(temporaryFile)!).Saved.Recovery.Count == entries.Count,
            "A batch hide persists recovery records for every hidden icon.");
        RejectSave(() => controller.ChangeManyAsync(entries, false, asynchronous: false).GetAwaiter().GetResult());
        check(entries.All(x => Native.State(x) == 0) && controller.Saved.Recovery.Count == entries.Count &&
            new Controller(Path.GetDirectoryName(temporaryFile)!).Saved.Recovery.Count == entries.Count,
            "Failed restoration save retains both memory and disk recovery records after icons are shown.");
        controller.RestoreManaged();
        check(controller.Saved.Recovery.Count == 0, "Retrying restoration safely clears the retained batch journal.");
    }
}
