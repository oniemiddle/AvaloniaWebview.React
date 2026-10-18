namespace AvaloniaWebview.React;

/// <summary>
/// Describes a bridge request that was rejected by the host security boundary.
/// </summary>
public sealed class ReactSecurityViolationEventArgs : EventArgs
{
    internal ReactSecurityViolationEventArgs(
        string reason,
        Uri? origin,
        string? apiName = null,
        string? methodName = null)
    {
        Reason = reason;
        Origin = origin;
        ApiName = apiName;
        MethodName = methodName;
    }

    /// <summary>Gets the reason the request was rejected.</summary>
    public string Reason { get; }

    /// <summary>Gets the page origin that triggered the violation, when known.</summary>
    public Uri? Origin { get; }

    /// <summary>Gets the rejected API name, when the violation came from a host API invocation.</summary>
    public string? ApiName { get; }

    /// <summary>Gets the rejected API method name, when the violation came from a host API invocation.</summary>
    public string? MethodName { get; }
}
