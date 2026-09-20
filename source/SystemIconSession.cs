using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace TrayPilot;

internal sealed class SystemIconSession : IDisposable
{
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
    internal uint Pid { get; }
    internal bool Legacy { get; }
    internal int Found => view.ReadInt32(16);
    internal int Hidden => view.ReadInt32(20);
    internal int Ticks => view.ReadInt32(24);
    internal int Error => view.ReadInt32(28);
    internal int Acknowledged => view.ReadInt32(44);
    internal int Requested => view.ReadInt32(8);
    internal void SetAppearanceCapture(bool enabled) => view.Write(2016, enabled ? 1 : 0);
    internal bool SharedMicrophoneLocation => view.ReadInt32(56) != 0;
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
    internal SystemIconSession(bool legacy = false, int initialMask = 0)
    {
        if ((initialMask & ~SystemIconCatalog.All) != 0) throw new ArgumentOutOfRangeException(nameof(initialMask));
        Legacy = legacy;
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(L.T("systemNativeArchitecture"));
        Pid = ExplorerPid();
        if (Pid == 0) throw new IOException(L.T("systemNativeUnavailable"));
        string? symbols = legacy ? null : TaskbarSymbols.PrepareAsync().GetAwaiter().GetResult();
        string dll = NativeHelper.Extract();
        string name = @"Local\TrayPilot.SystemIcons." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        mapping = MemoryMappedFile.CreateNew(name, 4096);
        view = mapping.CreateViewAccessor();
        view.Write(0, 1); view.Write(4, Environment.ProcessId);
        view.Write(8, initialMask);
        SetAppearanceCapture(true);
        try
        {
            library = NativeLibrary.Load(dll);
            int result = legacy
                ? Marshal.GetDelegateForFunctionPointer<AttachDelegate>(NativeLibrary.GetExport(library, "Attach"))(Pid, name)
                : Marshal.GetDelegateForFunctionPointer<AttachWithSymbolsDelegate>(NativeLibrary.GetExport(library, "AttachWithSymbols"))(Pid, name, symbols!);
            Marshal.ThrowExceptionForHR(result);
        }
        catch { Dispose(); throw; }
    }
    internal int Set(int mask)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((mask & ~SystemIconCatalog.All) != 0) throw new ArgumentOutOfRangeException(nameof(mask));
        view.Write(8, mask);
        view.Write(40, ++request);
        return request;
    }
    internal string DebugInfo()
    {
        return $"Backend={(Legacy ? "LegacySymbolServer" : "DirectSymbolCache")} OS={Environment.OSVersion.Version}\nTaskbar timestamp={view.ReadUInt32(48):X8} ImageSize={view.ReadUInt32(52)}";
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
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
    }
}
