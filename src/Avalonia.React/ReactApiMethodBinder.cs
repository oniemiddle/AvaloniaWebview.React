using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Avalonia.React;

/// <summary>
/// Converts JSON arguments from the bridge into a method invocation, honoring optional parameters.
/// </summary>
internal static class ReactApiMethodBinder
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Reflection-based registration is annotated with RequiresUnreferencedCode; use generated bridge registration or a source-generated JSON context.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Reflection-based registration requires a source-generated JSON context for Native AOT; generated bridge registration avoids this path.")]
    public static object?[] Bind(
        ParameterInfo[] parameters,
        IReadOnlyList<JsonElement> arguments,
        string apiName,
        string methodName,
        CancellationToken cancellationToken = default,
        JsonSerializerOptions? serializerOptions = null)
    {
        var tokenIndex = parameters.Length > 0 &&
            parameters[^1].ParameterType == typeof(CancellationToken)
                ? parameters.Length - 1
                : -1;
        var jsonParameterCount = tokenIndex >= 0 ? tokenIndex : parameters.Length;

        if (arguments.Count > jsonParameterCount)
        {
            throw new InvalidOperationException(
                $"'{apiName}.{methodName}' expects {jsonParameterCount} arguments, but received {arguments.Count}.");
        }

        var bound = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index == tokenIndex)
            {
                bound[index] = cancellationToken;
                continue;
            }

            if (index < arguments.Count)
            {
                bound[index] = arguments[index].Deserialize(
                    parameters[index].ParameterType,
                    serializerOptions ?? DefaultOptions);
                continue;
            }

            if (!parameters[index].HasDefaultValue)
            {
                throw new InvalidOperationException(
                    $"'{apiName}.{methodName}' expects {jsonParameterCount} arguments, but received {arguments.Count}.");
            }

            bound[index] = parameters[index].DefaultValue is DBNull ? null : parameters[index].DefaultValue;
        }

        return bound;
    }
}
