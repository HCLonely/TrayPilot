using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestExplorerImages(string report)
    {
        var checks = new List<string>();
        void Check(bool passed, string message)
        {
            if (!passed) throw new IOException(message);
            checks.Add("PASS " + message);
        }
        using var mapping = MemoryMappedFile.CreateNew(null, ExplorerIconImage.MappingSize);
        using var view = mapping.CreateViewAccessor();
        const int offset = ExplorerIconImage.Offset;
        const long now = 10000;
        void Frame(int width = 2, int height = 1, int error = 0, long timestamp = now, int sequence = 2)
        {
            view.Write(offset, sequence);
            view.Write(offset + 4, width);
            view.Write(offset + 8, height);
            view.Write(offset + 12, error);
            view.Write(offset + 16, timestamp);
            view.WriteArray(offset + 24, new byte[] { 0, 0, 255, 255, 64, 0, 0, 128 }, 0, 8);
        }
        Check(ExplorerIconImage.Read(view, 0, now) == null, "Unpublished image selects fallback.");
        Frame();
        var frame = ExplorerIconImage.Read(view, 0, now);
        Check(frame is { Width: 2, Height: 1 }, "Coherent shared BGRA frame is accepted.");
        using (var bitmap = frame!.CreateBitmap())
        {
            Check(bitmap!.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "Native red pixels retain their color.");
            var translucent = bitmap.GetPixel(1, 0);
            Check(translucent.A == 128 && Math.Abs(translucent.B - 128) <= 1, "Premultiplied alpha is decoded without dark fringes.");
        }
        Frame(sequence: 3); Check(ExplorerIconImage.Read(view, 0, now) == null, "In-progress writer selects fallback.");
        Frame(error: unchecked((int)0x80004005)); Check(ExplorerIconImage.Read(view, 0, now) == null, "Explorer renderer failure selects fallback.");
        Frame(width: 193); Check(ExplorerIconImage.Read(view, 0, now) == null, "Oversized frame is rejected before allocation.");
        Frame(height: -1); Check(ExplorerIconImage.Read(view, 0, now) == null, "Invalid dimensions select fallback.");
        Frame(timestamp: now - 3001); Check(ExplorerIconImage.Read(view, 0, now) == null, "Stalled/expired image selects fallback.");
        Frame(timestamp: now + 1); Check(ExplorerIconImage.Read(view, 0, now) == null, "Invalid future timestamp selects fallback.");
        Frame(); view.Write(offset + 27, (byte)0); view.Write(offset + 31, (byte)0);
        Check(ExplorerIconImage.Read(view, 0, now) == null, "Fully transparent image selects fallback.");
        Frame();
        Check(ExplorerIconImage.Read(view, 0, now) != null, "A successful retry recovers after a failed frame.");
        Check(new ExplorerIconImage(2, 1, new byte[1]).CreateBitmap() == null, "Malformed pixel length cannot reach GDI+.");
        Frame();
        int last = ExplorerIconImage.Offset + (ExplorerIconImage.Count - 1) * ExplorerIconImage.SlotSize;
        var slot = new byte[ExplorerIconImage.SlotSize];
        view.ReadArray(offset, slot, 0, slot.Length); view.WriteArray(last, slot, 0, slot.Length);
        Check(ExplorerIconImage.Read(view, 11, now) != null, "Final native slot fits the shared layout.");
        Check(ExplorerIconImage.Read(view, 12, now) == null, "Out-of-range slot is rejected.");
        File.WriteAllLines(report, checks);
        return 0;
    }

    static int ProbeExplorerImages(string report)
    {
        using var session = new SystemIconSession();
        var images = new ExplorerIconImage?[12];
        for (int i = 0; i < 60 && session.Error == 0; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
            images = session.ReadImages();
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(report))!;
        for (int i = 0; i < images.Length; i++)
        {
            using var bitmap = images[i]?.CreateBitmap();
            bitmap?.Save(Path.Combine(directory, $"explorer-{1 << i}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            connected = session.Ticks > 0 && session.Error == 0,
            found = session.Found, hidden = session.Hidden, error = $"0x{session.Error:X8}",
            images = SystemIconCatalog.Items.Select((item, index) => new
            {
                item.Mask, item.Name, native = images[index] != null,
                width = images[index]?.Width ?? 0, height = images[index]?.Height ?? 0
            }),
            diagnostic = session.DebugInfo(),
            isolation = "Read-only Explorer control rendering; no screen capture or visibility changes."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return session.Ticks > 0 && session.Error == 0 && images.Any(image => image != null) ? 0 : 1;
    }
}
