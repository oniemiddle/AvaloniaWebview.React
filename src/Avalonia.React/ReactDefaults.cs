using System.Reflection;

namespace Avalonia.React;

/// <summary>
/// Reads the build-time defaults emitted by the Avalonia.React MSBuild targets into
/// <see cref="AssemblyMetadataAttribute"/> entries of the host application.
/// </summary>
internal static class ReactDefaults
{
    public const string SourceModeKey = "AvaloniaReact.SourceMode";
    public const string DevServerUrlKey = "AvaloniaReact.DevServerUrl";
    public const string EnableDevToolsKey = "AvaloniaReact.EnableDevTools";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Metadata = new(LoadMetadata);

    public static string? SourceMode => Get(SourceModeKey);

    public static Uri DevServerUrl =>
        Uri.TryCreate(Get(DevServerUrlKey), UriKind.Absolute, out var uri)
            ? uri
            : new Uri("http://127.0.0.1:5173/");

    public static bool EnableDevTools =>
        bool.TryParse(Get(EnableDevToolsKey), out var enabled) && enabled;

    public static ReactSourceMode ResolveSourceMode(Uri? devServerUrl, string? embeddedAssetsRoot) =>
        ReactSourceResolver.Resolve(
            ReactSourceMode.Auto,
            devServerUrl,
            SourceMode,
            ReactSourceResolver.EmbeddedRootExists(embeddedAssetsRoot));

    private static string? Get(string key) =>
        Metadata.Value.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static IReadOnlyDictionary<string, string> LoadMetadata()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assembly in new[] { Assembly.GetEntryAssembly(), typeof(ReactDefaults).Assembly })
        {
            if (assembly is null)
            {
                continue;
            }

            foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (!string.IsNullOrEmpty(attribute.Key) && !values.ContainsKey(attribute.Key))
                {
                    values[attribute.Key] = attribute.Value ?? string.Empty;
                }
            }
        }

        return values;
    }
}

/// <summary>
/// Resolves <see cref="ReactSourceMode.Auto"/> without touching the file system in tests.
/// </summary>
internal static class ReactSourceResolver
{
    public static ReactSourceMode Resolve(
        ReactSourceMode configured,
        Uri? devServerUrl,
        string? metadataSourceMode,
        bool embeddedRootExists)
    {
        if (configured != ReactSourceMode.Auto)
        {
            return configured;
        }

        // An explicit URL wins over build metadata; the caller asked for a specific server.
        if (devServerUrl is not null)
        {
            return ReactSourceMode.DevServer;
        }

        if (Enum.TryParse<ReactSourceMode>(metadataSourceMode, ignoreCase: true, out var metadataMode) &&
            metadataMode is ReactSourceMode.DevServer or ReactSourceMode.EmbeddedAssets)
        {
            return metadataMode;
        }

        return embeddedRootExists ? ReactSourceMode.EmbeddedAssets : ReactSourceMode.DevServer;
    }

    public static bool EmbeddedRootExists(string? rootPath) =>
        Directory.Exists(string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AppContext.BaseDirectory, "wwwroot")
            : Path.GetFullPath(rootPath));
}

/// <summary>
/// Helpers for deciding whether a navigation stays inside the application.
/// </summary>
internal static class ReactUriPolicy
{
    public static Uri? GetOrigin(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return null;
        }

        return IsHttp(uri) ? new Uri(uri.GetLeftPart(UriPartial.Authority)) : null;
    }

    public static bool IsHttp(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsSameOrigin(Uri left, Uri right) =>
        IsHttp(left) &&
        IsHttp(right) &&
        string.Equals(left.GetLeftPart(UriPartial.Authority), right.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
