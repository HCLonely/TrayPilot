using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace TrayPilot;

// BGRA8 premultiplied pixels produced inside Explorer, never read from the screen.
internal sealed record ExplorerIconImage(int Width, int Height, byte[] Pixels)
{
    internal const int Count = 12, MaxWidth = 192, MaxHeight = 64;
    internal const int Offset = 2024, SlotSize = 49176, MappingSize = Offset + Count * SlotSize;

    internal static ExplorerIconImage? Read(MemoryMappedViewAccessor view, int index, long now)
    {
        if (index < 0 || index >= Count) return null;
        long offset = Offset + index * SlotSize;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int sequence = view.ReadInt32(offset);
            if (sequence == 0 || (sequence & 1) != 0) continue;
            Thread.MemoryBarrier();
            int width = view.ReadInt32(offset + 4), height = view.ReadInt32(offset + 8);
            int error = view.ReadInt32(offset + 12);
            long timestamp = view.ReadInt64(offset + 16);
            if (error != 0 || width <= 0 || width > MaxWidth || height <= 0 || height > MaxHeight ||
                timestamp <= 0 || timestamp > now || now - timestamp > 3000) return null;
            var pixels = new byte[width * height * 4];
            view.ReadArray(offset + 24, pixels, 0, pixels.Length);
            Thread.MemoryBarrier();
            if (sequence != view.ReadInt32(offset)) continue;
            bool visible = false;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) { visible = true; break; }
            return visible ? new(width, height, pixels) : null;
        }
        return null;
    }

    internal Bitmap? CreateBitmap()
    {
        if (Width <= 0 || Width > MaxWidth || Height <= 0 || Height > MaxHeight || Pixels.Length != Width * Height * 4)
            return null;
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < Height; row++)
                    Marshal.Copy(Pixels, row * Width * 4, data.Scan0 + row * data.Stride, Width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
