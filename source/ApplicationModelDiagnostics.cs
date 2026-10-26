using System.Text.Json;
using System.IO.MemoryMappedFiles;

namespace TrayPilot;

internal static partial class Diagnostics
{
    static int TestApplicationModel(string report)
    {
        report = Path.GetFullPath(report);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var checks = new List<string>();
        void Check(bool value, string label)
        {
            if (!value) throw new IOException(label);
            checks.Add("PASS " + label);
        }
        ApplicationModelImage Frame(byte blue, byte green, byte red, byte alpha = 255)
            => new(0, 7, Guid.Empty, 0, "fixture", "SoftwareBitmap", 1, 1, new[] { blue, green, red, alpha });
        var red = Frame(0, 0, 255);
        var green = Frame(0, 255, 0);
        var redPng = red.ToPng()!;
        var greenPng = green.ToPng()!;
        Color Pixel(byte[] png)
        {
            using var input = new MemoryStream(png);
            using var bitmap = new Bitmap(input);
            return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        }
        Check(Pixel(redPng).ToArgb() == Color.Red.ToArgb(), "Explorer BGRA pixels decode to the original red color.");
        Check(Pixel(Frame(64, 0, 0, 128).ToPng()!).A == 128, "Premultiplied alpha survives conversion.");
        Check((red with { Width = 129 }).ToPng() == null, "Oversized model image is rejected.");
        Check((red with { Bytes = new byte[1] }).ToPng() == null, "Incomplete pixel buffer is rejected.");
        Check(Frame(0, 0, 0, 0).ToPng() == null, "Transparent model image selects fallback.");
        Check((red with { Error = -1 }).ToPng() == null, "Model image error selects fallback.");
        var owner = new NativeWindow();
        owner.CreateHandle(new CreateParams { Caption = "Application image identity fixture", Parent = -3 });
        try
        {
            var target = new TrayEntry { Window = owner.Handle, Id = 7, Pid = (uint)Environment.ProcessId,
                Started = Native.ProcessStarted((uint)Environment.ProcessId), Path = Environment.ProcessPath! };
            var image = red with { Window = owner.Handle };
            Check(image.Matches(target with { State = 1 }), "UID image identity remains valid independently of hidden state.");
            Check(!image.Matches(target with { Id = 8 }), "Sibling UID icons cannot share an image accidentally.");
            Check(!image.Matches(target with { Started = target.Started + 1 }), "Reused process identity rejects an older image.");
            Check(!image.Matches(target with { Path = @"C:\wrong.exe" }), "Executable identity is checked before matching pixels.");
            Guid guid = Guid.NewGuid();
            image = image with { Guid = guid };
            Check(image.Matches(target with { Guid = guid, Window = 0 }), "GUID matching uses the real model owner instead of an arbitrary scanned owner window.");
            Check(!image.Matches(target with { Guid = Guid.NewGuid() }), "Different GUIDs cannot share an image.");
        }
        finally { owner.DestroyHandle(); }
        var entry = new TrayEntry { Path = @"C:\missing.exe", IconSnapshot = redPng, LiveIconSnapshot = greenPng };
        using (var bitmap = TrayImages.Create(entry, 32, allowFileAccess: false))
            Check(bitmap.GetPixel(16, 16).ToArgb() == Color.Lime.ToArgb(), "The product image renderer prefers the live application icon.");
        using (var bitmap = TrayImages.Create(entry with { LiveIconSnapshot = new byte[] { 1, 2, 3 } }, 32, allowFileAccess: false))
            Check(bitmap.GetPixel(16, 16).ToArgb() == Color.Red.ToArgb(), "Invalid live image falls back to the original Windows snapshot.");
        using (var bitmap = TrayImages.Create(entry with { LiveIconSnapshot = null }, 32, allowFileAccess: false))
            Check(bitmap.GetPixel(16, 16).ToArgb() == Color.Red.ToArgb(), "Missing live image falls back to the original Windows snapshot.");
        using (var form = new MainForm(new Controller(Path.Combine(Path.GetDirectoryName(report)!, "model-image-ui")), initialize: false))
        {
            var method = typeof(MainForm).GetMethod("WebImage", DashboardFlags)!;
            string Image(TrayEntry item) => ((ValueTuple<string, bool, bool>)method.Invoke(form, new object[] { item })!).Item1;
            byte[] Decode(string uri) => Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
            string first = Image(entry);
            string fallback = Image(entry with { LiveIconSnapshot = null });
            string recovered = Image(entry);
            Check(Pixel(Decode(first)).ToArgb() == Color.Lime.ToArgb(), "Web list and tray-panel image output receives live pixels.");
            Check(Pixel(Decode(fallback)).ToArgb() == Color.Red.ToArgb() && fallback != first,
                "Losing the source invalidates the web image cache and restores fallback.");
            Check(recovered == first, "A successful retry restores the live web image.");
        }

        using var mapping = MemoryMappedFile.CreateNew(null, ApplicationModelImages.MappingSize);
        using var view = mapping.CreateViewAccessor();
        void Publish(int sequence = 2, long? timestamp = null, int length = 4)
        {
            view.Write(4, sequence);
            view.Write(8, 1);
            view.Write(192, timestamp ?? Environment.TickCount64);
            view.Write(ApplicationModelImages.HeaderSize + 32, length);
            view.Write(ApplicationModelImages.HeaderSize + 36, 1u | (1u << 16));
            view.WriteArray(ApplicationModelImages.HeaderSize + 552, red.Bytes, 0, 4);
        }
        Check(ApplicationModelImages.Read(view).Length == 0, "Unpublished IPC does not expose images.");
        Publish();
        Check(Pixel(ApplicationModelImages.Read(view).Single().ToPng()!).ToArgb() == Color.Red.ToArgb(), "Native and managed record offsets agree on an actual BGRA frame.");
        Publish(sequence: 3); Check(ApplicationModelImages.Read(view).Length == 0, "An in-progress writer cannot expose partial image data.");
        Publish(timestamp: Environment.TickCount64 - 4000); Check(ApplicationModelImages.Read(view).Length == 0, "Stale model frames select fallback.");
        Publish(length: 65537); Check(ApplicationModelImages.Read(view).Length == 0, "Oversized IPC payload is rejected before allocation.");
        File.WriteAllLines(report, checks);
        return 0;
    }

