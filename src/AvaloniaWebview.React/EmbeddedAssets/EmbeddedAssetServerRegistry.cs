using AvaloniaWebview.React;

namespace AvaloniaWebview.React.EmbeddedAssets;

/// <summary>
/// Shares loopback asset servers between WebViews that use identical embedded-asset options.
/// A shared server keeps the page origin stable across windows, so cookies, localStorage and
/// IndexedDB use the same origin.
/// </summary>
internal static class EmbeddedAssetServerRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<OptionsKey, Entry> Servers = [];

    public static IAssetTransport Acquire(EmbeddedAssetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var key = OptionsKey.Create(options);
        lock (Sync)
        {
            if (Servers.TryGetValue(key, out var entry))
            {
                entry.References++;
                return new SharedTransport(key, entry.Server.BaseUri);
            }

            var server = new EmbeddedAssetServer(options);
            Servers.Add(key, new Entry(server));
            return new SharedTransport(key, server.BaseUri);
        }
    }

    private static async Task ReleaseAsync(OptionsKey key)
    {
        Entry? entry = null;
        lock (Sync)
        {
            if (!Servers.TryGetValue(key, out var current))
            {
                return;
            }

            if (--current.References <= 0)
            {
                Servers.Remove(key);
                entry = current;
            }
        }

        if (entry is not null)
        {
            try
            {
                await entry.Server.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Shutdown is best-effort; the process may already be exiting.
            }
        }
    }

    private sealed class Entry(EmbeddedAssetServer server)
    {
        public EmbeddedAssetServer Server { get; } = server;

        public int References { get; set; } = 1;
    }

    private sealed class SharedTransport(OptionsKey key, Uri baseUri) : IAssetTransport
    {
        private int _disposed;

        public Uri BaseUri { get; } = baseUri;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            return new ValueTask(ReleaseAsync(key));
        }
    }

    private readonly record struct OptionsKey(
        string RootPath,
        string SpaFallback,
        bool EnableCaching,
        bool EnableKeepAlive,
        long IdleConnectionTimeoutTicks,
        int MaxRequestsPerConnection,
        int MaxConnections)
    {
        public static OptionsKey Create(EmbeddedAssetOptions options)
        {
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
            var spaFallback = options.SpaFallback;
            if (OperatingSystem.IsWindows())
            {
                rootPath = rootPath.ToUpperInvariant();
                spaFallback = spaFallback.ToUpperInvariant();
            }

            return new OptionsKey(
                rootPath,
                spaFallback,
                options.EnableCaching,
                options.EnableKeepAlive,
                options.IdleConnectionTimeout.Ticks,
                options.MaxRequestsPerConnection,
                options.MaxConnections);
        }
    }
}
