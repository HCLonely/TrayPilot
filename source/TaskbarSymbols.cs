using System.Buffers.Binary;
using System.Net.Http;
using System.Reflection.PortableExecutable;

namespace TrayPilot;

// Fetch the exact PDB over HTTPS without requiring symsrv.dll beside dbghelp.dll.
// DbgHelp still validates the PDB against the loaded image before resolving symbols.
internal static class TaskbarSymbols
{
    internal sealed record Identity(string FileName, Guid Guid, int Age)
    {
        internal string Key => Guid.ToString("N").ToUpperInvariant() + Age.ToString("X");
    }
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };
    static readonly SemaphoreSlim Gate = new(1, 1);
    const int MaxSize = 128 * 1024 * 1024;

    internal static Identity ReadIdentity(string image)
    {
        using var stream = File.OpenRead(image);
        using var pe = new PEReader(stream);
        foreach (var entry in pe.ReadDebugDirectory())
        {
            if (entry.Type != DebugDirectoryEntryType.CodeView) continue;
            var data = pe.ReadCodeViewDebugDirectoryData(entry);
            string name = Path.GetFileName(data.Path);
            if (!name.Equals("Taskbar.pdb", StringComparison.OrdinalIgnoreCase) || data.Age < 1)
                throw new InvalidDataException("Unexpected taskbar symbol identity.");
            return new(name, data.Guid, data.Age);
        }
        throw new InvalidDataException("taskbar.dll has no CodeView symbol identity.");
    }

    internal static async Task<string> PrepareAsync()
    {
        var identity = ReadIdentity(Path.Combine(Environment.SystemDirectory, "taskbar.dll"));
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayPilot", "symbols", identity.FileName, identity.Key);
        string file = Path.Combine(folder, identity.FileName);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Matches(file, identity)) return folder;
            Directory.CreateDirectory(folder);
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var url = new Uri($"https://msdl.microsoft.com/download/symbols/{identity.FileName}/{identity.Key}/{identity.FileName}");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxSize) throw new InvalidDataException("Symbol file exceeds size limit.");
                await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int count; long total = 0;
                    while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        total += count;
                        if (total > MaxSize) throw new InvalidDataException("Symbol file exceeds size limit.");
                        await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    }
                }
                if (!Matches(temporary, identity)) throw new InvalidDataException("Downloaded taskbar symbols do not match the installed Windows build.");
                File.Move(temporary, file, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return folder;
        }
        finally { Gate.Release(); }
    }

    // Read the PDB information stream (MSF 7). Reject stale, partial or corrupt
    // caches before publishing them, including a PDB with the wrong GUID/age.
    internal static bool Matches(string file, Identity identity)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length < 56 || info.Length > MaxSize) return false;
            byte[] bytes = File.ReadAllBytes(file);
            if (!bytes.AsSpan(0, 32).SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8)) return false;
            int Read(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
            int pageSize = Read(bytes, 32), directorySize = Read(bytes, 44), map = Read(bytes, 52);
            if (pageSize is < 512 or > 65536 || (pageSize & (pageSize - 1)) != 0 || directorySize < 12 || directorySize > MaxSize) return false;
            var directory = new byte[directorySize];
            for (int offset = 0; offset < directorySize; offset += pageSize)
            {
                int page = Read(bytes, checked(map * pageSize + offset / pageSize * 4));
                bytes.AsSpan(checked(page * pageSize), Math.Min(pageSize, directorySize - offset)).CopyTo(directory.AsSpan(offset));
            }
            int streams = Read(directory, 0);
            if (streams < 4 || streams > (directorySize - 4) / 4) return false;
            int StreamStart(int index, int minimumSize)
            {
                if (Read(directory, 4 + index * 4) < minimumSize) throw new InvalidDataException("Truncated PDB stream.");
                int pages = 0;
                for (int i = 0; i < index; i++)
                {
                    int size = Read(directory, 4 + i * 4);
                    if (size < -1) throw new InvalidDataException("Invalid PDB stream size.");
                    if (size > 0) pages = checked(pages + checked(size + pageSize - 1) / pageSize);
                }
                return checked(Read(directory, checked(4 + streams * 4 + pages * 4)) * pageSize);
            }
            int start = StreamStart(1, 28), dbi = StreamStart(3, 12);
            // Public/stripped PDBs can have a newer information-stream age.
            // The DBI age retains the executable's age (e.g. 3 vs 1 on 26200.9457).
            return Read(bytes, checked(dbi + 8)) == identity.Age && Read(bytes, checked(start + 8)) >= identity.Age &&
                new Guid(bytes.AsSpan(checked(start + 12), 16)) == identity.Guid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { return false; }
    }
}
