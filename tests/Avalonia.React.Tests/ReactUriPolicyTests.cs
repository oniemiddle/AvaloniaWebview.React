namespace Avalonia.React.Tests;

public sealed class ReactUriPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5173/", "http://127.0.0.1:5173")]
    [InlineData("https://avalonia-react.local/index.html", "https://avalonia-react.local")]
    [InlineData("https://example.com:8443/path?q=1", "https://example.com:8443")]
    public void Origin_keeps_scheme_host_and_port(string value, string expected) =>
        Assert.Equal(expected, ReactUriPolicy.GetOrigin(new Uri(value))!.AbsoluteUri.TrimEnd('/'));

    [Theory]
    [InlineData("file:///C:/app/index.html")]
    [InlineData("about:blank")]
    public void Origin_is_null_for_non_http_uris(string value) =>
        Assert.Null(ReactUriPolicy.GetOrigin(new Uri(value)));

    [Theory]
    [InlineData("http://127.0.0.1:5173/a", "http://127.0.0.1:5173/b", true)]
    [InlineData("http://127.0.0.1:5173/a", "http://127.0.0.1:6000/a", false)]
    [InlineData("http://127.0.0.1:5173/a", "https://127.0.0.1:5173/a", false)]
    [InlineData("http://LOCALHOST:5173/", "http://localhost:5173/", true)]
    public void Origin_comparison_ignores_paths_and_host_case(string left, string right, bool expected) =>
        Assert.Equal(expected, ReactUriPolicy.IsSameOrigin(new Uri(left), new Uri(right)));

    [Fact]
    public void Http_detection_is_limited_to_http_schemes()
    {
        Assert.True(ReactUriPolicy.IsHttp(new Uri("https://example.com/")));
        Assert.True(ReactUriPolicy.IsHttp(new Uri("http://example.com/")));
        Assert.False(ReactUriPolicy.IsHttp(new Uri("file:///C:/index.html")));
        Assert.False(ReactUriPolicy.IsHttp(new Uri("about:blank")));
    }
}
