using System.Runtime.InteropServices;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestPackage(string report)
    {
        var log = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new IOException(message); log.Add("PASS: " + message); }
        string root = Path.Combine(Path.GetTempPath(), "TrayPilot-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var packs = L.Packs();
            Check(packs.ContainsKey("en-US") && packs.ContainsKey("zh-CN"), "English and Chinese are available from the package.");
            L.Set("zh-CN"); Check(L.T("about") == "关于", "Embedded Chinese translations load.");
            string path = NativeHelper.Extract(root);
            var library = NativeLibrary.Load(path);
            try
            {
                foreach (string export in new[] { "Attach", "AttachWithSymbols", "IdentifyGlyph", "DllGetClassObject", "DllCanUnloadNow" })
                    Check(NativeLibrary.GetExport(library, export) != 0, "Embedded helper exports " + export + ".");
                Check(NativeHelper.Extract(root) == path, "An already-loaded cached helper can be reused.");
            }
            finally { NativeLibrary.Free(library); }
            File.WriteAllText(path, "damaged cache");
            Check(NativeHelper.Extract(root) == path && new FileInfo(path).Length > 1024, "A damaged helper cache is repaired.");
            string[] paths = new string[8];
            Parallel.For(0, paths.Length, i => paths[i] = NativeHelper.Extract(Path.Combine(root, "parallel")));
            Check(paths.All(p => p == paths[0]), "Concurrent extraction publishes one complete helper.");
            File.WriteAllLines(report, log);
            return 0;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
