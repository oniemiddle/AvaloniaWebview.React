using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AvaloniaWebview.React;

/// <summary>
/// Holds JSON options for bridge payloads and collects generated type-info resolvers.
/// Generated DTO/enum support installs its resolver at assembly load, so payloads keep
/// working when reflection-based serialization is trimmed away.
/// </summary>
public static class ReactJsonSerializerDefaults
{
    private static readonly object Sync = new();
    private static readonly List<IJsonTypeInfoResolver> GeneratedResolvers = [];
    private static JsonSerializerOptions? _options;

    /// <summary>Gets or sets the serializer options used by new WebView instances.</summary>
    public static JsonSerializerOptions Options
    {
        get
        {
            lock (Sync)
            {
                return _options ??= CreateOptions();
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (Sync)
            {
                var options = value.IsReadOnly ? new JsonSerializerOptions(value) : value;
                if (GeneratedResolvers.Count > 0)
                {
                    var resolvers = new List<IJsonTypeInfoResolver>();
                    if (options.TypeInfoResolver is { } existing)
                    {
                        resolvers.Add(existing);
                    }

                    resolvers.AddRange(GeneratedResolvers);
                    options.TypeInfoResolver = JsonTypeInfoResolver.Combine(resolvers.ToArray());
                }

                _options = options;
            }
        }
    }

    /// <summary>
    /// Adds a generated type-info resolver. Module initializers call this before the first
    /// WebView is created.
    /// </summary>
    public static void AddTypeInfoResolver(IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        lock (Sync)
        {
            GeneratedResolvers.Add(resolver);
            if (_options is not null && !_options.IsReadOnly)
            {
                var resolvers = new List<IJsonTypeInfoResolver>(GeneratedResolvers);
                if (_options.TypeInfoResolver is { } existing)
                {
                    resolvers.Add(existing);
                }

                _options.TypeInfoResolver = JsonTypeInfoResolver.Combine(resolvers.ToArray());
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (GeneratedResolvers.Count > 0)
        {
            options.TypeInfoResolver = JsonTypeInfoResolver.Combine(GeneratedResolvers.ToArray());
        }

        return options;
    }
}
