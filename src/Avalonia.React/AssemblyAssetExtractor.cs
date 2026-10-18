using System.Collections.Concurrent;
using System.Reflection;

namespace Avalonia.React;

/// <summary>
/// Configures how frontend files embedded in an assembly are extracted before being served.
/// </summary>
public sealed class AssemblyEmbeddedAssetOptions
{
    /// <summary>Gets or sets the assembly containing the frontend resources. Defaults to the entry assembly.</summary>
    public Assembly? Assembly { get; set; }

    /// <summary>Gets or sets the manifest resource name prefix. Defaults to <c>Avalonia.React.Assets/</c>.</summary>
    public string ResourcePrefix { get; set; } = "Avalonia.React.Assets/";

    /// <summary>
    /// Gets or sets the directory that receives the extracted files. Defaults to a versioned directory
    /// under the system temporary directory.
    /// </summary>
    public string? ExtractionRoot { get; set; }

    /// <summary>Gets or sets whether existing extracted files are overwritten even when the marker matches.</summary>
    public bool ForceExtraction { get; set; }
}

/// <summary>
/// Extracts frontend files embedded as manifest resources so the normal loopback asset server can serve them.
/// </summary>
internal static class AssemblyAssetExtractor
{
    public const string DefaultResourcePrefix = "Avalonia.React.Assets/";
    private const string MarkerFileName = ".avalonia-react-extracted";

    private static readonly ConcurrentDictionary<string, Lazy<string>> Cache = new(StringComparer.Ordinal);

    public static string Extract(
        Assembly assembly,
        string resourcePrefix,
        string? extractionRoot,
        bool forceExtraction)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        var prefix = NormalizePrefix(resourcePrefix);
        var root = string.IsNullOrWhiteSpace(extractionRoot)
            ? Path.Combine(
                Path.GetTempPath(),
                "avalonia-react",
                $"{Sanitize(assembly.GetName().Name ?? "host")}-{assembly.ManifestModule.ModuleVersionId:N}")
            : Path.GetFullPath(extractionRoot);
        root = Path.TrimEndingDirectorySeparator(root);
        var marker = $"{assembly.FullName}|{assembly.ManifestModule.ModuleVersionId}|{prefix}";
        var cacheKey = $"{root}|{marker}|{forceExtraction}";

        var lazy = Cache.GetOrAdd(
            cacheKey,
            _ => new Lazy<string>(() => ExtractCore(assembly, prefix, root, marker, forceExtraction), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            Cache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private static string ExtractCore(Assembly assembly, string prefix, string root, string marker, bool forceExtraction)
    {
        var markerPath = Path.Combine(root, MarkerFileName);
        if (!forceExtraction && File.Exists(markerPath) && File.ReadAllText(markerPath) == marker)
        {
            return root;
        }

        var resources = assembly.GetManifestResourceNames()
            .Select(name => new
            {
                Name = name,
                Normalized = name.Replace('\\', '/'),
            })
            .Where(resource => resource.Normalized.StartsWith(prefix, StringComparison.Ordinal))
            .Select(resource => new
            {
                resource.Name,
                Relative = resource.Normalized[prefix.Length..],
            })
            .Where(resource => resource.Relative.Length > 0)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' contains no embedded frontend resources with prefix '{prefix}'. " +
                "Set AvaloniaReactEmbedAssetsInAssembly=true in the host project so the build embeds the frontend output.");
        }

        Directory.CreateDirectory(root);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        foreach (var resource in resources)
        {
            var relative = resource.Relative.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) ||
                relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "." or ".."))
            {
                throw new InvalidOperationException($"Embedded resource '{resource.Name}' has an unsafe relative path.");
            }

            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Embedded resource '{resource.Name}' resolves outside the extraction root.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = assembly.GetManifestResourceStream(resource.Name)
                ?? throw new InvalidOperationException($"Embedded resource '{resource.Name}' could not be opened.");
            using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(file);
        }

        File.WriteAllText(markerPath, marker);
        return root;
    }

    private static string NormalizePrefix(string prefix)
    {
        var normalized = prefix.Replace('\\', '/');
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static string Sanitize(string value)
    {
        var sanitized = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray());
        return sanitized.Length == 0 ? "host" : sanitized;
    }
}
