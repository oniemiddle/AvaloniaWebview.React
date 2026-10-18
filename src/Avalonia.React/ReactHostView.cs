using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.React.EmbeddedAssets;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avalonia.React;

/// <summary>
/// Hosts an <see cref="AvaloniaReactWebView"/> with a themed loading/error overlay and forwards the
/// most common configuration, events, and methods. Applications that need full control can use
/// <see cref="WebView"/> directly.
/// </summary>
public sealed class ReactHostView : Grid, IDisposable
{
    private readonly Border _overlay;
    private readonly StackPanel _overlayPanel;
    private readonly ProgressBar _loadingIndicator;
    private readonly TextBlock _statusText;
    private readonly Button _retryButton;
    private readonly DispatcherTimer _readyTimer;
    private bool _overlayTimedOut;

    /// <summary>Creates the host view, its WebView child, and the built-in overlay.</summary>
    public ReactHostView()
    {
        WebView = new AvaloniaReactWebView();
        Children.Add(WebView);

        _loadingIndicator = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 220,
            Height = 4,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        _statusText = new TextBlock
        {
            Text = LoadingText,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            MaxWidth = 520,
        };
        _retryButton = new Button
        {
            Content = RetryText,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsVisible = false,
        };
        _retryButton.Click += (_, _) => WebView.Reload();

        _overlayPanel = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children =
            {
                _loadingIndicator,
                _statusText,
                _retryButton,
            },
        };
        _overlay = new Border
        {
            Child = _overlayPanel,
            IsVisible = true,
        };
        Children.Add(_overlay);

        _readyTimer = new DispatcherTimer { Interval = BridgeReadyTimeout };
        _readyTimer.Tick += HandleReadyTimeout;

