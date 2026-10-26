using System.Text.Json;
using System.Runtime.InteropServices;

namespace TrayPilot;

internal static partial class Diagnostics
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ProbeApplicationNotifyDelegate(uint duration, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 5)] int[] result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int ProbeApplicationSymbolsDelegate([MarshalAs(UnmanagedType.LPWStr)] string directory,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 8)] ulong[] result);

    static int TestApplicationTrayImages(string report)
    {
        var checks = new List<string>();
        void Check(bool passed, string label)
        {
            if (!passed) throw new IOException(label);
            checks.Add("PASS " + label);
        }
        var owner = new NativeWindow();
        owner.CreateHandle(new CreateParams { Caption = "TrayPilot image decoder fixture", Parent = -3 });
        using var reader = new TrayNotifyReader();
        using var red = new Bitmap(16, 16);
        using var green = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(red)) graphics.Clear(Color.Red);
        using (var graphics = Graphics.FromImage(green)) graphics.Clear(Color.Lime);
        nint redIcon = red.GetHicon(), greenIcon = green.GetHicon();
        try
        {
            var callback = (ITrayImageCallback)reader;
            var item = new TrayNotifyItem { Executable = Environment.ProcessPath, Window = owner.Handle,
                Id = 9182, Icon = redIcon, Tooltip = "Synthetic UID fixture" };
            callback.Notify(0, ref item);
            var first = reader.Snapshot().Single();
            using (var stream = new MemoryStream(first.Png))
            using (var bitmap = new Bitmap(stream))
                Check(bitmap.GetPixel(8, 8).ToArgb() == Color.Red.ToArgb(), "Copies the submitted HICON's actual red pixels.");
            item.Id++;
            item.Guid = Guid.NewGuid();
            item.Icon = greenIcon;
            callback.Notify(0, ref item);
            Check(reader.Snapshot().Length == 2, "UID and GUID icons belonging to one executable stay separate.");
            item.Id--;
            item.Guid = Guid.Empty;
            callback.Notify(1, ref item);
            var updated = reader.Snapshot().Single(image => image.Owner.Guid == Guid.Empty);
            Check(!updated.Png.SequenceEqual(first.Png), "An image update replaces only the matching icon's pixels.");
            using (var stream = new MemoryStream(updated.Png))
            using (var bitmap = new Bitmap(stream))
                Check(bitmap.GetPixel(8, 8).ToArgb() == Color.Lime.ToArgb(), "The updated icon decodes to green.");
            Check(updated.Matches(updated.Owner with { State = 1 }), "Image matching does not depend on visibility.");
            Check(!updated.Matches(updated.Owner with { Started = updated.Owner.Started + 1 }), "Process restarts cannot reuse an earlier image.");
            Check(!updated.Matches(updated.Owner with { Pid = updated.Owner.Pid + 1 }), "PID mismatch rejects an image.");
            Check(!updated.Matches(updated.Owner with { Path = @"C:\wrong-owner.exe" }), "Executable mismatch rejects an image.");
            item.Icon = 0;
            callback.Notify(1, ref item);
            Check(reader.Snapshot().Length == 1 && reader.ImageFailures == 1, "An invalid replacement drops the stale image so the caller can fall back.");
            item.Icon = redIcon;
            callback.Notify(1, ref item);
            Check(reader.Snapshot().Length == 2, "A later successful notification recovers the image.");
            callback.Notify(2, ref item);
            Check(reader.Snapshot().Length == 1, "Deleting one icon preserves its sibling.");
            Check(reader.GetImage(reader.Snapshot()[0].Owner) == null, "An unconnected reader returns no live image and preserves the original fallback.");
            reader.Dispose();
            callback.Notify(0, ref item);
            Check(reader.Snapshot().Length == 0, "Disposed subscriptions ignore late callbacks.");
            using var original = Icon.FromHandle(redIcon).ToBitmap();
            Check(original.GetPixel(8, 8).ToArgb() == Color.Red.ToArgb(), "The reader never destroys the provider's original HICON.");
        }
        finally
        {
            Native.DestroyIcon(redIcon);
            Native.DestroyIcon(greenIcon);
            owner.DestroyHandle();
        }
        File.WriteAllLines(report, checks);
        return 0;
    }

    static int ProbeApplicationTrayImages(string report)
    {
        report = Path.GetFullPath(report);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        using var reader = new TrayNotifyReader();
        bool connected = reader.Connect();
        // Only these test-owned icons are created, updated or hidden. Existing
        // user icons are observed without changing their visibility/preferences.
        var owner = new NativeWindow();
        owner.CreateHandle(new CreateParams { Caption = "TrayPilot image probe", Parent = -3 });
        var fixture = new Native.IconData { Size = (uint)Marshal.SizeOf<Native.IconData>(), Window = owner.Handle,
            Id = 9182, Flags = 7, Message = 0x8001, Icon = SystemIcons.Information.Handle, Tip = "TrayPilot image probe UID" };
        bool added = Native.Shell_NotifyIconW(0, ref fixture);
        var nativeResult = new int[5];
        int nativeStatus;
        var symbols = new ulong[8];
        int symbolStatus = unchecked((int)0x80004005);
        string symbolError = "";
        int updates = 0;
        bool hidden = false;
        using var update = new System.Windows.Forms.Timer { Interval = 300 };
        update.Tick += (_, _) =>
        {
            if (!added) return;
            updates++;
            fixture.Flags = 2;
            fixture.Icon = updates % 2 == 0 ? SystemIcons.Information.Handle : SystemIcons.Warning.Handle;
            Native.Shell_NotifyIconW(1, ref fixture);
        };
        update.Start();
        nint library = 0;
        try
        {
            library = NativeLibrary.Load(NativeHelper.Extract());
            var probe = Marshal.GetDelegateForFunctionPointer<ProbeApplicationNotifyDelegate>(
                NativeLibrary.GetExport(library, "ProbeApplicationNotify"));
            nativeStatus = probe(3000, nativeResult);
            fixture.Flags = 8; fixture.StateMask = 1; fixture.State = 1;
            hidden = added && Native.Shell_NotifyIconW(1, ref fixture);
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }
            hidden = hidden && Native.State(new TrayEntry { Window = owner.Handle, Id = fixture.Id }) == 1;
            try
            {
                string directory = TaskbarSymbols.PrepareAsync().GetAwaiter().GetResult();
                var resolve = Marshal.GetDelegateForFunctionPointer<ProbeApplicationSymbolsDelegate>(
                    NativeLibrary.GetExport(library, "ProbeApplicationSymbols"));
                symbolStatus = resolve(directory, symbols);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException)
            {
                symbolError = ex.Message;
            }
        }
        finally
        {
            update.Stop();
            if (added) Native.Shell_NotifyIconW(2, ref fixture);
            owner.DestroyHandle();
            if (library != 0) NativeLibrary.Free(library);
        }
        var entries = Scanner.Scan().Where(entry => !Scanner.IsShellEntry(entry)).ToList();
        var images = reader.Snapshot();
        var results = entries.Select(entry =>
        {
            byte[]? png = reader.GetImage(entry);
            string? file = null;
            if (png != null)
            {
                file = $"application-{entries.IndexOf(entry):D3}.png";
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report)!, file), png);
            }
            return new { entry.Key, entry.Name, entry.Path, entry.State, native = png != null, image = file };
        }).ToArray();
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            connected, error = $"0x{reader.Error:X8}", callbacks = reader.Callbacks,
            available = results.Any(item => item.native),
            validImages = images.Length, imageFailures = reader.ImageFailures, items = results,
            nativeControl = new { status = $"0x{nativeStatus:X8}", callbacks = nativeResult[0], images = nativeResult[1],
                registerStatus = $"0x{nativeResult[2]:X8}", unregisterStatus = $"0x{nativeResult[3]:X8}", cookie = nativeResult[4] },
            fixture = new { added, updates, hiddenBeforeRemoval = hidden },
            internalModelSymbols = new { status = $"0x{symbolStatus:X8}", error = symbolError,
                taskbarTimestamp = $"0x{symbols[0]:X8}", imageSize = symbols[1],
                shellNotifyIconRva = $"0x{symbols[2]:X}", promotedCollectionRva = $"0x{symbols[3]:X}",
                overflowCollectionRva = $"0x{symbols[4]:X}", identityRva = $"0x{symbols[5]:X}",
                iconGetterRva = $"0x{symbols[6]:X}", iconInterfaceVtableRva = $"0x{symbols[7]:X}",
                note = "Symbol resolution only; private getters were not invoked and no object offsets were guessed." },
            isolation = "Read-only for user icons. Only the probe's own icon is created, updated, hidden and removed. No screen capture or preference writes."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return connected && results.Any(item => item.native) ? 0 : 1;
    }
}
