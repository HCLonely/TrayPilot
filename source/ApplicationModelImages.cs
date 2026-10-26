using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayPilot;

internal sealed record ApplicationModelImage(long Window, uint Id, Guid Guid, int Error, string Model, string ImageType, int Width, int Height,
    [property: System.Text.Json.Serialization.JsonIgnore] byte[] Bytes)
{
    internal byte[]? ToPng()
    {
        if (Error != 0 || Bytes.Length == 0 || Bytes.Length > ApplicationModelImages.MaxImageBytes) return null;
        try
        {
            using var output = new MemoryStream();
            if (Width != 0 || Height != 0)
            {
                if (Width <= 0 || Height <= 0 || Width > 128 || Height > 128 || Bytes.Length != Width * Height * 4) return null;
                bool visible = false;
                for (int i = 3; i < Bytes.Length; i += 4)
                    if (Bytes[i] != 0) { visible = true; break; }
                if (!visible) return null;
                var handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
                try
                {
                    using var bitmap = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, handle.AddrOfPinnedObject());
                    bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                }
                finally { handle.Free(); }
            }
            else
            {
                using var input = new MemoryStream(Bytes);
                using var bitmap = Image.FromStream(input);
                if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.Width > 128 || bitmap.Height > 128) return null;
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            return output.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException) { return null; }
    }

    internal bool Matches(TrayEntry entry)
    {
        if (Error != 0 || Bytes.Length == 0) return false;
        if (entry.Guid != Guid.Empty ? Guid != entry.Guid : Guid != Guid.Empty || Window != entry.Window || Id != entry.Id) return false;
        Native.GetWindowThreadProcessId((nint)Window, out uint pid);
        return pid == entry.Pid && Native.ProcessStarted(pid) == entry.Started && Controller.SamePath(Native.ProcessPath(pid), entry.Path);
    }
}

internal sealed class ApplicationModelImages : IDisposable
{
    internal const int HeaderSize = 200, RecordSize = 66088, Capacity = 128, MaxImageBytes = 65536;
    internal const int MappingSize = HeaderSize + RecordSize * Capacity;
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int ResolveDelegate([MarshalAs(UnmanagedType.LPWStr)] string directory,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 8)] ulong[] symbols);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int ResolveViewDelegate([MarshalAs(UnmanagedType.LPWStr)] string image,
        [MarshalAs(UnmanagedType.LPWStr)] string directory, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 12)] ulong[] symbols);
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    internal int Error => view.ReadInt32(12);
    internal int Ticks => view.ReadInt32(16);
    internal ulong[] ViewSymbols { get; } = new ulong[12];

    internal ApplicationModelImages(string sessionName, string symbolsDirectory, nint library)
    {
        mapping = MemoryMappedFile.CreateNew(sessionName + ".Applications", MappingSize);
        view = mapping.CreateViewAccessor();
        try
        {
            var symbols = new ulong[8];
            int status = Marshal.GetDelegateForFunctionPointer<ResolveDelegate>(NativeLibrary.GetExport(library,
                "ProbeApplicationSymbols"))(symbolsDirectory, symbols);
            Marshal.ThrowExceptionForHR(status);
            if (symbols.Any(symbol => symbol == 0)) throw new NotSupportedException("Application image model symbols are incomplete.");
            string image = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps",
                "MicrosoftWindows.Client.Core_cw5n1h2txyewy", "SystemTray.dll");
            string viewDirectory = TaskbarSymbols.PrepareAsync(image).GetAwaiter().GetResult();
            var viewSymbols = ViewSymbols;
            status = Marshal.GetDelegateForFunctionPointer<ResolveViewDelegate>(NativeLibrary.GetExport(library,
                "ProbeSystemTraySymbols"))(image, viewDirectory, viewSymbols);
            Marshal.ThrowExceptionForHR(status);
            if (viewSymbols.Any(symbol => symbol == 0)) throw new NotSupportedException("Application view symbols are incomplete.");
            view.Write(0, 3);
            view.WriteArray(32, symbols, 0, symbols.Length);
            view.WriteArray(96, viewSymbols, 0, viewSymbols.Length);
        }
        catch { Dispose(); throw; }
    }

    internal ApplicationModelImage[] Read() => Read(view);

    internal static ApplicationModelImage[] Read(MemoryMappedViewAccessor view)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int sequence = view.ReadInt32(4);
            if (sequence == 0 || (sequence & 1) != 0) continue;
            Thread.MemoryBarrier();
            int count = view.ReadInt32(8);
            long updated = view.ReadInt64(192), now = Environment.TickCount64;
            if (view.ReadInt32(12) != 0 || count < 0 || count > Capacity || updated <= 0 || updated > now || now - updated > 3000) return Array.Empty<ApplicationModelImage>();
            var result = new ApplicationModelImage[count];
            for (int i = 0; i < count; i++)
            {
                long offset = HeaderSize + i * RecordSize;
                var header = new byte[552];
                view.ReadArray(offset, header, 0, header.Length);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32));
                int error = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28));
                uint dimensions = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(36));
                if (length < 0 || length > MaxImageBytes) return Array.Empty<ApplicationModelImage>();
                var bytes = new byte[length];
                if (length != 0) view.ReadArray(offset + 552, bytes, 0, length);
                result[i] = new(BinaryPrimitives.ReadInt64LittleEndian(header), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)),
                    new Guid(header.AsSpan(12, 16)), error, Encoding.Unicode.GetString(header, 40, 256).Split('\0')[0],
                    Encoding.Unicode.GetString(header, 296, 256).Split('\0')[0], (int)(dimensions & 65535), (int)(dimensions >> 16), bytes);
            }
            Thread.MemoryBarrier();
            if (sequence == view.ReadInt32(4)) return result;
        }
        return Array.Empty<ApplicationModelImage>();
    }

    public void Dispose() { view.Dispose(); mapping.Dispose(); }
}
