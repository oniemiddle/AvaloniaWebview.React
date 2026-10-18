using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.React.EmbeddedAssets;

namespace Avalonia.React.Tests;

public sealed class EmbeddedAssetServerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "test-assets", Guid.NewGuid().ToString("N"));
    private EmbeddedAssetServer _server = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<html>root</html>");
        File.WriteAllText(Path.Combine(_root, "assets", "app.js"), "console.log('app');");
        File.WriteAllBytes(Path.Combine(_root, "assets", "data.bin"), Encoding.ASCII.GetBytes("0123456789"));
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "secret");

        _server = new EmbeddedAssetServer(new EmbeddedAssetOptions { RootPath = _root });
        _client = new HttpClient { BaseAddress = _server.BaseUri };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Serves_index_with_no_cache()
    {
        using var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal("<html>root</html>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Serves_assets_with_immutable_cache()
    {
        using var response = await _client.GetAsync("/assets/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal("console.log('app');", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Falls_back_to_index_for_spa_routes()
    {
        using var response = await _client.GetAsync("/workspace/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<html>root</html>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Does_not_fall_back_for_missing_assets()
    {
        using var response = await _client.GetAsync("/assets/missing.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Supports_head_and_single_ranges()
    {
        using var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/assets/data.bin"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(10, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/data.bin");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
        Assert.Equal("bytes 2-5/10", response.Content.Headers.ContentRange?.ToString());
    }

    [Fact]
    public async Task Returns_not_modified_for_matching_etag()
    {
        using var first = await _client.GetAsync("/assets/app.js");
        var etag = first.Headers.ETag;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/app.js");
        request.Headers.IfNoneMatch.Add(etag!);
        using var second = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Rejects_foreign_host_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "example.com";
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_post()
    {
        using var response = await _client.PostAsync("/", new StringContent(""));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_encoded_path_traversal()
    {
        var statusLine = await SendRawAsync("GET /%2e%2e/secret.txt HTTP/1.1");

        Assert.Contains(" 404 ", statusLine);
    }

    [Fact]
    public async Task Disables_caching_when_configured()
    {
        await using var server = new EmbeddedAssetServer(new EmbeddedAssetOptions
        {
            RootPath = _root,
            EnableCaching = false,
        });
        using var client = new HttpClient { BaseAddress = server.BaseUri };
        using var response = await client.GetAsync("/assets/app.js");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void Throws_when_the_asset_root_is_missing()
    {
        var missingRoot = Path.Combine(_root, "missing");

        var exception = Assert.Throws<DirectoryNotFoundException>(
            () => new EmbeddedAssetServer(new EmbeddedAssetOptions { RootPath = missingRoot }));

        Assert.Contains(missingRoot, exception.Message);
    }

    [Fact]
    public async Task Falls_back_for_dotted_routes_when_the_browser_requests_html()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/workspace/dashboard.v2");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<html>root</html>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Does_not_fall_back_for_dotted_asset_requests()
    {
        using var response = await _client.GetAsync("/workspace/dashboard.v2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Supports_last_modified_and_if_modified_since()
    {
        using var first = await _client.GetAsync("/assets/app.js");
        var lastModified = first.Content.Headers.LastModified;
        Assert.NotNull(lastModified);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/app.js");
        request.Headers.IfModifiedSince = lastModified;
        using var second = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Matches_any_etag_in_if_none_match()
    {
        using var first = await _client.GetAsync("/assets/app.js");
        var etag = first.Headers.ETag!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/app.js");
        request.Headers.IfNoneMatch.ParseAdd("\"stale\"");
        request.Headers.IfNoneMatch.Add(etag);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task Reuses_a_keep_alive_connection()
    {
        var port = _server.BaseUri.Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);

        var first = await SendKeepAliveRequestAsync(reader, stream, port, "/index.html");
        Assert.Contains(" 200 ", first.StatusLine);
        Assert.Contains("keep-alive", first.Connection);
        Assert.Equal("<html>root</html>", first.Body);

        var second = await SendKeepAliveRequestAsync(reader, stream, port, "/assets/app.js");
        Assert.Contains(" 200 ", second.StatusLine);
        Assert.Equal("console.log('app');", second.Body);
    }

    [Fact]
    public async Task Closes_the_connection_when_keep_alive_is_disabled()
    {
        await using var server = new EmbeddedAssetServer(new EmbeddedAssetOptions
        {
            RootPath = _root,
            EnableKeepAlive = false,
        });
        var port = server.BaseUri.Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);

        var response = await SendKeepAliveRequestAsync(reader, stream, port, "/index.html");

        Assert.Contains("close", response.Connection);
    }

    private static async Task<RawResponse> SendKeepAliveRequestAsync(StreamReader reader, Stream stream, int port, string path)
    {
        var request = $"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        await stream.FlushAsync();

        var statusLine = await reader.ReadLineAsync() ?? string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            var separator = line.IndexOf(':');
            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var body = string.Empty;
        if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) && length > 0)
        {
            var buffer = new char[length];
            var read = 0;
            while (read < length)
            {
                var chunk = await reader.ReadAsync(buffer.AsMemory(read, length - read));
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            body = new string(buffer, 0, read);
        }

        return new RawResponse(statusLine, body, headers.GetValueOrDefault("Connection", string.Empty));
    }

    private sealed record RawResponse(string StatusLine, string Body, string Connection);

    [Fact]
    public void Uses_sensible_asset_server_defaults()
    {
        var options = new EmbeddedAssetOptions();

        Assert.Equal("index.html", options.SpaFallback);
        Assert.True(options.EnableCaching);
        Assert.True(options.EnableKeepAlive);
        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleConnectionTimeout);
        Assert.Equal(64, options.MaxConnections);
    }

    private async Task<string> SendRawAsync(string requestLine)
    {
        var port = _server.BaseUri.Port;
        var request = $"{requestLine}\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n";

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadLineAsync() ?? string.Empty;
    }
}
