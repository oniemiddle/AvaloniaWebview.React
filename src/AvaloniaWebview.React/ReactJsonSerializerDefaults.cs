using System.Text.Json;

namespace AvaloniaWebview.React;

/// <summary>
/// Holds JSON options registered by a generated <c>JsonSerializerContext</c>. The source generator
/// installs these defaults at assembly load when <c>AvaloniaReactGenerateJsonContext</c> is enabled,
/// so bridge payloads keep working when reflection-based serialization is trimmed away.
/// </summary>
public static class ReactJsonSerializerDefaults
{
    /// <summary>Gets or sets the generated serializer options used by new WebView instances.</summary>
    public static JsonSerializerOptions? Options { get; set; }
}
