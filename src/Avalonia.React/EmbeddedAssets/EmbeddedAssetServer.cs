using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Avalonia.React;

/// <summary>
/// Selects how published frontend files are served.
/// </summary>
public enum EmbeddedAssetTransport
{
    /// <summary>Serves files from a loopback HTTP server. Works on all platforms and supports SPA fallback.</summary>
    Loopback,

    /// <summary>Maps the asset folder to a WebView2 virtual host. Windows only, no loopback port, no SPA fallback.</summary>
    NativeHost,

    /// <summary>Uses the native virtual host on Windows and the loopback server on other platforms.</summary>
    Auto,
}

/// <summary>
/// Configures the built-in server used to serve a published frontend from disk.
/// </summary>
public sealed class EmbeddedAssetOptions
{
    /// <summary>Gets or sets the directory that contains the published frontend.</summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot");

    /// <summary>Gets or sets the file served for routes that do not map to an asset. Defaults to <c>index.html</c>.</summary>
    public string SpaFallback { get; set; } = "index.html";

    /// <summary>Gets or sets whether immutable cache headers are sent for files under <c>assets/</c>.</summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>Gets or sets the asset transport. Defaults to <see cref="EmbeddedAssetTransport.Loopback"/>.</summary>
    public EmbeddedAssetTransport Transport { get; set; } = EmbeddedAssetTransport.Loopback;

    /// <summary>Gets or sets the virtual host name used by <see cref="EmbeddedAssetTransport.NativeHost"/>.</summary>
    public string NativeHostName { get; set; } = "avalonia-react.local";
}