    static int TestApplicationModelLive(string report)
    {
        using var scanner = new ScanSession();
        var first = scanner.Scan(forceFull: true);
        List<TrayEntry> entries = first;
        for (int i = 0; i < 20 && entries.All(entry => entry.LiveIconSnapshot == null); i++)
        {
            Thread.Sleep(300);
            entries = scanner.Scan(forceFull: false);
        }
        int live = entries.Count(entry => entry.LiveIconSnapshot is { Length: > 0 });
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            passed = live > 0, live, fallback = entries.Count - live,
            items = entries.Select(entry => new { entry.Name, entry.Key, entry.State, live = entry.LiveIconSnapshot != null }),
            isolation = "Real ScanSession integration; no visibility or preference changes."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return live > 0 ? 0 : 1;
    }

    static int ProbeApplicationModel(string report)
    {
        report = Path.GetFullPath(report);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        using var session = new SystemIconSession(applicationImages: true);
        var source = session.ApplicationImages!;
        for (int i = 0; i < 50 && session.Error == 0; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }
        var records = source.Read();
        var entries = Scanner.Scan().Where(entry => !Scanner.IsShellEntry(entry)).ToArray();
        int matched = 0;
        var items = entries.Select((entry, index) =>
        {
            var image = records.SingleOrDefault(record => record.Matches(entry));
            string? file = null;
            string error = "";
            if (image != null)
            {
                try
                {
                    var png = image.ToPng() ?? throw new IOException("Invalid image pixels.");
                    file = $"model-application-{index:D3}.png";
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report)!, file), png);
                    matched++;
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException)
                { error = ex.Message; }
            }
            return new { entry.Key, entry.Name, entry.State, image = file, error };
        }).ToArray();
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            sessionError = $"0x{session.Error:X8}", modelError = $"0x{source.Error:X8}", ticks = source.Ticks,
            viewSymbols = source.ViewSymbols.Select(symbol => $"0x{symbol:X}"),
            matched, items, records = records.Select(record => new { record.Window, record.Id, record.Guid,
                error = $"0x{record.Error:X8}", record.Model, record.ImageType, record.Width, record.Height, bytes = record.Bytes.Length }),
            isolation = "Read-only Explorer model resource access. No screen capture, control rendering or visibility/preference changes."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return matched > 0 && session.Error == 0 && source.Error == 0 ? 0 : 1;
    }
}
