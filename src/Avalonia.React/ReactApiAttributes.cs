namespace Avalonia.React;

/// <summary>
/// Marks a generated C# interface with the JavaScript API name declared by the TypeScript contract.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
public sealed class ReactApiContractAttribute(string name) : Attribute
{
    /// <summary>Gets the JavaScript API name used by <c>invokeHostApi</c>.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Marks a generated C# interface method with the original TypeScript method name used by
/// <c>invokeHostApi</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ReactApiMethodAttribute(string name) : Attribute
{
    /// <summary>Gets the JavaScript method name used by <c>invokeHostApi</c>.</summary>
    public string Name { get; } = name;
}
