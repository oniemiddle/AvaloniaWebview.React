using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.React;

namespace Avalonia.React.Tests;

public sealed class EmbeddedAssetServerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avalonia-react-tests-" + Guid.NewGuid().ToString("N"));
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
