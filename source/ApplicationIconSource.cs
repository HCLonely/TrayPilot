namespace TrayPilot;

// Optional image source. Symbol preparation and Explorer attachment must never
// delay the first list render; the existing icon path works while connecting.
internal sealed class ApplicationIconSource : IDisposable
{
    readonly object gate = new();
    Task<SystemIconSession?>? pending;
    SystemIconSession? session;
    long retryAt;
    bool disposed;

    internal List<TrayEntry> Apply(List<TrayEntry> entries)
    {
        lock (gate)
        {
            if (disposed) return entries;
            long now = Environment.TickCount64;
            if (session != null && (session.IsDisposed || session.Error != 0 || session.Pid != SystemIconSession.ExplorerPid()))
            {
                session.Dispose();
                session = null;
                retryAt = now + 60000;
            }
            if (pending is { IsCompleted: true })
            {
                session = pending.GetAwaiter().GetResult();
                pending = null;
                if (session == null) retryAt = now + 60000;
            }
            if (session == null && pending == null && now >= retryAt)
                pending = Task.Run(() =>
                {
                    try { return new SystemIconSession(applicationImages: true); }
                    catch (Exception ex)
                    {
                        // This backend is optional, including on future Windows builds.
                        System.Diagnostics.Debug.WriteLine("Application tray images: " + ex.Message);
                        return null;
                    }
                });

            ApplicationModelImage[] images;
            try { images = session?.ApplicationImages?.Read() ?? Array.Empty<ApplicationModelImage>(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or ArgumentException)
            { images = Array.Empty<ApplicationModelImage>(); }
            return entries.Select(entry =>
            {
                var matches = images.Where(image => image.Matches(entry)).Take(2).ToArray();
                // Never choose arbitrarily between conflicting model identities.
                byte[]? png = matches.Length == 1 ? matches[0].ToPng() : null;
                return entry with { LiveIconSnapshot = png };
            }).ToList();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            session?.Dispose();
            session = null;
            if (pending != null)
                _ = pending.ContinueWith(task => task.Result?.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            pending = null;
        }
    }
}
