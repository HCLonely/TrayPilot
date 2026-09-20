namespace TrayPilot;

// Expiration also refreshes metadata when an executable is replaced in place.
internal sealed class NameCache
{
    const int Capacity = 256;
    const long Lifetime = 5 * 60 * 1000;
    readonly Dictionary<string, (string Value, long Created, long Used)> values = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<string, string> read;
    readonly Func<long> now;
    long sequence;
    internal NameCache(Func<string, string>? read = null, Func<long>? now = null)
    { this.read = read ?? Scanner.ReadName; this.now = now ?? (() => Environment.TickCount64); }
    internal string Get(string path)
    {
        lock (values)
        {
            long time = now();
            if (values.TryGetValue(path, out var cached) && time - cached.Created < Lifetime)
            {
                values[path] = (cached.Value, cached.Created, ++sequence);
                return cached.Value;
            }
            string value = read(path);
            if (!values.ContainsKey(path) && values.Count >= Capacity)
                values.Remove(values.MinBy(x => x.Value.Used).Key);
            values[path] = (value, time, ++sequence);
            return value;
        }
    }
}
