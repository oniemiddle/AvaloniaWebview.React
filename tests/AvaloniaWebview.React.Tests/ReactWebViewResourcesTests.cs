namespace AvaloniaWebview.React.Tests;

[Collection("EmbeddedAssetExtraction")]
public sealed class ReactWebViewResourcesTests
{
    [Fact]
    public async Task Clears_registered_embedded_asset_extraction_roots()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "cleanup-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            AssemblyAssetExtractor.Extract(
                typeof(ReactWebViewResourcesTests).Assembly,
                AssemblyAssetExtractor.DefaultResourcePrefix,
                root,
                forceExtraction: false);
            Assert.True(File.Exists(Path.Combine(root, "index.html")));

            var result = await ReactWebViewResources.ClearExtractedAssetsAsync();

            Assert.Contains(Path.GetFullPath(root), result.DeletedRoots);
            Assert.False(Directory.Exists(root));
            Assert.True(result.IsSuccess);
            Assert.True(result.DeletedFiles >= 2);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Clearing_extracted_assets_is_idempotent()
    {
        var first = await ReactWebViewResources.ClearExtractedAssetsAsync();
        var second = await ReactWebViewResources.ClearExtractedAssetsAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Empty(second.DeletedRoots);
    }

    [Fact]
    public void Browser_data_kinds_are_composable()
    {
        Assert.Equal(
            ReactWebViewDataKind.LocalStorage |
            ReactWebViewDataKind.SessionStorage |
            ReactWebViewDataKind.CacheStorage |
            ReactWebViewDataKind.IndexedDb |
            ReactWebViewDataKind.ServiceWorkers,
            ReactWebViewDataKind.BrowserStorage);
        Assert.Equal(
            ReactWebViewDataKind.BrowserStorage | ReactWebViewDataKind.Cookies,
            ReactWebViewDataKind.All);
    }

    [Fact]
    public void Cleanup_script_only_includes_requested_storage_kinds()
    {
        var script = ReactBrowserDataCleanup.BuildScript(
            ReactWebViewDataKind.LocalStorage | ReactWebViewDataKind.CacheStorage,
            "request-1");

        Assert.Contains("const requestId = \"request-1\";", script);
        Assert.Contains("const kinds = 10;", script);
        Assert.Contains("localStorage.clear()", script);
        Assert.Contains("cacheStorage", script);
        Assert.Contains("avalonia:resources:cleared", script);
    }
}
