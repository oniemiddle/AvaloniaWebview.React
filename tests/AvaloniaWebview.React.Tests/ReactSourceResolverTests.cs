namespace AvaloniaWebview.React.Tests;

public sealed class ReactSourceResolverTests
{
    [Theory]
    [InlineData(ReactSourceMode.Manual)]
    [InlineData(ReactSourceMode.DevServer)]
    [InlineData(ReactSourceMode.EmbeddedAssets)]
    public void Non_auto_modes_pass_through(ReactSourceMode mode) =>
        Assert.Equal(mode, ReactSourceResolver.Resolve(mode, null, null, embeddedRootExists: true));

    [Fact]
    public void Auto_prefers_an_explicit_dev_server_url()
    {
        var resolved = ReactSourceResolver.Resolve(
            ReactSourceMode.Auto,
            new Uri("http://127.0.0.1:6000/"),
            metadataSourceMode: "EmbeddedAssets",
            embeddedRootExists: true);

        Assert.Equal(ReactSourceMode.DevServer, resolved);
    }

    [Theory]
    [InlineData("DevServer", ReactSourceMode.DevServer)]
    [InlineData("EmbeddedAssets", ReactSourceMode.EmbeddedAssets)]
    [InlineData("AssemblyEmbeddedAssets", ReactSourceMode.AssemblyEmbeddedAssets)]
    [InlineData("devserver", ReactSourceMode.DevServer)]
    public void Auto_uses_build_metadata(string metadata, ReactSourceMode expected) =>
        Assert.Equal(
            expected,
            ReactSourceResolver.Resolve(ReactSourceMode.Auto, null, metadata, embeddedRootExists: false));

    [Fact]
    public void Auto_falls_back_to_embedded_assets_when_wwwroot_exists() =>
        Assert.Equal(
            ReactSourceMode.EmbeddedAssets,
            ReactSourceResolver.Resolve(ReactSourceMode.Auto, null, null, embeddedRootExists: true));

    [Fact]
    public void Auto_falls_back_to_dev_server_when_wwwroot_is_missing() =>
        Assert.Equal(
            ReactSourceMode.DevServer,
            ReactSourceResolver.Resolve(ReactSourceMode.Auto, null, null, embeddedRootExists: false));

    [Fact]
    public void Auto_ignores_metadata_values_that_are_not_source_modes() =>
        Assert.Equal(
            ReactSourceMode.DevServer,
            ReactSourceResolver.Resolve(ReactSourceMode.Auto, null, "Manual", embeddedRootExists: false));

    [Fact]
    public void Embedded_root_check_uses_the_given_directory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "resolver-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(ReactSourceResolver.EmbeddedRootExists(root));
            Directory.CreateDirectory(root);
            Assert.True(ReactSourceResolver.EmbeddedRootExists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
