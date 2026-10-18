namespace Avalonia.React;

/// <summary>
/// Selects how <see cref="AvaloniaReactWebView"/> obtains its initial source.
/// </summary>
public enum ReactSourceMode
{
    /// <summary>The application assigns <see cref="Avalonia.Controls.NativeWebView.Source"/> itself.</summary>
    Manual,

    /// <summary>Navigates to the Vite development server.</summary>
    DevServer,

    /// <summary>Serves the published frontend from <c>wwwroot</c> or <see cref="AvaloniaReactWebView.EmbeddedAssetsRoot"/>.</summary>
    EmbeddedAssets,

    /// <summary>
    /// Uses build-time defaults emitted by the Avalonia.React MSBuild targets: the Vite server for Debug
    /// builds and embedded assets otherwise. Falls back to <see cref="DevServer"/> when no metadata exists
    /// and the embedded asset root is missing.
    /// </summary>
    Auto,
}

/// <summary>
/// Controls what the WebView does when a navigation leaves the initial application origin.
/// </summary>
public enum ReactExternalNavigationPolicy
{
    /// <summary>Allow every navigation in the WebView.</summary>
    Allow,

    /// <summary>Cancel the navigation without opening a browser.</summary>
    Block,

    /// <summary>Cancel the navigation and open the target in the system browser.</summary>
    OpenInSystemBrowser,
}
