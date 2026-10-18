using Avalonia.Controls;
using Avalonia.Platform;

namespace AvaloniaWebview.React;

/// <summary>
/// Describes the browser data profile used by WebView windows. Reuse
/// <see cref="Shared"/> across windows to share cookies, localStorage, and other browser data;
/// assign a fresh <see cref="CreateEphemeral"/> session to isolate a window.
/// </summary>
public sealed class ReactWebViewSession
{
    /// <summary>Gets the process-wide session reused by WebView instances by default.</summary>
    public static ReactWebViewSession Shared { get; } = new();

    /// <summary>
    /// Gets or sets the WebView2 profile name. When set, every window with this session uses the
    /// same profile inside <see cref="UserDataFolder"/>.
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Gets or sets the platform data directory. Windows uses this as the WebView2 user-data
    /// folder; Linux GTK/WPE use it as the base data directory.
    /// </summary>
    public string? UserDataFolder { get; set; }

    /// <summary>Gets or sets the cache directory on Linux GTK/WPE.</summary>
    public string? CacheFolder { get; set; }

    /// <summary>Gets or sets whether Windows WebView2 runs in InPrivate mode.</summary>
    public bool? InPrivateMode { get; set; }

    /// <summary>Gets or sets the persistent data-store identifier used by WKWebView.</summary>
    public Guid? DataStoreIdentifier { get; set; }

    /// <summary>Gets or sets whether WKWebView uses a non-persistent data store.</summary>
    public bool? NonPersistentDataStore { get; set; }

    /// <summary>Gets or sets whether WebKitGTK shares its web process between views.</summary>
    public bool? SharedProcessModel { get; set; }

    /// <summary>Gets or sets whether WebKitGTK uses an ephemeral data manager.</summary>
    public bool? EphemeralDataManager { get; set; }

    /// <summary>
    /// Creates a session whose browser data is discarded when its windows close. The exact
    /// mechanism is platform-specific: WebView2 InPrivate mode, WKWebView non-persistent store,
    /// or a WebKitGTK ephemeral data manager.
    /// </summary>
    public static ReactWebViewSession CreateEphemeral() =>
        new()
        {
            InPrivateMode = true,
            NonPersistentDataStore = true,
            EphemeralDataManager = true,
        };

    /// <summary>Applies this session to a platform environment request.</summary>
    internal void Apply(WebViewEnvironmentRequestedEventArgs args)
    {
        switch (args)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                if (!string.IsNullOrWhiteSpace(ProfileName))
                {
                    windows.ProfileName = ProfileName;
                }

                if (!string.IsNullOrWhiteSpace(UserDataFolder))
                {
                    windows.UserDataFolder = UserDataFolder;
                }

                if (InPrivateMode is { } inPrivateMode)
                {
                    windows.IsInPrivateModeEnabled = inPrivateMode;
                }

                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                if (DataStoreIdentifier is { } dataStoreIdentifier && dataStoreIdentifier != Guid.Empty)
                {
                    apple.DataStoreIdentifier = dataStoreIdentifier;
                }

                if (NonPersistentDataStore is { } nonPersistent)
                {
                    apple.NonPersistentDataStore = nonPersistent;
                }

                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                if (!string.IsNullOrWhiteSpace(UserDataFolder))
                {
                    gtk.BaseDataDirectory = UserDataFolder;
                }

                if (!string.IsNullOrWhiteSpace(CacheFolder))
                {
                    gtk.BaseCacheDirectory = CacheFolder;
                }

                if (SharedProcessModel is { } sharedProcessModel)
                {
                    gtk.SharedProcessModel = sharedProcessModel;
                }

                if (EphemeralDataManager is { } ephemeralDataManager)
                {
                    gtk.EphemeralDataManager = ephemeralDataManager;
                }

                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                if (!string.IsNullOrWhiteSpace(UserDataFolder))
                {
                    wpe.DataDirectory = UserDataFolder;
                }

                if (!string.IsNullOrWhiteSpace(CacheFolder))
                {
                    wpe.CacheDirectory = CacheFolder;
                }

                break;
            case AndroidWebViewEnvironmentRequestedEventArgs android:
                if (!string.IsNullOrWhiteSpace(ProfileName))
                {
                    android.DataDirectorySuffix = ProfileName;
                }

                break;
        }
    }
}
