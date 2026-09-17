using System.Runtime.InteropServices;
using System.Text;

namespace TrayPilot;
internal static partial class Native
{
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? Title;
        public uint InfoFlags; public Guid Guid; public nint Balloon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Identifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumProc(nint window, nint param);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIconW(uint message, ref IconData data);
    [DllImport("shell32.dll")] internal static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowExW(nint parent, nint after, string? cls, string? name);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassNameW(nint window, StringBuilder name, int size);
    internal static string WindowClass(nint window) { var name = new StringBuilder(256); GetClassNameW(window, name, name.Capacity); return name.ToString(); }
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] internal static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("shell32.dll")] internal static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);

    internal static int State(TrayEntry entry)
    {
        var id = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = (nint)entry.Window, Id = entry.Id, Guid = entry.Guid };
        return Shell_NotifyIconGetRect(ref id, out _);
    }
    internal static bool SetHidden(TrayEntry entry, bool hidden)
    {
        var data = new IconData { Size = (uint)Marshal.SizeOf<IconData>(), Window = (nint)entry.Window,
            Id = entry.Id, Guid = entry.Guid, Flags = 8u | (entry.Guid == Guid.Empty ? 0u : 32u), StateMask = 1, State = hidden ? 1u : 0u };
        return Shell_NotifyIconW(1, ref data);
    }
    internal static string ProcessPath(uint pid)
    {
        var p = OpenProcess(0x1000, false, pid);
        if (p == 0) return SystemProcessPath(pid);
        try { var text = new StringBuilder(32768); uint length = 32768; return QueryFullProcessImageNameW(p, 0, text, ref length) ? text.ToString() : SystemProcessPath(pid); }
        finally { CloseHandle(p); }
    }
}
