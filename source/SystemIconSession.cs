using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace TrayPilot;

internal sealed class SystemIconSession : IDisposable
{
    internal const int LanguageTraceOffset = ExplorerIconImage.MappingSize, LanguageTraceChars = 16384;
    internal const int MappingSize = LanguageTraceOffset + 8 + LanguageTraceChars * 2 + 120;
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int AttachWithSourceDelegate(uint pid, [MarshalAs(UnmanagedType.LPWStr)] string mapping,
        [MarshalAs(UnmanagedType.LPWStr)] string? symbolDirectory, [MarshalAs(UnmanagedType.LPWStr)] string? sourceDirectory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int AttachDelegate(uint pid, [MarshalAs(UnmanagedType.LPWStr)] string mapping);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate int AttachWithSymbolsDelegate(uint pid, [MarshalAs(UnmanagedType.LPWStr)] string mapping,
        [MarshalAs(UnmanagedType.LPWStr)] string symbolDirectory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int IdentifyGlyphDelegate(ushort glyph);
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    nint library;
    int request;
    bool disposed;
    internal bool IsDisposed => disposed;
    internal ApplicationModelImages? ApplicationImages { get; }
    internal uint Pid { get; }
    internal bool Legacy { get; }
    internal int Found => view.ReadInt32(16);
    internal int Hidden => view.ReadInt32(20);
    internal int Ticks => view.ReadInt32(24);
    internal int Error => view.ReadInt32(28);
    internal int Acknowledged => view.ReadInt32(44);
    internal int Requested => view.ReadInt32(8) & SystemIconCatalog.All;
    internal int ForcedShown => (view.ReadInt32(8) >> 16) & SystemIconCatalog.Language;
    internal int SourceStatus => view.ReadInt32(625024);
    internal void SetAppearanceCapture(bool enabled) => view.Write(2016, enabled ? 1 : 0);
    internal bool SharedMicrophoneLocation => view.ReadInt32(56) != 0;
    internal ExplorerIconImage?[] ReadImages()
    {
        var images = new ExplorerIconImage?[ExplorerIconImage.Count];
        long now = Environment.TickCount64;
        int visible = Found & ~Hidden;
        for (int i = 0; i < images.Length; i++)
            if ((visible & (1 << i)) != 0) images[i] = ExplorerIconImage.Read(view, i, now);
        return images;
    }
    internal (string Text, string Font)[] ReadAppearances()
    {
        var result = new (string Text, string Font)[12];
        var bytes = new byte[12 * 160];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int before = view.ReadInt32(60);
            if ((before & 1) != 0) continue;
            view.ReadArray(96, bytes, 0, bytes.Length);
            Thread.MemoryBarrier();
            if (before != view.ReadInt32(60)) continue;
            for (int i = 0; i < result.Length; i++)
                result[i] = (System.Text.Encoding.Unicode.GetString(bytes, i * 160, 32).Split('\0')[0],
                    System.Text.Encoding.Unicode.GetString(bytes, i * 160 + 32, 128).Split('\0')[0]);
            return result;
        }
        return result;
    }
    internal static uint ExplorerPid()
    {
        var window = Native.FindWindowExW(0, 0, "Shell_TrayWnd", null);
        Native.GetWindowThreadProcessId(window, out var pid);
        return pid;
    }
    internal SystemIconSession(bool legacy = false, int initialMask = 0, bool applicationImages = false, int initialForcedShown = 0)
    {
        SystemIconCatalog.ValidateVisibility(initialMask, initialForcedShown);
        Legacy = legacy;
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(L.T("systemNativeArchitecture"));
        Pid = ExplorerPid();
        if (Pid == 0) throw new IOException(L.T("systemNativeUnavailable"));
        string? symbols = legacy ? null : TaskbarSymbols.PrepareAsync().GetAwaiter().GetResult();
        string? sourceSymbols = null;
        if (!applicationImages)
        {
            try
            {
                string image = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps",
                    "MicrosoftWindows.Client.Core_cw5n1h2txyewy", "SystemTray.dll");
                sourceSymbols = TaskbarSymbols.PrepareAsync(image).GetAwaiter().GetResult();
            }
            catch when (initialForcedShown == 0) { /* Hiding remains available without the optional source symbols. */ }
        }
        string dll = NativeHelper.Extract();
        string name = @"Local\TrayPilot.SystemIcons." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        mapping = MemoryMappedFile.CreateNew(name, MappingSize);
        view = mapping.CreateViewAccessor();
        view.Write(0, 4); view.Write(4, Environment.ProcessId);
        view.Write(8, initialMask | (initialForcedShown << 16));
        SetAppearanceCapture(true);
        try
        {
            library = NativeLibrary.Load(dll);
            if (applicationImages) ApplicationImages = new(name, symbols ?? throw new NotSupportedException("Application images require verified symbols."), library);
            int result = !applicationImages
                ? Marshal.GetDelegateForFunctionPointer<AttachWithSourceDelegate>(NativeLibrary.GetExport(library, "AttachWithSourceSymbols"))(Pid, name, symbols, sourceSymbols)
                : legacy
                ? Marshal.GetDelegateForFunctionPointer<AttachDelegate>(NativeLibrary.GetExport(library, "Attach"))(Pid, name)
                : Marshal.GetDelegateForFunctionPointer<AttachWithSymbolsDelegate>(NativeLibrary.GetExport(library, "AttachWithSymbols"))(Pid, name, symbols!);
            Marshal.ThrowExceptionForHR(result);
        }
        catch { Dispose(); throw; }
    }
    internal int Set(int mask, int restoreMask = 0, int forcedShown = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SystemIconCatalog.ValidateVisibility(mask, forcedShown);
        if ((restoreMask & ~SystemIconCatalog.All) != 0 || (restoreMask & mask) != 0)
            throw new ArgumentOutOfRangeException(nameof(restoreMask));
        // Both mutually exclusive preferences are published in one atomic LONG.
        view.Write(8, mask | (forcedShown << 16));
        view.Write(2020, restoreMask);
        view.Write(40, ++request);
        return request;
    }
    internal string DebugInfo()
    {
        string images = string.Join(" ", Enumerable.Range(0, 12).Select(i =>
        {
            long offset = ExplorerIconImage.Offset + i * ExplorerIconImage.SlotSize;
            return $"{1 << i}:{view.ReadInt32(offset + 4)}x{view.ReadInt32(offset + 8)}/0x{view.ReadInt32(offset + 12):X8}";
        }));
        return $"Backend={(Legacy ? "LegacySymbolServer" : "DirectSymbolCache")} OS={Environment.OSVersion.Version}\nTaskbar timestamp={view.ReadUInt32(48):X8} ImageSize={view.ReadUInt32(52)}\nLanguage source override=0x{SourceStatus:X8}\nExplorer images (bit:widthxheight/HRESULT)={images}\nLanguage structural transitions (no input text):\n{ReadLanguageTrace(view)}";
    }
    internal static string ReadLanguageTrace(MemoryMappedViewAccessor source)
    {
        var buffer = new byte[LanguageTraceChars * 2];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int sequence = source.ReadInt32(LanguageTraceOffset);
            if (sequence == 0 || (sequence & 1) != 0) continue;
            source.ReadArray(LanguageTraceOffset + 4, buffer, 0, buffer.Length);
            Thread.MemoryBarrier();
            if (sequence == source.ReadInt32(LanguageTraceOffset))
                return System.Text.Encoding.Unicode.GetString(buffer).Split('\0')[0];
        }
        return "";
    }
    internal int IdentifyGlyph(char glyph) => Marshal.GetDelegateForFunctionPointer<IdentifyGlyphDelegate>(
        NativeLibrary.GetExport(library, "IdentifyGlyph"))(glyph);
    public void Dispose() => Stop(restore: true);
    internal void Stop(bool restore)
    {
        if (disposed) return;
        disposed = true;
        if (restore) view.Write(8, 0);
        view.Write(12, restore ? 1 : 2);
        // The helper also watches our process handle and restores after a crash.
        view.Dispose(); mapping.Dispose();
        ApplicationImages?.Dispose();
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
    }
}
