using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TrayPilot;

// The interface declarations describe Explorer's undocumented tray COM ABI.
// Verified against Chromium's status_tray_state_changer_win.cc. In particular,
// UnregisterCallback takes a pointer to a 32-bit cookie, not a cookie by value.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
[ComVisible(true)]
public struct TrayNotifyItem
{
    [MarshalAs(UnmanagedType.LPWStr)] public string? Executable;
    [MarshalAs(UnmanagedType.LPWStr)] public string? Tooltip;
    public nint Icon, Window;
    public uint Preference, Id;
    public Guid Guid;
}

[ComVisible(true), Guid("D782CCBA-AFB0-43F1-94DB-FDA3779EACCB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITrayImageCallback
{
    [PreserveSig] int Notify(uint kind, [In] ref TrayNotifyItem item);
}

[ComImport, Guid("D133CE13-3537-48BA-93A7-AFCD5D2053B4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITrayImageNotify
{
    [PreserveSig] int RegisterCallback([MarshalAs(UnmanagedType.Interface)] ITrayImageCallback callback, out uint cookie);
    [PreserveSig] int UnregisterCallback(ref uint cookie);
    // Intentionally omit mutating methods: this reader never sets preferences.
}

internal sealed record TrayNotifyImage(TrayEntry Owner, byte[] Png, long Updated)
{
    internal bool Matches(TrayEntry entry) => Owner.Key == entry.Key && Owner.Pid == entry.Pid &&
        Owner.Started == entry.Started && Controller.SamePath(Owner.Path, entry.Path);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TrayNotifyReader : ITrayImageCallback, IDisposable
{
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("user32.dll", SetLastError = true)]
    static extern nint CopyIcon(nint icon);

    readonly Dictionary<string, TrayNotifyImage> images = new();
    readonly object gate = new();
    ITrayImageNotify? connection;
    uint cookie;
    bool registered;
    volatile bool disposed;
    int callbacks, imageFailures;
    internal int Error { get; private set; }
    internal bool Connected => registered && !disposed;
    internal int Callbacks => Volatile.Read(ref callbacks);
    internal int ImageFailures => Volatile.Read(ref imageFailures);
    internal TrayNotifyImage[] Snapshot() { lock (gate) return images.Values.ToArray(); }

    internal bool Connect()
    {
        if (disposed || registered) return Connected;
        Guid clsid = new("25DEAD04-1EAC-4911-9E3A-AD0A4AB560FD");
        Guid iid = typeof(ITrayImageNotify).GUID;
        nint instance = 0;
        try
        {
            Error = CoCreateInstance(ref clsid, 0, 4, ref iid, out instance);
            if (Error < 0) return false;
            connection = (ITrayImageNotify)Marshal.GetObjectForIUnknown(instance);
            Error = connection.RegisterCallback(this, out cookie);
            registered = Error >= 0;
            return registered;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Error = ex.HResult;
            return false;
        }
        finally { if (instance != 0) Marshal.Release(instance); }
    }

    int ITrayImageCallback.Notify(uint kind, ref TrayNotifyItem item)
    {
        if (disposed) return 0;
        Interlocked.Increment(ref callbacks);
        string? key = null;
        try
        {
            // Explorer also enumerates historical records. Accept only live owners.
            if (!Native.IsWindow(item.Window)) return 0;
            Native.GetWindowThreadProcessId(item.Window, out uint pid);
            string path = Scanner.ExpandPath(item.Executable ?? "");
            long started = Native.ProcessStarted(pid);
            if (pid == 0 || started == 0 || path.Length == 0 || !Controller.SamePath(path, Native.ProcessPath(pid))) return 0;
            var entry = new TrayEntry { Window = item.Window, Id = item.Id, Guid = item.Guid,
                Pid = pid, Started = started, Path = path, Tooltip = item.Tooltip ?? "" };
            key = entry.Key;
            if (kind == 2)
            {
                lock (gate) images.Remove(entry.Key);
                return 0;
            }
            if (kind is not (0 or 1)) return 0;
            // Copy within the callback. Explorer retains ownership of item.Icon.
            nint copy = item.Icon == 0 ? 0 : CopyIcon(item.Icon);
            if (copy == 0)
            {
                lock (gate) images.Remove(entry.Key);
                Interlocked.Increment(ref imageFailures);
                return 0;
            }
            byte[] png;
            try
            {
                using var icon = Icon.FromHandle(copy);
                if (icon.Width <= 0 || icon.Height <= 0 || icon.Width > 256 || icon.Height > 256)
                    throw new InvalidDataException("Invalid notification icon dimensions.");
                using var bitmap = icon.ToBitmap();
                bool visible = false;
                for (int y = 0; y < bitmap.Height && !visible; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if (bitmap.GetPixel(x, y).A != 0) { visible = true; break; }
                if (!visible) throw new InvalidDataException("Transparent notification icon.");
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                png = stream.ToArray();
            }
            finally { Native.DestroyIcon(copy); }
            lock (gate)
            {
                if (!disposed && (images.Count < 512 || images.ContainsKey(entry.Key)))
                    images[entry.Key] = new(entry, png, Environment.TickCount64);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException or InvalidOperationException or OutOfMemoryException)
        {
            if (key != null) { lock (gate) images.Remove(key); }
            Interlocked.Increment(ref imageFailures);
        }
        return 0;
    }

    internal byte[]? GetImage(TrayEntry entry)
    {
        lock (gate)
        {
            if (!Connected || !images.TryGetValue(entry.Key, out var image)) return null;
            return image.Matches(entry) ? image.Png : null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (registered) connection?.UnregisterCallback(ref cookie); }
        catch (COMException) { }
        finally
        {
            registered = false;
            if (connection != null) Marshal.FinalReleaseComObject(connection);
            connection = null;
            lock (gate) images.Clear();
        }
    }
}
