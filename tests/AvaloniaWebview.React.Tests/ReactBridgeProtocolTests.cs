using System.Text.Json;

namespace AvaloniaWebview.React.Tests;

public sealed class ReactBridgeProtocolTests
{
    [Fact]
    public void Parses_type_and_payload()
    {
        var parsed = ReactBridgeProtocol.TryParse(
            """{"type":"account:changed","payload":{"name":"Ada"}}""",
            out var type,
            out var payload);

        Assert.True(parsed);
        Assert.Equal("account:changed", type);
        Assert.Equal("Ada", payload.GetProperty("name").GetString());
    }

    [Fact]
    public void Missing_payload_becomes_null_element()
    {
        var parsed = ReactBridgeProtocol.TryParse("""{"type":"ping"}""", out var type, out var payload);

        Assert.True(parsed);
        Assert.Equal("ping", type);
        Assert.Equal(JsonValueKind.Null, payload.ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"payload":1}""")]
    [InlineData("""{"type":42}""")]
    [InlineData("""{"type":""}""")]
    public void Rejects_invalid_envelopes(string? body)
    {
        Assert.False(ReactBridgeProtocol.TryParse(body, out _, out _));
    }

    [Theory]
    [InlineData(ReactBridgeMessages.Ready)]
    [InlineData(ReactBridgeMessages.Invoke)]
    [InlineData(ReactBridgeMessages.InvokeResult)]
    [InlineData(ReactBridgeMessages.Theme)]
    [InlineData(ReactBridgeMessages.ThemeRequest)]
    [InlineData(ReactBridgeMessages.Error)]
    public void Recognizes_reserved_types(string type) => Assert.True(ReactBridgeProtocol.IsReserved(type));

    [Fact]
    public void Application_messages_are_not_reserved() =>
        Assert.False(ReactBridgeProtocol.IsReserved("react:clicked"));

    [Fact]
    public void Builds_a_domain_event_script_with_escaped_type_and_payload()
    {
        var script = ReactBridgeProtocol.CreateDomainEventScript("weird\"type", """{"value":1}""");

        Assert.Contains("window.dispatchEvent(new CustomEvent('avalonia-react:message'", script);
        Assert.Contains("weird\\u0022type", script);
        Assert.Contains("payload: {\"value\":1}", script);
    }

    [Fact]
    public void Quotes_json_strings()
    {
        Assert.Equal("\"plain\"", ReactBridgeProtocol.QuoteJson("plain"));
        Assert.Equal("\"a\\u0022b\"", ReactBridgeProtocol.QuoteJson("a\"b"));
    }
}
