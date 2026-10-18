using System.Text.Json;

namespace AvaloniaWebview.React;

/// <summary>
/// Describes a single host API invocation received from React.
/// </summary>
public sealed class ReactApiInvocationContext
{
    internal ReactApiInvocationContext(
        IReadOnlyList<JsonElement> arguments,
        CancellationToken cancellationToken,
        JsonSerializerOptions jsonSerializerOptions)
    {
        Arguments = arguments;
        CancellationToken = cancellationToken;
        JsonSerializerOptions = jsonSerializerOptions;
    }

    /// <summary>Gets the JSON arguments supplied by the React caller.</summary>
    public IReadOnlyList<JsonElement> Arguments { get; }

    /// <summary>Gets the token canceled when the caller aborts, the page navigates, or the host shuts down.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the serializer options to use for argument and result payloads.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; }
}

/// <summary>
/// Invokes a generated host API method without reflection.
/// </summary>
public delegate Task<object?> ReactApiMethodHandler(ReactApiInvocationContext context);
