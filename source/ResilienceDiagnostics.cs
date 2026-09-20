using System.Text.Json;

namespace TrayPilot;
internal static partial class Diagnostics
{
    static void TestStartupRecovery(List<TrayEntry> entries, Action<bool, string> check, string folder)
    {
        var controller = new Controller(Path.Combine(folder, "async-startup"));
        controller.ChangeManyAsync(entries, true, asynchronous: false).GetAwaiter().GetResult();
        using var form = new MainForm(controller, initialize: false, restoreOnStartup: true);
        try
        {
            form.Show();
            check(form.Visible && controller.Saved.Recovery.Count == entries.Count,
                "Startup shows the window before recovery begins.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (controller.Saved.Recovery.Count != 0 && watch.ElapsedMilliseconds < 5000)
            { Application.DoEvents(); Thread.Sleep(10); }
            check(controller.Saved.Recovery.Count == 0 && entries.All(x => Native.State(x) == 0),
                "Startup recovery completes through the UI event loop and clears the journal.");
        }
        finally { controller.RestoreManaged(); }
    }
    static int TestResilience(string report)
    {
        var log = new List<string>();
        void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); log.Add("PASS: " + message); }
        string folder = Path.Combine(Path.GetTempPath(), "TrayPilot-resilience-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "settings.json");
        var controller = new Controller(folder);
        controller.Saved.Recovery.Add(new TrayEntry { Path = Environment.ProcessPath!, Name = "Recovery", Guid = Guid.NewGuid() });
        controller.Save();
        controller.Saved.CloseToTray = false;
        controller.Save();
        Check(File.Exists(file + ".bak"), "Saving an existing configuration keeps the previous version.");
        File.WriteAllText(file, "{broken");
        var recovered = new Controller(folder);
        Check(recovered.Saved.Recovery.Count == 1 && recovered.LoadWarning != null, "Corrupt settings load backup recovery records with a warning.");
        Check(Directory.GetFiles(folder, "settings.json.corrupt-*").Any(x => File.ReadAllText(x) == "{broken"), "Corrupt original is preserved verbatim.");
        File.WriteAllText(file, "{\"Recovery\":null}");
        Check(new Controller(folder).Saved.Recovery.Count == 1, "Null recovery collections trigger backup recovery.");
        File.WriteAllText(file, "{\"Version\":99}");
        bool rejected = false;
        try { _ = new Controller(folder); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && File.ReadAllText(file).Contains("99"), "Future configuration versions are preserved and never downgraded.");
        File.WriteAllText(file + ".bak", "broken backup");
        File.WriteAllText(file, "{\"HiddenIcons\":[null]}");
        rejected = false;
        try { _ = new Controller(folder); } catch (JsonException) { rejected = true; }
        Check(rejected && File.ReadAllText(file).Contains("null"), "Invalid primary and backup fail without discarding either journal.");
        File.WriteAllText(file, "{}");
        Check(new Controller(folder).Saved.Version == 1, "Unversioned settings retain backward compatibility.");
        var retry = new Controller(Path.Combine(folder, "retry"));
        retry.Saved.Recovery.Add(new TrayEntry { Path = Environment.ProcessPath!, Name = "Exited owner" });
        retry.Save();
        string temporaryFile = Path.Combine(folder, "retry", "settings.json.tmp");
        Directory.CreateDirectory(temporaryFile);
        rejected = false;
        try { retry.RestoreManagedAsync().GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
        Check(rejected && retry.Saved.Recovery.Count == 1 && new Controller(Path.Combine(folder, "retry")).Saved.Recovery.Count == 1,
            "Failed async recovery save preserves in-memory and persisted records.");
        Directory.Delete(temporaryFile);
        retry.RestoreManagedAsync().GetAwaiter().GetResult();
        Check(retry.Saved.Recovery.Count == 0, "Async recovery can be retried after a save failure.");
        long time = 0;
        int reads = 0;
        var cache = new NameCache(path => path + ++reads, () => time);
        string original = cache.Get("app");
        Check(cache.Get("APP") == original && reads == 1, "Name cache reuses case-insensitive paths.");
        time = 300000;
        Check(cache.Get("app") != original && reads == 2, "Names refresh after expiration, including replacements at the same path.");
        for (int i = 0; i < 256; i++) cache.Get("other-" + i);
        int before = reads;
        cache.Get("app");
        Check(reads == before + 1, "Name cache evicts old entries at its capacity limit.");
        File.WriteAllLines(report, log);
        return 0;
    }
}
