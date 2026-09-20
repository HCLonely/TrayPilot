using System.Diagnostics;
using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetGuiResources(nint process, uint flags);
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
    static int TestRefreshPerformance(string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new MainForm(new Controller(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,
            "refresh-state-" + Guid.NewGuid().ToString("N"))), initialize: false);
        var entriesField = typeof(MainForm).GetField("entries", flags)!;
        var render = typeof(MainForm).GetMethod("RenderList", flags)!;
        form.Show(); Application.DoEvents();
        var entries = new List<TrayEntry>();
        var random = new Random(42);
        for (int i = 0; i < 80; i++)
        {
            using var bitmap = new Bitmap(32, 32);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(255, random.Next(256), random.Next(256), random.Next(256)));
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            entries.Add(new TrayEntry { Path = Environment.ProcessPath!, Name = "Snapshot " + i, Pid = (uint)(1000 + i),
                Guid = Guid.NewGuid(), IconSnapshot = stream.ToArray() });
        }
        entriesField.SetValue(form, entries);
        render.Invoke(form, null);
        var log = new List<string>();
        void Measure(string name, int count, Action action)
        {
            for (int i = 0; i < 5; i++) action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) action();
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            log.Add($"{name}: iterations={count}; elapsed={watch.Elapsed.TotalMilliseconds:F2} ms; allocated={allocated} bytes");
        }
        Measure("80 unchanged snapshot rows", 100, () => render.Invoke(form, null));
        Measure("One changed state among 80 rows", 30, () =>
        {
            entries[0] = entries[0] with { State = 1 - entries[0].State }; render.Invoke(form, null);
        });
        Measure("Full scan", 5, () => Scanner.Scan());
        File.WriteAllLines(report, log);
        return 0;
    }

    static int TestRefreshRegression(string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var log = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); log.Add("PASS: " + message); }
        var controller = new Controller(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "refresh-regression-" + Guid.NewGuid().ToString("N")));
        using var form = new MainForm(controller, initialize: false);
        var render = typeof(MainForm).GetMethod("RenderList", flags)!;
        var entriesField = typeof(MainForm).GetField("entries", flags)!;
        var list = (ListView)typeof(MainForm).GetField("list", flags)!.GetValue(form)!;
        byte[] Snapshot(Color color)
        {
            using var bitmap = new Bitmap(32, 32); using var graphics = Graphics.FromImage(bitmap); graphics.Clear(color);
            using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray();
        }
        var entry = new TrayEntry { Name = "Test", Path = Environment.ProcessPath!, Guid = Guid.NewGuid(), IconSnapshot = Snapshot(Color.Red) };
        entriesField.SetValue(form, new List<TrayEntry> { entry }); render.Invoke(form, null);
        var original = list.Items[0]; var images = list.SmallImageList;
        entriesField.SetValue(form, new List<TrayEntry> { entry with { IconSnapshot = entry.IconSnapshot!.ToArray() } });
        render.Invoke(form, null);
        Check(ReferenceEquals(original, list.Items[0]) && ReferenceEquals(images, list.SmallImageList), "Identical bytes from a new scan preserve rows and ImageLists.");
        entry = entry with { State = 1 };
        entriesField.SetValue(form, new List<TrayEntry> { entry }); render.Invoke(form, null);
        Check(ReferenceEquals(original, list.Items[0]) && ((TrayEntry)original.Tag!).State == 1, "State changes update the existing row.");
        controller.AddRule(entry.Path); render.Invoke(form, null);
        Check(((TrayListItem)original).MatchesRule, "Rule changes update the existing row marker.");
        entry = entry with { State = 0, IconSnapshot = Snapshot(Color.Blue) };
        entriesField.SetValue(form, new List<TrayEntry> { entry }); render.Invoke(form, null);
        using (var bitmap = (Bitmap)list.SmallImageList!.Images[0])
            Check(bitmap.GetPixel(14, 14).B > 200, "Changed snapshot bytes invalidate the cached image.");
        using (var cache = new TrayImageCache())
        {
            using var retained = cache.Create(entry, 28);
            for (int i = 0; i < 300; i++) { using var image = cache.Create(entry with { Guid = Guid.NewGuid() }, 28); }
            var items = (System.Collections.IDictionary)typeof(TrayImageCache).GetField("images", flags)!.GetValue(cache)!;
            Check(items.Count == 256 && retained.GetPixel(14, 14).B > 200, "Cache remains bounded and eviction preserves caller-owned images.");
        }
        using (var process = Process.GetCurrentProcess())
        {
            uint before = GetGuiResources(process.Handle, 0);
            for (int i = 0; i < 300; i++)
            {
                entry = entry with { State = i % 2 };
                entriesField.SetValue(form, new List<TrayEntry> { entry }); render.Invoke(form, null);
            }
            uint after = GetGuiResources(process.Handle, 0);
            Check(after <= before + 10, $"300 state changes keep GDI resources bounded ({before} -> {after}).");
        }
        typeof(MainForm).GetField("initialized", flags)!.SetValue(form, true);
        entriesField.SetValue(form, new List<TrayEntry>()); render.Invoke(form, null);
        Check(list.Items.Count == 1, "Hidden background forms skip rendering.");
        typeof(MainForm).GetMethod("UpdateTimer", flags)!.Invoke(form, null);
        var timer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("timer", flags)!.GetValue(form)!;
        ((CheckBox)typeof(MainForm).GetField("autoRefresh", flags)!.GetValue(form)!).Checked = false;
        Check(timer.Enabled && timer.Interval == 2500, "Rules continue running when automatic list refresh is disabled.");
        controller.RemoveRule(entry.Path);
        ((CheckBox)typeof(MainForm).GetField("autoRefresh", flags)!.GetValue(form)!).Checked = true;
        Check(timer.Enabled && timer.Interval == 15000, "An idle background form uses a 15-second refresh interval.");
        timer.Stop();
        File.WriteAllLines(report, log); return 0;
    }
}
