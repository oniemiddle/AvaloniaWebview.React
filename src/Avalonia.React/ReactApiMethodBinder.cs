using System.Reflection;
using System.Text.Json;

namespace Avalonia.React;

/// <summary>
/// Converts JSON arguments from the bridge into a method invocation, honoring optional parameters.
/// </summary>
internal static class ReactApiMethodBinder
{
    public static object?[] Bind(
        ParameterInfo[] parameters,
        IReadOnlyList<JsonElement> arguments,
        string apiName,
        string methodName,
        CancellationToken cancellationToken = default)
    {
        var tokenIndex = parameters.Length > 0 &&
            parameters[parameters.Length - 1].ParameterType == typeof(CancellationToken)
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
                bound[index] = arguments[index].Deserialize(parameters[index].ParameterType, JsonSerializerOptions.Web);
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
