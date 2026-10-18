using System.Net;
using System.Net.Sockets;
using AvaloniaWebview.React.EmbeddedAssets;

namespace AvaloniaWebview.React.Tests;

public sealed class EmbeddedAssetServerPortTests
{
    [Fact]
    public async Task Uses_a_fixed_loopback_port_when_configured()
    {
        var root = CreateRoot();
        var port = GetFreePort();
        var server = new EmbeddedAssetServer(new EmbeddedAssetOptions { RootPath = root, Port = port });
        try
        {
            Assert.Equal(port, server.BaseUri.Port);
            using var client = new HttpClient();
            Assert.Equal("fixed-port", await client.GetStringAsync(new Uri(server.BaseUri, "index.html")));
        }
        finally
        {
            await server.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Rejects_out_of_range_ports(int port)
    {
        var root = CreateRoot();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new EmbeddedAssetServer(new EmbeddedAssetOptions { RootPath = root, Port = port }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reports_a_clear_error_when_a_fixed_port_is_already_in_use()
    {
        var root = CreateRoot();
        var port = GetFreePort();
        using var occupied = new TcpListener(IPAddress.Loopback, port);
        occupied.Start();
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new EmbeddedAssetServer(new EmbeddedAssetOptions { RootPath = root, Port = port }));

            Assert.Contains(port.ToString(), exception.Message);
        }
        finally
        {
            occupied.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "port-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "fixed-port");
        return root;
    }
}
