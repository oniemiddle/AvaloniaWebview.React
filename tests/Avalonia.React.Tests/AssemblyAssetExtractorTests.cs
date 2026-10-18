namespace Avalonia.React.Tests;

public sealed class AssemblyAssetExtractorTests
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "extractor-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Extracts_embedded_resources_to_the_requested_directory()
    {
        try
        {
            var root = AssemblyAssetExtractor.Extract(
                typeof(AssemblyAssetExtractorTests).Assembly,
                AssemblyAssetExtractor.DefaultResourcePrefix,
                _root,
                forceExtraction: false);

            Assert.Equal(Path.GetFullPath(_root), root);
            Assert.Contains("<body>embedded</body>", File.ReadAllText(Path.Combine(root, "index.html")));
            Assert.Equal("console.log(\"embedded app\");", File.ReadAllText(Path.Combine(root, "assets", "app.js")).Trim());
            Assert.True(File.Exists(Path.Combine(root, ".avalonia-react-extracted")));
        }
        finally
        {
            DeleteRoot();
        }
    }

    [Fact]
    public void Accepts_a_prefix_with_backslashes()
    {
        try
        {
            var root = AssemblyAssetExtractor.Extract(
                typeof(AssemblyAssetExtractorTests).Assembly,
                "Avalonia.React.Assets\\",
                _root,
                forceExtraction: false);

            Assert.True(File.Exists(Path.Combine(root, "index.html")));
        }
        finally
        {
            DeleteRoot();
        }
    }

    [Fact]
    public void Force_extraction_restores_modified_files()
    {
        try
        {
            var assembly = typeof(AssemblyAssetExtractorTests).Assembly;
            var root = AssemblyAssetExtractor.Extract(assembly, AssemblyAssetExtractor.DefaultResourcePrefix, _root, false);
            File.WriteAllText(Path.Combine(root, "index.html"), "changed");

            var extracted = AssemblyAssetExtractor.Extract(assembly, AssemblyAssetExtractor.DefaultResourcePrefix, _root, forceExtraction: true);

            Assert.Contains("<body>embedded</body>", File.ReadAllText(Path.Combine(extracted, "index.html")));
        }
        finally
        {
            DeleteRoot();
        }
    }

    [Fact]
    public void Throws_when_no_resources_match_the_prefix()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AssemblyAssetExtractor.Extract(
                typeof(AssemblyAssetExtractorTests).Assembly,
                "Missing.Assets/",
                _root,
                forceExtraction: false));

        Assert.Contains("AvaloniaReactEmbedAssetsInAssembly", exception.Message);
    }

    private void DeleteRoot()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
