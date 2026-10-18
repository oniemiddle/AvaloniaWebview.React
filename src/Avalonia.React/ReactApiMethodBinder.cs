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
        string methodName)
    {
        if (arguments.Count > parameters.Length)
        {
            throw new InvalidOperationException(
                $"'{apiName}.{methodName}' expects {parameters.Length} arguments, but received {arguments.Count}.");
        }

        var bound = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index < arguments.Count)
            {
                bound[index] = arguments[index].Deserialize(parameters[index].ParameterType, JsonSerializerOptions.Web);
                continue;
            }

            if (!parameters[index].HasDefaultValue)
            {
                throw new InvalidOperationException(
                    $"'{apiName}.{methodName}' expects {parameters.Length} arguments, but received {arguments.Count}.");
            }

            bound[index] = parameters[index].DefaultValue is DBNull ? null : parameters[index].DefaultValue;
        }

        return bound;
    }
}
