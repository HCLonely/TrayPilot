namespace TrayPilot;

internal sealed class GlobalHotkey(nint window) : IDisposable
{
    int registeredId;
    Keys registeredKeys;
    internal bool Matches(nint id) => registeredId != 0 && id == registeredId;
    internal static bool Valid(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        return (keys & (Keys.Control | Keys.Alt)) != 0
            && key is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.F12);
    }
    internal bool TrySet(bool enabled, Keys keys)
    {
        if (!enabled) { Dispose(); return true; }
        if (!Valid(keys)) return false;
        if (registeredId != 0 && registeredKeys == keys) return true;
        uint modifiers = 0x4000u | ((keys & Keys.Alt) != 0 ? 1u : 0u) | ((keys & Keys.Control) != 0 ? 2u : 0u) | ((keys & Keys.Shift) != 0 ? 4u : 0u);
        int nextId = registeredId == 0x4A01 ? 0x4A02 : 0x4A01;
        if (!Native.RegisterHotKey(window, nextId, modifiers, (uint)(keys & Keys.KeyCode))) return false;
        if (registeredId != 0) Native.UnregisterHotKey(window, registeredId);
        registeredId = nextId; registeredKeys = keys;
        return true;
    }
    public void Dispose()
    {
        if (registeredId != 0) Native.UnregisterHotKey(window, registeredId);
        registeredId = 0; registeredKeys = Keys.None;
    }
}
