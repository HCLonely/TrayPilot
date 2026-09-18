using System.Buffers.Binary;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestSymbolCache(string report)
    {
        var log = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new IOException(message);
            log.Add("PASS " + message);
        }
        var identity = TaskbarSymbols.ReadIdentity(Path.Combine(Environment.SystemDirectory, "taskbar.dll"));
        string folder = TaskbarSymbols.PrepareAsync().GetAwaiter().GetResult();
        string file = Path.Combine(folder, identity.FileName);
        Check(TaskbarSymbols.Matches(file, identity), "Downloaded PDB matches the installed taskbar GUID and DBI age");
        DateTime written = File.GetLastWriteTimeUtc(file);
        Check(TaskbarSymbols.PrepareAsync().GetAwaiter().GetResult() == folder && File.GetLastWriteTimeUtc(file) == written,
            "Valid cache is reused without a download or rewrite");
        Check(!TaskbarSymbols.Matches(file, identity with { Guid = Guid.NewGuid() }), "Wrong GUID is rejected");
        Check(!TaskbarSymbols.Matches(file, identity with { Age = identity.Age + 1 }), "Wrong executable age is rejected");
        string temporary = Path.GetFullPath(report) + ".fixture-" + Guid.NewGuid().ToString("N");
        try
        {
            // A public PDB can have info age 3 but executable/DBI age 1.
            var bytes = new byte[6 * 512];
            "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8.CopyTo(bytes);
            void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
            Write(32, 512); Write(40, 6); Write(44, 28); Write(52, 1);
            Write(512, 2); // Directory page.
            Write(1024, 4); Write(1028, -1); Write(1032, 28); Write(1036, -1); Write(1040, 12);
            Write(1044, 3); Write(1048, 4); // Stream 1 and 3 pages.
            Write(1536 + 8, 3); identity.Guid.TryWriteBytes(bytes.AsSpan(1536 + 12, 16));
            Write(2048 + 8, 1);
            File.WriteAllBytes(temporary, bytes);
            Check(TaskbarSymbols.Matches(temporary, identity with { Age = 1 }), "Public PDB info-age increment is accepted when DBI age matches");
            File.WriteAllBytes(temporary, bytes[..100]);
            Check(!TaskbarSymbols.Matches(temporary, identity), "Truncated cache is rejected");
            Write(52, int.MaxValue); File.WriteAllBytes(temporary, bytes);
            Check(!TaskbarSymbols.Matches(temporary, identity), "Corrupt page offsets are rejected without crashing");
            File.WriteAllText(temporary, "<html>symbol server error</html>");
            Check(!TaskbarSymbols.Matches(temporary, identity), "Non-PDB response is rejected");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); File.WriteAllLines(report, log); }
        return 0;
    }
}
