using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace AvaloniaWebview.React;

/// <summary>
/// Describes the lifecycle of the React page hosted by <see cref="AvaloniaReactWebView"/>.
/// </summary>
public enum ReactLoadState
{
    /// <summary>The WebView has no source yet, or the source has not started navigating.</summary>
    Idle,

    /// <summary>A navigation is in progress.</summary>
    Loading,

    /// <summary>The page navigated successfully and can receive bridge messages.</summary>
    Loaded,

    /// <summary>The last navigation failed. Inspect <see cref="AvaloniaReactWebView.LoadError"/>.</summary>
    Failed,
}

/// <summary>
/// Provides data for <see cref="AvaloniaReactWebView.LoadStateChanged"/>.
/// </summary>
public sealed class ReactLoadStateChangedEventArgs(ReactLoadState state, string? error) : EventArgs
{
    /// <summary>Gets the new load state.</summary>
    public ReactLoadState State { get; } = state;

    /// <summary>Gets the failure message when <see cref="State"/> is <see cref="ReactLoadState.Failed"/>.</summary>
    public string? Error { get; } = error;
}

/// <summary>
/// Provides data for <see cref="AvaloniaReactWebView.BridgeReady"/>, raised when the React client
/// sends the reserved <c>avalonia:ready</c> message.
/// </summary>
public sealed class ReactBridgeReadyEventArgs(JsonElement payload) : EventArgs
{
    /// <summary>Gets the payload sent by the React client.</summary>
    public JsonElement Payload { get; } = payload;

    /// <summary>Deserializes the ready payload to a .NET type.</summary>
    [RequiresUnreferencedCode("GetPayload uses reflection-based System.Text.Json deserialization.")]
    [RequiresDynamicCode("GetPayload JSON deserialization may require runtime code generation.")]
    public T? GetPayload<T>(JsonSerializerOptions? options = null) =>
        Payload.Deserialize<T>(options ?? JsonSerializerOptions.Web);
}

/// <summary>
/// Provides data for <see cref="AvaloniaReactWebView.BridgeError"/>, raised when an application
/// message handler throws.
/// </summary>
public sealed class ReactBridgeErrorEventArgs(Exception exception) : EventArgs
{
    /// <summary>Gets the exception thrown by the application handler.</summary>
    public Exception Exception { get; } = exception;
}
