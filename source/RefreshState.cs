namespace TrayPilot;

internal sealed class ScanSession
{
    List<TrayEntry> previous = new();
    long lastFull;
    internal List<TrayEntry> Scan(bool forceFull)
    {
        long now = Environment.TickCount64;
        if (forceFull || lastFull == 0 || now - lastFull >= 10000)
        {
            previous = Scanner.Scan(); lastFull = now;
        }
        else
        {
            var current = new List<TrayEntry>(previous.Count);
            var processes = new Dictionary<uint, (string Path, long Start)>();
            foreach (var entry in previous)
            {
                if (!Scanner.SameOwner(entry, processes)) continue;
                int state = Native.State(entry);
                if (state is 0 or 1) current.Add(entry with { State = state });
            }
            if (current.Count != previous.Count) { previous = Scanner.Scan(); lastFull = now; }
            else previous = current;
        }
        return previous;
    }
}
