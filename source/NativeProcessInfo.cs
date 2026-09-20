using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayPilot;

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString { public ushort Length, MaximumLength; public nint Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessIdInfo { public nint Pid; public UnicodeString ImageName; }
    // SYSTEM_PROCESS_INFORMATION prefix, Windows 11. Fail closed if the query/layout is unavailable.
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo
    {
        public uint Next, Threads;
        public long PrivateWorkingSet;
        public uint HardFaults, ThreadHighWatermark;
        public ulong Cycles;
        public long CreateTime, UserTime, KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public nint Pid;
    }
    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int kind, nint buffer, int length, out int required);
    [DllImport("ntdll.dll", EntryPoint = "NtQuerySystemInformation")]
    static extern int QueryProcessId(int kind, ref ProcessIdInfo info, int length, out int required);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint QueryDosDeviceW(string name, StringBuilder target, int length);

    internal static string SystemProcessPath(uint pid)
    {
        var memory = Marshal.AllocHGlobal(65534);
        try
        {
            var info = new ProcessIdInfo { Pid = (nint)pid, ImageName = new() { MaximumLength = 65534, Buffer = memory } };
            if (QueryProcessId(88, ref info, Marshal.SizeOf<ProcessIdInfo>(), out _) != 0 || info.ImageName.Length == 0) return "";
            var path = Marshal.PtrToStringUni(memory, info.ImageName.Length / 2) ?? "";
            if (path.StartsWith(@"\??\", StringComparison.Ordinal)) return path[4..];
            foreach (var drive in Environment.GetLogicalDrives())
            {
                var target = new StringBuilder(32768);
                if (QueryDosDeviceW(drive[..2], target, target.Capacity) == 0) continue;
                var prefix = target.ToString();
                if (path.StartsWith(prefix + @"\", StringComparison.OrdinalIgnoreCase)) return drive[..2] + path[prefix.Length..];
            }
            return "";
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return ""; }
        finally { Marshal.FreeHGlobal(memory); }
    }

    internal static long ProcessStarted(uint pid, Func<Dictionary<uint, long>>? fallback = null)
    {
        try { using var process = Process.GetProcessById(checked((int)pid)); return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
        return fallback != null ? fallback().GetValueOrDefault(pid) : SystemProcessStarted(pid);
    }
    internal static long SystemProcessStarted(uint pid)
        => SystemProcessStarts().GetValueOrDefault(pid);
    internal static Dictionary<uint, long> SystemProcessStarts()
    {
        var starts = new Dictionary<uint, long>();
        int length = 1024 * 1024;
        for (int attempt = 0; attempt < 5 && length <= 64 * 1024 * 1024; attempt++)
        {
            var memory = Marshal.AllocHGlobal(length);
            try
            {
                int status = NtQuerySystemInformation(5, memory, length, out int required);
                if (status == unchecked((int)0xC0000004)) { length = Math.Max(length * 2, required); continue; }
                if (status != 0) return starts;
                int size = Marshal.SizeOf<ProcessInfo>();
                for (int offset = 0; offset <= required - size;)
                {
                    var info = Marshal.PtrToStructure<ProcessInfo>(memory + offset);
                    if ((ulong)info.Pid <= uint.MaxValue && info.CreateTime > 0)
                        starts[(uint)info.Pid] = DateTime.FromFileTimeUtc(info.CreateTime).Ticks;
                    if (info.Next < size || info.Next > required - offset) break;
                    offset += (int)info.Next;
                }
                return starts;
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or ArgumentOutOfRangeException) { return starts; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        return starts;
    }
}
