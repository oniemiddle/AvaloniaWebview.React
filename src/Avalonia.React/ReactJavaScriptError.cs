using System.Text.Json;

namespace Avalonia.React;

/// <summary>
/// Provides data for <see cref="AvaloniaReactWebView.JavaScriptError"/> when the React client
/// forwards a window error or an unhandled promise rejection.
/// </summary>
public sealed class ReactJavaScriptErrorEventArgs : EventArgs
{
    internal ReactJavaScriptErrorEventArgs(
        string message,
        string? stack,
        string? source,
        int? line,
        int? column,
        bool isUnhandledRejection)
    {
        Message = message;
        Stack = stack;
        Source = source;
        Line = line;
        Column = column;
        IsUnhandledRejection = isUnhandledRejection;
    }

    /// <summary>Gets the error message.</summary>
    public string Message { get; }

    /// <summary>Gets the JavaScript stack trace when the browser provided one.</summary>
    public string? Stack { get; }

    /// <summary>Gets the source URL when the browser provided one.</summary>
    public string? Source { get; }

    /// <summary>Gets the source line when the browser provided one.</summary>
    public int? Line { get; }

    /// <summary>Gets the source column when the browser provided one.</summary>
    public int? Column { get; }

    /// <summary>Gets whether the error came from an unhandled promise rejection.</summary>
    public bool IsUnhandledRejection { get; }
}

/// <summary>
/// Parses the reserved <c>avalonia:error</c> payload sent by the bridge client.
/// </summary>
internal static class ReactJavaScriptErrorParser
{
    public static ReactJavaScriptErrorEventArgs Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new ReactJavaScriptErrorEventArgs(
                "The React client reported an error.",
                null,
                null,
                null,
                null,
                false);
        }

        return new ReactJavaScriptErrorEventArgs(
            GetString(payload, "message") ?? "The React client reported an error.",
            GetString(payload, "stack"),
            GetString(payload, "source"),
            GetInt32(payload, "line"),
            GetInt32(payload, "column"),
            string.Equals(GetString(payload, "kind"), "unhandledrejection", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;
}
