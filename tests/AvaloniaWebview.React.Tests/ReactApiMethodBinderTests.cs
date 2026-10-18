using System.Reflection;
using System.Text.Json;

namespace AvaloniaWebview.React.Tests;

public sealed class ReactApiMethodBinderTests
{
    [Fact]
    public void Binds_required_arguments()
    {
        var arguments = Bind("Ada", "hello");

        Assert.Equal("Ada", arguments[0]);
        Assert.Equal("hello", arguments[1]);
        Assert.Equal(42, arguments[2]);
    }

    [Fact]
    public void Fills_missing_optional_arguments_with_their_defaults()
    {
        var arguments = Bind("Ada");

        Assert.Equal("Ada", arguments[0]);
        Assert.Null(arguments[1]);
        Assert.Equal(42, arguments[2]);
    }

    [Fact]
    public void Explicit_null_overrides_a_default()
    {
        var arguments = Bind("Ada", null);

        Assert.Null(arguments[1]);
        Assert.Equal(42, arguments[2]);
    }

    [Fact]
    public void Injects_the_host_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        var method = typeof(ReactApiMethodBinderTests).GetMethod(
            nameof(TokenTarget),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new[] { JsonSerializer.SerializeToElement("Ada", JsonSerializerOptions.Web) };

        var bound = ReactApiMethodBinder.Bind(method.GetParameters(), arguments, "TestApi", "token", cancellation.Token);

        Assert.Equal("Ada", bound[0]);
        Assert.Equal(cancellation.Token, bound[1]);
    }

    [Fact]
    public void Uses_the_provided_serializer_options()
    {
        var method = typeof(ReactApiMethodBinderTests).GetMethod(
            nameof(OptionsTarget),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        using var document = JsonDocument.Parse("""{"full_name":"Ada"}""");
        var arguments = new[] { document.RootElement.Clone() };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
        };

        var bound = ReactApiMethodBinder.Bind(
            method.GetParameters(),
            arguments,
            "TestApi",
            "options",
            serializerOptions: options);

        Assert.Equal("Ada", ((OptionsDto)bound[0]!).FullName);
    }

    [Fact]
    public void Rejects_a_missing_required_argument()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Bind());

        Assert.Contains("expects 3 arguments", exception.Message);
    }

    [Fact]
    public void Rejects_too_many_arguments()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Bind("Ada", "hello", 1, "extra"));

        Assert.Contains("expects 3 arguments", exception.Message);
    }

    private static object?[] Bind(params object?[] values)
    {
        var method = typeof(ReactApiMethodBinderTests).GetMethod(
            nameof(Target),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = values
            .Select(value => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web))
            .ToArray();
        return ReactApiMethodBinder.Bind(method.GetParameters(), arguments, "TestApi", "target");
    }

    private static void Target(string name, string? title = null, int count = 42)
    {
    }

    private static void TokenTarget(string name, CancellationToken cancellationToken = default)
    {
    }

    private static void OptionsTarget(OptionsDto profile)
    {
    }

    public sealed class OptionsDto
    {
        public string FullName { get; set; } = string.Empty;
    }
}
