using System.Text.Json;

namespace Avalonia.React;

/// <summary>
/// Parses the JSON envelope shared with the <c>@avalonia-react/bridge</c> client.
/// </summary>
internal static class ReactBridgeProtocol
{
    /// <summary>
    /// Attempts to read <c>{ "type": string, "payload": unknown }</c> from a raw WebView message.
    /// </summary>
    public static bool TryParse(string? body, out string type, out JsonElement payload)
    {
        type = string.Empty;
        payload = default;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var parsedType = typeElement.GetString();
            if (string.IsNullOrEmpty(parsedType))
            {
                return false;
            }

            type = parsedType;
            if (root.TryGetProperty("payload", out var payloadElement))
            {
                payload = payloadElement.Clone();
            }
            else
            {
                using var nullDocument = JsonDocument.Parse("null");
                payload = nullDocument.RootElement.Clone();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Builds the JavaScript used to deliver a host message to the React client.</summary>
    public static string CreateDomainEventScript(string type, string payloadJson) =>
        $"window.dispatchEvent(new CustomEvent('{ReactBridgeMessages.DomainEventName}', {{ detail: {{ type: {QuoteJson(type)}, payload: {payloadJson} }} }}));";

    /// <summary>Encodes a string as a JSON string literal.</summary>
    public static string QuoteJson(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    /// <summary>Returns whether a message type is reserved by the bridge.</summary>
    public static bool IsReserved(string type) =>
        type is ReactBridgeMessages.Ready
            or ReactBridgeMessages.Invoke
            or ReactBridgeMessages.InvokeResult
            or ReactBridgeMessages.InvokeCancel
            or ReactBridgeMessages.Theme
            or ReactBridgeMessages.ThemeRequest
            or ReactBridgeMessages.Error;
}
