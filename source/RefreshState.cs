namespace TrayPilot;

internal sealed record RenderedRow(TrayEntry Entry, bool Rule, bool Manual)
{
    internal bool SameContent(RenderedRow other) => Rule == other.Rule && Manual == other.Manual &&
        Entry.Key == other.Entry.Key && Entry.Name == other.Entry.Name && Entry.Path == other.Entry.Path &&
        Entry.Pid == other.Entry.Pid && Entry.Started == other.Entry.Started && Entry.Window == other.Entry.Window && Entry.Id == other.Entry.Id &&
        Entry.Tooltip == other.Entry.Tooltip && Entry.State == other.Entry.State &&
        Entry.IconSnapshot.AsSpan().SequenceEqual(other.Entry.IconSnapshot);
}

// A single refresh operation owns this session. Full discovery remains the fallback for
// new icons, including applications which reuse an existing NotifyIconSettings record.
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

// Bounded, form-owned cache. Callers receive a clone and retain their existing ownership
// contract; eviction can never dispose an image still used by a menu or ImageList.
internal sealed class TrayImageCache : IDisposable
{
    const int Capacity = 256;
    readonly Dictionary<(string Key, string Path, long Started, int Size, bool Hidden, bool Files), Cached> images = new();
    long clock;
    sealed record Cached(byte[]? Snapshot, Bitmap Image) { internal long Used; }
    internal Bitmap Create(TrayEntry entry, int size, bool allowFileAccess = true)
    {
        var key = (entry.Key, entry.Path, entry.Started, size, entry.State == 1, allowFileAccess);
        if (images.TryGetValue(key, out var cached) && !entry.IconSnapshot.AsSpan().SequenceEqual(cached.Snapshot))
        { images.Remove(key); cached.Image.Dispose(); cached = null; }
        if (cached == null)
        {
            if (images.Count >= Capacity)
            {
                var oldest = images.MinBy(x => x.Value.Used);
                images.Remove(oldest.Key); oldest.Value.Image.Dispose();
            }
            cached = new(entry.IconSnapshot, TrayImages.Create(entry, size, allowFileAccess));
            images.Add(key, cached);
        }
        cached.Used = ++clock;
        return (Bitmap)cached.Image.Clone();
    }
    public void Dispose() { foreach (var cached in images.Values) cached.Image.Dispose(); images.Clear(); }
}
