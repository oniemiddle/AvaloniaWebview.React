namespace Avalonia.React;

/// <summary>
/// Reserved message types understood by <see cref="AvaloniaReactWebView"/> and the
/// <c>@avalonia-react/bridge</c> client. Application messages must not use these names.
/// </summary>
public static class ReactBridgeMessages
{
    /// <summary>Sent by the React client once it can receive host messages.</summary>
    public const string Ready = "avalonia:ready";

    /// <summary>Sent by the React client to invoke a registered host API.</summary>
    public const string Invoke = "avalonia:invoke";

    /// <summary>Sent by the host with the result of a host API invocation.</summary>
    public const string InvokeResult = "avalonia:invoke:result";

    /// <summary>Sent by the host whenever the effective theme variant changes.</summary>
    public const string Theme = "theme";

    /// <summary>Sent by the React client to request the current theme immediately.</summary>
    public const string ThemeRequest = "theme:request";

    /// <summary>The DOM event name used by the host to deliver messages to JavaScript.</summary>
    public const string DomainEventName = "avalonia-react:message";
}