/// <summary>
/// Serves a published frontend from a local directory over loopback HTTP.
/// </summary>
public sealed class EmbeddedAssetServer : IAssetTransport
{
    private const int MaxRequestLineLength = 8 * 1024;
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxHeaderCount = 100;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".xml"] = "application/xml; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
    };

    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly string _spaFallback;
    private readonly bool _enableCaching;
    private readonly HashSet<string> _allowedHosts;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _acceptLoop;

    public EmbeddedAssetServer(EmbeddedAssetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Embedded asset root '{_rootPath}' does not exist.");
        }

        _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
        _spaFallback = options.SpaFallback;
        if (string.IsNullOrWhiteSpace(_spaFallback) ||
            Path.IsPathRooted(_spaFallback) ||
            _spaFallback.Split('/', '\\').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("SpaFallback must be a relative file name inside the asset root.", nameof(options));
        }

        if (!File.Exists(Path.Combine(_rootPath, _spaFallback)))
        {
            throw new FileNotFoundException($"Embedded asset root '{_rootPath}' does not contain '{_spaFallback}'.", options.SpaFallback);
        }

        _enableCaching = options.EnableCaching;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"127.0.0.1:{port}",
            $"localhost:{port}",
        };
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri BaseUri { get; }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _cancellation.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine))
                {
                    return;
                }

                if (requestLine.Length > MaxRequestLineLength)
                {
                    await WriteErrorAsync(stream, 414, "URI Too Long").ConfigureAwait(false);
                    return;
                }

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3)
                {
                    await WriteErrorAsync(stream, 400, "Bad Request").ConfigureAwait(false);
                    return;
                }

                var method = parts[0].ToUpperInvariant();
                var target = parts[1];
                var version = parts[2];
                if (version is not "HTTP/1.1" and not "HTTP/1.0")
                {
                    await WriteErrorAsync(stream, 505, "HTTP Version Not Supported").ConfigureAwait(false);
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var headerBytes = 0;
                var headerCount = 0;
                while (true)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    if (line.Length == 0)
                    {
                        break;
                    }

                    headerBytes += line.Length + 2;
                    if (++headerCount > MaxHeaderCount || headerBytes > MaxHeaderBytes)
                    {
                        await WriteErrorAsync(stream, 431, "Request Header Fields Too Large").ConfigureAwait(false);
                        return;
                    }

                    var separator = line.IndexOf(':');
                    if (separator <= 0)
                    {
                        await WriteErrorAsync(stream, 400, "Bad Request").ConfigureAwait(false);
                        return;
                    }

                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }

                if (!headers.TryGetValue("Host", out var host) || !_allowedHosts.Contains(host))
                {
                    await WriteErrorAsync(stream, 400, "Bad Request").ConfigureAwait(false);
                    return;
                }

                if (method is not "GET" and not "HEAD")
                {
                    await WriteErrorAsync(stream, 405, "Method Not Allowed", "GET, HEAD").ConfigureAwait(false);
                    return;
                }

                if (!TryResolvePath(target, out var fullPath, out var relativePath))
                {
                    await WriteErrorAsync(stream, 404, "Not Found").ConfigureAwait(false);
                    return;
                }

                await ServeFileAsync(stream, fullPath, relativePath, method, headers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private bool TryResolvePath(string target, out string fullPath, out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = string.Empty;

        var path = target;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        if (!path.StartsWith('/') || path.Contains('\\') || path.Contains('\0'))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.Contains('\\') || decoded.Contains('\0'))
        {
            return false;
        }

        var relative = decoded.TrimStart('/');
        if (relative.Length == 0)
        {
            relative = _spaFallback;
        }

        foreach (var segment in relative.Split('/'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(_rootPath, relative));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!candidate.StartsWith(_rootPrefix, PathComparison))
        {
            return false;
        }

        if (Directory.Exists(candidate))
        {
            candidate = Path.Combine(candidate, _spaFallback);
        }

        relative = Path.GetRelativePath(_rootPath, candidate).Replace('\\', '/');

        if (File.Exists(candidate))
        {
            fullPath = candidate;
            relativePath = relative;
            return true;
        }

        if (Path.GetExtension(relative).Length == 0 &&
            !relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = Path.Combine(_rootPath, _spaFallback);
            if (File.Exists(fallback))
            {
                fullPath = fallback;
                relativePath = _spaFallback;
                return true;
            }
        }

        return false;
    }

    private async Task ServeFileAsync(Stream stream, string fullPath, string relativePath, string method, Dictionary<string, string> headers)
    {
        var info = new FileInfo(fullPath);
        var length = info.Length;
        var lastModified = info.LastWriteTimeUtc;
        var etag = string.Create(CultureInfo.InvariantCulture, $"\"{length:x}-{lastModified.Ticks:x}\"");
        var cacheControl = GetCacheControl(relativePath);

        if (headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
            string.Equals(ifNoneMatch.Trim(), etag, StringComparison.Ordinal))
        {
            await WriteHeadersAsync(
                stream,
                304,
                "Not Modified",
                [("Cache-Control", cacheControl), ("ETag", etag), ("Connection", "close")]).ConfigureAwait(false);
            return;
        }

        long start = 0;
        long end = length - 1;
        var hasRange = false;
        if (length > 0 && headers.TryGetValue("Range", out var range))
        {
            if (!TryParseRange(range, length, out start, out end, out var invalid))
            {
                if (invalid)
                {
                    await WriteHeadersAsync(
                        stream,
                        416,
                        "Range Not Satisfiable",
                        [("Content-Range", $"bytes */{length}"), ("Content-Length", "0"), ("Connection", "close")]).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                hasRange = true;
            }
        }

        var contentLength = length == 0 ? 0 : end - start + 1;
        var responseHeaders = new List<(string, string)>
        {
            ("Content-Type", GetContentType(fullPath)),
            ("Content-Length", contentLength.ToString(CultureInfo.InvariantCulture)),
            ("Cache-Control", cacheControl),
            ("ETag", etag),
            ("Accept-Ranges", "bytes"),
            ("Connection", "close"),
        };

        if (hasRange)
        {
            responseHeaders.Add(("Content-Range", $"bytes {start}-{end}/{length}"));
        }

        await WriteHeadersAsync(stream, hasRange ? 206 : 200, hasRange ? "Partial Content" : "OK", responseHeaders).ConfigureAwait(false);

        if (method == "HEAD" || contentLength == 0)
        {
            return;
        }

        await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        file.Seek(start, SeekOrigin.Begin);
        await CopyRangeAsync(file, stream, contentLength).ConfigureAwait(false);
    }

    private string GetCacheControl(string relativePath) =>
        !_enableCaching
            ? "no-store"
            : relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=31536000, immutable"
                : "no-cache";

    private static string GetContentType(string fullPath) =>
        MimeTypes.GetValueOrDefault(Path.GetExtension(fullPath), "application/octet-stream");

    private static bool TryParseRange(string value, long length, out long start, out long end, out bool invalid)
    {
        start = 0;
        end = 0;
        invalid = false;

        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            invalid = true;
            return false;
        }

        var specification = value[6..].Trim();
        if (specification.Contains(','))
        {
            return false;
        }

        var separator = specification.IndexOf('-');
        if (separator < 0)
        {
            invalid = true;
            return false;
        }

        var startText = specification[..separator].Trim();
        var endText = specification[(separator + 1)..].Trim();

        if (startText.Length == 0)
        {
            if (!long.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out var suffixLength) || suffixLength <= 0)
            {
                invalid = true;
                return false;
            }

            start = Math.Max(0, length - suffixLength);
            end = length - 1;
            return true;
        }

        if (!long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out start) ||
            start < 0 ||
            start >= length)
        {
            invalid = true;
            return false;
        }

        if (endText.Length == 0)
        {
            end = length - 1;
            return true;
        }

        if (!long.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
        {
            invalid = true;
            return false;
        }

        end = Math.Min(end, length - 1);
        return true;
    }

    private static async Task CopyRangeAsync(Stream source, Stream destination, long length)
    {
        var buffer = new byte[64 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining))).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static Task WriteErrorAsync(Stream stream, int status, string reason, string? allow = null)
    {
        var headers = new List<(string, string)>
        {
            ("Content-Length", "0"),
            ("Connection", "close"),
        };
        if (allow is not null)
        {
            headers.Add(("Allow", allow));
        }

        return WriteHeadersAsync(stream, status, reason, headers);
    }

    private static async Task WriteHeadersAsync(Stream stream, int status, string reason, IEnumerable<(string Name, string Value)> headers)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        foreach (var (name, value) in headers)
        {
            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        builder.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString())).ConfigureAwait(false);
    }
}