        WebView.LoadStateChanged += HandleLoadStateChanged;
        WebView.BridgeReady += HandleBridgeReady;
        ActualThemeVariantChanged += (_, _) => ApplyOverlayTheme();
        ApplyOverlayTheme();
        UpdateOverlay();
    }

    /// <summary>Gets the underlying WebView. Use it for advanced configuration and raw events.</summary>
    public AvaloniaReactWebView WebView { get; }

    /// <summary>Gets whether <see cref="Shutdown"/> has been called.</summary>
    public bool IsShutdown => WebView.IsShutdown;

    /// <summary>Stops the WebView, overlay timer, and embedded asset transport.</summary>
    public void Shutdown()
    {
        _readyTimer.Stop();
        WebView.Shutdown();
        _overlay.IsVisible = false;
    }

    /// <inheritdoc />
    public void Dispose() => Shutdown();

    /// <summary>Gets or sets how the initial source is selected.</summary>
    public ReactSourceMode SourceMode
    {
        get => WebView.SourceMode;
        set => WebView.SourceMode = value;
    }

    /// <summary>Gets or sets the Vite development server URL.</summary>
    public Uri? DevServerUrl
    {
        get => WebView.DevServerUrl;
        set => WebView.DevServerUrl = value;
    }

    /// <summary>Gets or sets the published frontend directory.</summary>
    public string? EmbeddedAssetsRoot
    {
        get => WebView.EmbeddedAssetsRoot;
        set => WebView.EmbeddedAssetsRoot = value;
    }

    /// <summary>Gets or sets the embedded asset transport.</summary>
    public EmbeddedAssetTransport EmbeddedAssetsTransport
    {
        get => WebView.EmbeddedAssetsTransport;
        set => WebView.EmbeddedAssetsTransport = value;
    }

    /// <summary>Gets or sets the external navigation policy.</summary>
    public ReactExternalNavigationPolicy ExternalNavigationPolicy
    {
        get => WebView.ExternalNavigationPolicy;
        set => WebView.ExternalNavigationPolicy = value;
    }

    /// <summary>Gets or sets whether platform dev tools are requested.</summary>
    public bool EnableDevTools
    {
        get => WebView.EnableDevTools;
        set => WebView.EnableDevTools = value;
    }

    /// <summary>Gets or sets whether Windows offscreen composition is requested.</summary>
    public bool EnableWindowsOffscreenComposition
    {
        get => WebView.EnableWindowsOffscreenComposition;
        set => WebView.EnableWindowsOffscreenComposition = value;
    }

    /// <summary>Gets or sets how long a host API implementation may run before a timeout error.</summary>
    public TimeSpan ApiInvokeTimeout
    {
        get => WebView.ApiInvokeTimeout;
        set => WebView.ApiInvokeTimeout = value;
    }

    /// <summary>Gets the additional origins allowed to navigate inside the WebView.</summary>
    public IList<Uri> AllowedOrigins => WebView.AllowedOrigins;

    /// <summary>Gets the current page lifecycle state.</summary>
    public ReactLoadState LoadState => WebView.LoadState;

    /// <summary>Gets the failure message when the page failed to load.</summary>
    public string? LoadError => WebView.LoadError;

    /// <summary>Gets whether the page navigated successfully.</summary>
    public bool IsPageLoaded => WebView.IsPageLoaded;

    /// <summary>Gets whether the React client sent its ready handshake.</summary>
    public bool IsBridgeReady => WebView.IsBridgeReady;

    /// <summary>Gets whether the built-in overlay is currently visible.</summary>
    public bool IsOverlayVisible => _overlay.IsVisible;

    /// <summary>Gets or sets whether the built-in loading/error overlay is used. Defaults to <see langword="true"/>.</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the overlay waits for <c>avalonia:ready</c> instead of hiding as soon as
    /// navigation completes. Defaults to <see langword="false"/>.
    /// </summary>
    public bool WaitForBridgeReady { get; set; }

    /// <summary>
    /// Gets or sets how long the overlay waits for <c>avalonia:ready</c> before hiding anyway.
    /// Defaults to 10 seconds.
    /// </summary>
    public TimeSpan BridgeReadyTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the loading message. Defaults to <c>Loading React…</c>.</summary>
    public string LoadingText { get; set; } = "Loading React…";

    /// <summary>
    /// Gets or sets the error message format. The first placeholder receives
    /// <see cref="LoadError"/>. Defaults to <c>The React app failed to load: {0}</c>.
    /// </summary>
    public string ErrorTextFormat { get; set; } = "The React app failed to load: {0}";

    /// <summary>Gets or sets the retry button text. Defaults to <c>Retry</c>.</summary>
    public string RetryText { get; set; } = "Retry";

    /// <summary>
    /// Gets or sets a custom overlay background. When <see langword="null"/> the background follows
    /// the light/dark theme.
    /// </summary>
    public IBrush? OverlayBackground { get; set; }

    /// <summary>Raised for application messages from the React client.</summary>
    public event EventHandler<ReactMessageReceivedEventArgs>? MessageReceived
    {
        add => WebView.MessageReceived += value;
        remove => WebView.MessageReceived -= value;
    }

    /// <summary>Raised when <see cref="LoadState"/> changes.</summary>
    public event EventHandler<ReactLoadStateChangedEventArgs>? LoadStateChanged
    {
        add => WebView.LoadStateChanged += value;
        remove => WebView.LoadStateChanged -= value;
    }

    /// <summary>Raised when the React client sends its ready handshake.</summary>
    public event EventHandler<ReactBridgeReadyEventArgs>? BridgeReady
    {
        add => WebView.BridgeReady += value;
        remove => WebView.BridgeReady -= value;
    }

    /// <summary>Raised when an application message handler throws.</summary>
    public event EventHandler<ReactBridgeErrorEventArgs>? BridgeError
    {
        add => WebView.BridgeError += value;
        remove => WebView.BridgeError -= value;
    }

    /// <summary>Raised when the React client forwards a JavaScript error.</summary>
    public event EventHandler<ReactJavaScriptErrorEventArgs>? JavaScriptError
    {
        add => WebView.JavaScriptError += value;
        remove => WebView.JavaScriptError -= value;
    }

    /// <summary>Registers an implementation of a generated API contract.</summary>
    [RequiresUnreferencedCode("Host API registration reflects over the interface methods and the implementation type.")]
    [RequiresDynamicCode("Host API invocation may require runtime code generation for JSON serialization.")]
    public void RegisterApi<TApi>(TApi implementation, string? apiName = null) where TApi : class =>
        WebView.RegisterApi(implementation, apiName);

    /// <summary>Removes a previously registered API.</summary>
    public bool UnregisterApi(string apiName) => WebView.UnregisterApi(apiName);

    /// <summary>Sends a JSON message to the React client.</summary>
    [RequiresUnreferencedCode("PostMessageAsync serializes the payload with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("PostMessageAsync JSON serialization may require runtime code generation.")]
    public Task<string?> PostMessageAsync<T>(string type, T payload) => WebView.PostMessageAsync(type, payload);

    /// <summary>Reloads the current page.</summary>
    public bool Reload() => WebView.Reload();

    /// <summary>Applies <see cref="SourceMode"/> immediately.</summary>
    public bool ApplySourceMode() => WebView.ApplySourceMode();

    /// <summary>Starts serving a published frontend and navigates to it.</summary>
    public Uri UseEmbeddedAssets(Action<EmbeddedAssetOptions>? configure = null) => WebView.UseEmbeddedAssets(configure);

    /// <summary>Extracts frontend files embedded in the host assembly and navigates to them.</summary>
    public Uri UseAssemblyEmbeddedAssets(
        Action<AssemblyEmbeddedAssetOptions>? configureExtraction = null,
        Action<EmbeddedAssetOptions>? configureServer = null) =>
        WebView.UseAssemblyEmbeddedAssets(configureExtraction, configureServer);

    private void HandleLoadStateChanged(object? sender, ReactLoadStateChangedEventArgs args)
    {
        _overlayTimedOut = false;
        _readyTimer.Stop();
        UpdateOverlay();
        if (args.State == ReactLoadState.Loaded && WaitForBridgeReady && !WebView.IsBridgeReady && BridgeReadyTimeout > TimeSpan.Zero)
        {
            _readyTimer.Interval = BridgeReadyTimeout;
            _readyTimer.Start();
        }
    }

    private void HandleBridgeReady(object? sender, ReactBridgeReadyEventArgs args)
    {
        _readyTimer.Stop();
        _overlayTimedOut = true;
        UpdateOverlay();
    }

    private void HandleReadyTimeout(object? sender, EventArgs args)
    {
        _readyTimer.Stop();
        _overlayTimedOut = true;
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        var failed = WebView.LoadState == ReactLoadState.Failed;
        _overlay.IsVisible = ReactOverlayPolicy.ShouldShow(
            ShowOverlay,
            WebView.LoadState,
            WebView.IsBridgeReady,
            WaitForBridgeReady,
            _overlayTimedOut);

        _loadingIndicator.IsVisible = !failed;
        _retryButton.IsVisible = failed;
        _statusText.Text = failed
            ? string.Format(CultureInfo.CurrentCulture, ErrorTextFormat, WebView.LoadError)
            : LoadingText;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyOverlayTheme();
        UpdateOverlay();
    }

    private void ApplyOverlayTheme()
    {
        if (OverlayBackground is not null)
        {
            _overlay.Background = OverlayBackground;
            return;
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        _overlay.Background = new SolidColorBrush(dark
            ? Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E)
            : Color.FromArgb(0xE6, 0xFA, 0xFA, 0xFA));
        _statusText.Foreground = new SolidColorBrush(dark ? Colors.White : Colors.Black);
    }
}

/// <summary>
/// Decides whether the built-in overlay should be visible.
/// </summary>
internal static class ReactOverlayPolicy
{
    public static bool ShouldShow(
        bool showOverlay,
        ReactLoadState state,
        bool bridgeReady,
        bool waitForBridgeReady,
        bool timedOut)
    {
        if (!showOverlay)
        {
            return false;
        }

        if (state != ReactLoadState.Loaded)
        {
            return true;
        }

        return waitForBridgeReady && !bridgeReady && !timedOut;
    }
}
