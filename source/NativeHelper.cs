using System.Security.Cryptography;

namespace TrayPilot;

internal static class NativeHelper
{
    internal static string Extract(string? root = null)
    {
        using var resource = typeof(NativeHelper).Assembly.GetManifestResourceStream("TrayPilot.native.TrayPilot.Xaml.dll")
            ?? throw new IOException("Embedded taskbar helper is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        byte[] digest = SHA256.HashData(bytes);
        string directory = Path.Combine(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayPilot", "native"), Convert.ToHexString(digest));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "TrayPilot.Xaml.dll");
        string lockId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\TrayPilot.NativeExtract." + lockId);
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(15))) throw new IOException("Timed out extracting the taskbar helper."); }
        catch (AbandonedMutexException) { } // A crashed writer can leave only its unique temporary file.
        try
        {
            if (File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(digest)) return path;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally { File.Delete(temporary); }
            return path;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
