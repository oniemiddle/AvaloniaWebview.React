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
            payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : JsonSerializer.SerializeToElement<object?>(null);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Returns whether a message type is reserved by the bridge.</summary>
    public static bool IsReserved(string type) =>
        type is ReactBridgeMessages.Ready
            or ReactBridgeMessages.Invoke
            or ReactBridgeMessages.InvokeResult
            or ReactBridgeMessages.Theme
            or ReactBridgeMessages.ThemeRequest
            or ReactBridgeMessages.Error;
}
