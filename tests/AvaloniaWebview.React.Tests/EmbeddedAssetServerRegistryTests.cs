using AvaloniaWebview.React.EmbeddedAssets;

namespace AvaloniaWebview.React.Tests;

public sealed class EmbeddedAssetServerRegistryTests
{
    [Fact]
    public async Task Shares_one_server_for_identical_options_and_keeps_it_alive_until_last_release()
    {
        var root = CreateRoot();
        try
        {
            var options = new EmbeddedAssetOptions { RootPath = root };
            var first = EmbeddedAssetServerRegistry.Acquire(options);
            var second = EmbeddedAssetServerRegistry.Acquire(options);
            try
            {
                Assert.Equal(first.BaseUri, second.BaseUri);
                using var client = new HttpClient();
                Assert.Equal("registry", await client.GetStringAsync(new Uri(first.BaseUri, "index.html")));

                await first.DisposeAsync();
                Assert.Equal("registry", await client.GetStringAsync(new Uri(second.BaseUri, "index.html")));
            }
            finally
            {
                await first.DisposeAsync();
                await second.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Does_not_share_servers_for_different_asset_options()
    {
        var firstRoot = CreateRoot("one");
        var secondRoot = CreateRoot("two");
        try
        {
            var first = EmbeddedAssetServerRegistry.Acquire(new EmbeddedAssetOptions { RootPath = firstRoot });
            var second = EmbeddedAssetServerRegistry.Acquire(new EmbeddedAssetOptions { RootPath = secondRoot });
            try
            {
                Assert.NotEqual(first.BaseUri, second.BaseUri);
            }
            finally
            {
                await first.DisposeAsync();
                await second.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(firstRoot, recursive: true);
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Releasing_a_shared_transport_twice_is_safe()
    {
        var root = CreateRoot();
        try
        {
            var transport = EmbeddedAssetServerRegistry.Acquire(new EmbeddedAssetOptions { RootPath = root });
            await transport.DisposeAsync();
            await transport.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot(string content = "registry")
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-assets", "registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), content);
        return root;
    }
}
