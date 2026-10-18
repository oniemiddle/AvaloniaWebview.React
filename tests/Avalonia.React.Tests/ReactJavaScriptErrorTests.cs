using System.Text.Json;
using Avalonia.React;

namespace Avalonia.React.Tests;

public sealed class ReactJavaScriptErrorTests
{
    [Fact]
    public void Parses_a_window_error_payload()
    {
        using var document = JsonDocument.Parse("""
            {
              "kind": "error",
              "message": "Cannot read properties of undefined",
              "stack": "at App",
              "source": "http://127.0.0.1/app.js",
              "line": 12,
              "column": 5
            }
            """);

        var error = ReactJavaScriptErrorParser.Parse(document.RootElement);

        Assert.Equal("Cannot read properties of undefined", error.Message);
        Assert.Equal("at App", error.Stack);
        Assert.Equal("http://127.0.0.1/app.js", error.Source);
        Assert.Equal(12, error.Line);
        Assert.Equal(5, error.Column);
        Assert.False(error.IsUnhandledRejection);
    }

    [Fact]
    public void Parses_an_unhandled_rejection_payload()
    {
        using var document = JsonDocument.Parse("""
            { "kind": "unhandledrejection", "message": "network failed" }
            """);

        var error = ReactJavaScriptErrorParser.Parse(document.RootElement);

        Assert.True(error.IsUnhandledRejection);
        Assert.Equal("network failed", error.Message);
        Assert.Null(error.Stack);
        Assert.Null(error.Line);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Falls_back_for_non_object_payloads(string json)
    {
        using var document = JsonDocument.Parse(json);

        var error = ReactJavaScriptErrorParser.Parse(document.RootElement);

        Assert.Contains("reported an error", error.Message);
        Assert.Null(error.Source);
    }

    [Fact]
    public void Ignores_missing_or_mistyped_properties()
    {
        using var document = JsonDocument.Parse("""
            { "line": "not a number", "column": 1.5, "stack": 42 }
            """);

        var error = ReactJavaScriptErrorParser.Parse(document.RootElement);

        Assert.Contains("reported an error", error.Message);
        Assert.Null(error.Stack);
        Assert.Null(error.Line);
        Assert.Null(error.Column);
    }
}
