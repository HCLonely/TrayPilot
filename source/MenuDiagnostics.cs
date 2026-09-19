using System.Diagnostics;
using System.Reflection;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestMenuPerformance(string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var controller = new Controller(Path.Combine(Path.GetTempPath(), "TrayPilot-menu-" + Guid.NewGuid().ToString("N")));
        using var form = new MainForm(controller, initialize: false);
        var snapshot = Scanner.Scan();
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, snapshot);
        var build = typeof(MainForm).GetMethod("BuildTrayMenu", flags)!;
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("trayMenu", flags)!.GetValue(form)!;
        var watch = Stopwatch.StartNew();
        build.Invoke(form, null);
        double cold = watch.Elapsed.TotalMilliseconds;
        var samples = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            watch.Restart(); build.Invoke(form, null); samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        // Missing owners/paths must not prevent rendering a snapshot, or trigger disk fallback.
        // Real click actions remain responsible for rejecting stale owners.
        var missing = Enumerable.Range(0, 1000).Select(i => new TrayEntry { Pid = uint.MaxValue,
            Path = @"Z:\unavailable\" + i + ".exe", Name = "Snapshot " + i, Id = (uint)i, State = i % 2 }).ToList();
        typeof(MainForm).GetField("entries", flags)!.SetValue(form, missing);
        watch.Restart(); build.Invoke(form, null); double many = watch.Elapsed.TotalMilliseconds;
        if (menu.Items.OfType<ToolStripMenuItem>().Count(x => x.Tag is string) != MainForm.TrayPageSize)
            throw new Exception("Snapshot menu unexpectedly depends on live owners or files.");
        File.WriteAllLines(report, new[] { $"Icons={snapshot.Count}; cold build={cold:F2} ms",
            $"Warm build: median={samples.Order().ElementAt(samples.Count / 2):F2} ms; max={samples.Max():F2} ms",
            $"1,000 unavailable snapshot icons: first page={many:F2} ms",
            "PASS: Snapshot menus render without querying missing owners or executable paths." });
        return 0;
    }
}
