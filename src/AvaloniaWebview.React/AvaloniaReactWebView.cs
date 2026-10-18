using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Reflection;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using AvaloniaWebview.React.EmbeddedAssets;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AvaloniaWebview.React;

/// <summary>
/// Hosts a React application in Avalonia's official native WebView and exposes a JSON message bridge.
/// </summary>
public sealed class AvaloniaReactWebView : NativeWebView
{
    private static readonly Lazy<bool> DevServerLifetime = new(RegisterDevServerLifetime);
    private readonly Dictionary<string, ApiRegistration> _apis = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ReactApiMethodHandler>> _apiHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _pendingInvocations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ReactInvokeResult>> _pendingReactInvocations = new(StringComparer.Ordinal);
    private readonly ReactMessageQueue _messageQueue = new();
    private readonly List<Uri> _allowedOrigins = [];
    private readonly List<Uri> _trustedApiOrigins = [];
    private IAssetTransport? _assetTransport;
    private EmbeddedAssetOptions? _embeddedAssetOptions;
    private bool _embeddedAssetsEnabled;
    private Window? _assetServerWindow;
    private Uri? _navigationOrigin;
    private Uri? _currentNavigationOrigin;
    private bool _sourceConfigured;
    private bool _bridgeReady;
    private bool _isShutdown;
    private IDisposable? _reparentingScope;
    private readonly DispatcherTimer _reparentingTimer;
    private ReactLoadState _loadState;

    /// <summary>
    /// Creates a React WebView and listens for messages posted by the React client package.
    /// </summary>
    public AvaloniaReactWebView()
    {
        _ = DevServerLifetime.Value;
        EnableDevTools = ReactDefaults.EnableDevTools;
        _reparentingTimer = new DispatcherTimer { Interval = ReparentingTimeout };
        _reparentingTimer.Tick += HandleReparentingTimeout;
        WebMessageReceived += HandleWebMessageReceived;
        ActualThemeVariantChanged += HandleActualThemeVariantChanged;
        NavigationStarted += HandleNavigationStarted;
        NavigationCompleted += HandleNavigationCompleted;
        NewWindowRequested += HandleNewWindowRequested;
        EnvironmentRequested += HandleEnvironmentRequested;
    }

    /// <summary>
    /// Raised when JavaScript sends a message using <c>postMessage</c> from <c>@avalonia-react/bridge</c>.
    /// Reserved bridge messages are handled by the control and are not forwarded.
    /// </summary>
    public event EventHandler<ReactMessageReceivedEventArgs>? MessageReceived;

    /// <summary>Raised when <see cref="LoadState"/> changes.</summary>
    public event EventHandler<ReactLoadStateChangedEventArgs>? LoadStateChanged;

    /// <summary>Raised once per page when the React client sends <c>avalonia:ready</c>.</summary>
    public event EventHandler<ReactBridgeReadyEventArgs>? BridgeReady;

    /// <summary>Raised when an application <see cref="MessageReceived"/> handler throws.</summary>
    public event EventHandler<ReactBridgeErrorEventArgs>? BridgeError;

    /// <summary>Raised when the React client forwards a window error or an unhandled promise rejection.</summary>
    public event EventHandler<ReactJavaScriptErrorEventArgs>? JavaScriptError;

    /// <summary>Raised when a bridge request is rejected by the host security boundary.</summary>
    public event EventHandler<ReactSecurityViolationEventArgs>? SecurityViolation;

    /// <summary>Gets the current page lifecycle state.</summary>
    public ReactLoadState LoadState => _loadState;

    /// <summary>Gets the failure message when <see cref="LoadState"/> is <see cref="ReactLoadState.Failed"/>.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Gets whether the page navigated successfully and can receive host messages.</summary>
    public bool IsPageLoaded => _loadState == ReactLoadState.Loaded;

    /// <summary>Gets whether <see cref="Shutdown"/> has been called.</summary>
    public bool IsShutdown => _isShutdown;

    /// <summary>
    /// Gets detailed diagnostics for the underlying WebView adapter, or <see langword="null"/> when
    /// the adapter has not been created yet or the platform does not expose diagnostics.
    /// </summary>
    public DetailedWebViewAdapterInfo? GetAdapterDiagnostics()
    {
        try
        {
            var info = AdapterInfo;
            return info is null ? null : WebViewAdapterInfo.GetAdapterInfo(info.Type);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Gets whether the React client has sent the <c>avalonia:ready</c> handshake for the current page.</summary>
    public bool IsBridgeReady => _bridgeReady;

    /// <summary>
    /// Gets or sets how the initial source is selected. <see cref="ReactSourceMode.Auto"/> resolves to the
    /// Vite server for Debug builds and embedded assets otherwise, based on build-time metadata emitted by
    /// the AvaloniaWebview.React MSBuild targets. Defaults to <see cref="ReactSourceMode.Manual"/> so existing
    /// applications that assign <see cref="NativeWebView.Source"/> are unaffected.
    /// </summary>
    public ReactSourceMode SourceMode { get; set; } = ReactSourceMode.Manual;

    /// <summary>Gets or sets the Vite server URL used by <see cref="ReactSourceMode.DevServer"/>.</summary>
    public Uri? DevServerUrl { get; set; }

    /// <summary>Gets or sets the published frontend directory. Defaults to <c>wwwroot</c> next to the executable.</summary>
    public string? EmbeddedAssetsRoot { get; set; }

    /// <summary>
    /// Gets or sets what happens when a navigation leaves the application origin. Defaults to
    /// <see cref="ReactExternalNavigationPolicy.OpenInSystemBrowser"/>.
    /// </summary>
    public ReactExternalNavigationPolicy ExternalNavigationPolicy { get; set; } = ReactExternalNavigationPolicy.OpenInSystemBrowser;

    /// <summary>Gets or sets whether opening the WebView dev tools is requested. Defaults from build metadata (Debug on).</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>
    /// Gets or sets whether WebView2 offscreen composition is requested on Windows. Enable this when Avalonia
    /// content must render above the WebView, for example a loading overlay. <see cref="ReactHostView"/> enables
    /// it automatically. Defaults to <see langword="false"/> for direct WebView use.
    /// </summary>
    public bool EnableWindowsOffscreenComposition { get; set; }

    /// <summary>
    /// Gets or sets whether the native WebView is preserved while the control is detached, allowing
    /// it to be reattached in another window without losing page state. Defaults to
    /// <see langword="false"/> for direct WebView use; <see cref="ReactHostView"/> enables it.
    /// </summary>
    public bool PreserveWebViewOnDetach { get; set; }

    /// <summary>
    /// Gets or sets how long a detached WebView is preserved before its reparenting scope is released
    /// and the native control is destroyed. Defaults to 10 seconds; set to <see cref="TimeSpan.Zero"/>
    /// to preserve it indefinitely until reattached or shut down.
    /// </summary>
    public TimeSpan ReparentingTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the JSON options used for bridge payloads. Applications using trimming or Native
    /// AOT can assign options backed by a source-generated <c>JsonSerializerContext</c>. Defaults to
    /// <see cref="JsonSerializerOptions.Web"/>.
    /// </summary>
    public JsonSerializerOptions BridgeJsonSerializerOptions { get; set; } =
        ReactJsonSerializerDefaults.Options ?? new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets or sets the maximum number of host-to-React messages held while the page is loading.
    /// Defaults to 1024.
    /// </summary>
    public int MaxQueuedBridgeMessages { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum size, in UTF-16 characters, of an incoming bridge message.
    /// Larger messages are dropped and reported through <see cref="SecurityViolation"/>.
    /// Defaults to 4,000,000 characters.
    /// </summary>
    public int MaxIncomingMessageCharacters { get; set; } = 4_000_000;

    /// <summary>
    /// Gets or sets how long a host API implementation may run before the caller receives a timeout
    /// error. Defaults to 30 seconds; set to <see cref="TimeSpan.Zero"/> to disable the timeout.
    /// </summary>
    public TimeSpan ApiInvokeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long the host waits for a registered React handler to return a result.
    /// Defaults to 10 seconds; set to <see cref="TimeSpan.Zero"/> to disable the timeout.
    /// </summary>
    public TimeSpan ReactInvokeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the additional origins allowed to navigate inside the WebView when
    /// <see cref="ExternalNavigationPolicy"/> is not <see cref="ReactExternalNavigationPolicy.Allow"/>.
    /// </summary>
    public IList<Uri> AllowedOrigins => _allowedOrigins;

    /// <summary>
    /// Gets the additional page origins that may invoke registered host APIs. The application origin
    /// (the first successful navigation) is trusted automatically. Origins listed in
    /// <see cref="AllowedOrigins"/> are not trusted for API calls unless they are also added here.
    /// </summary>
    public IList<Uri> TrustedApiOrigins => _trustedApiOrigins;

    /// <summary>
    /// Starts serving a published frontend from the local asset directory and navigates the WebView to it.
    /// All platforms use the same loopback HTTP server, which supports SPA fallback, caching, conditional
    /// requests, and keep-alive connections. Use this for Release builds after the frontend has been copied
    /// to <c>wwwroot</c>; point <see cref="NativeWebView.Source"/> at the Vite server during development instead.
    /// </summary>
    /// <param name="configure">Configures the asset root, SPA fallback, caching, and connection limits.</param>
    /// <returns>The URI that the WebView navigates to.</returns>
    public Uri UseEmbeddedAssets(Action<EmbeddedAssetOptions>? configure = null)
    {
        ThrowIfShutdown();
        if (_embeddedAssetsEnabled)
        {
            throw new InvalidOperationException("Embedded assets are already enabled.");
        }

        var options = new EmbeddedAssetOptions();
        configure?.Invoke(options);
        _embeddedAssetOptions = options;

        try
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                AttachAssetServerWindow(window);
            }

            return EnableEmbeddedAssets(StartLoopbackAssetServer());
        }
        catch
        {
            _embeddedAssetOptions = null;
            _assetTransport = null;
            throw;
        }
    }

    /// <summary>
    /// Extracts frontend files embedded in the host assembly and serves them through the loopback asset
    /// server. Enable embedding with the <c>AvaloniaReactEmbedAssetsInAssembly</c> project property;
    /// extraction happens once per assembly build.
    /// </summary>
    /// <param name="configureExtraction">Configures the source assembly, resource prefix, or extraction directory.</param>
    /// <param name="configureServer">Configures the asset server after the extraction root is set.</param>
    /// <returns>The URI that the WebView navigates to.</returns>
    public Uri UseAssemblyEmbeddedAssets(
        Action<AssemblyEmbeddedAssetOptions>? configureExtraction = null,
        Action<EmbeddedAssetOptions>? configureServer = null)
    {
        ThrowIfShutdown();
        var extraction = new AssemblyEmbeddedAssetOptions();
        configureExtraction?.Invoke(extraction);
        var assembly = extraction.Assembly ?? Assembly.GetEntryAssembly() ?? typeof(AvaloniaReactWebView).Assembly;
        var root = AssemblyAssetExtractor.Extract(
            assembly,
            extraction.ResourcePrefix,
            extraction.ExtractionRoot,
            extraction.ForceExtraction);

        return UseEmbeddedAssets(options =>
        {
            options.RootPath = root;
            configureServer?.Invoke(options);
        });
    }

    /// <summary>
    /// Applies <see cref="SourceMode"/> immediately. This is called automatically when the control enters the
    /// visual tree; call it explicitly only when configuring the control after it is already attached.
    /// </summary>
    /// <returns><see langword="true"/> when a source was configured; otherwise <see langword="false"/>.</returns>
    public bool ApplySourceMode()
    {
        ThrowIfShutdown();
        if (SourceMode == ReactSourceMode.Manual || _sourceConfigured || _embeddedAssetsEnabled)
        {
            return false;
        }

        var resolved = SourceMode == ReactSourceMode.Auto
            ? ReactDefaults.ResolveSourceMode(DevServerUrl, EmbeddedAssetsRoot)
            : SourceMode;

        _sourceConfigured = true;
        switch (resolved)
        {
            case ReactSourceMode.DevServer:
                Source = DevServerUrl ?? ReactDefaults.DevServerUrl;
                return true;

            case ReactSourceMode.EmbeddedAssets:
                try
                {
                    UseEmbeddedAssets(options =>
                    {
                        if (!string.IsNullOrWhiteSpace(EmbeddedAssetsRoot))
                        {
                            options.RootPath = EmbeddedAssetsRoot;
                        }
                    });
                    return true;
                }
                catch (Exception exception)
                {
                    _sourceConfigured = false;
                    SetLoadState(ReactLoadState.Failed, exception.Message);
                    return false;
                }

            case ReactSourceMode.AssemblyEmbeddedAssets:
                try
                {
                    UseAssemblyEmbeddedAssets();
                    return true;
                }
                catch (Exception exception)
                {
                    _sourceConfigured = false;
                    SetLoadState(ReactLoadState.Failed, exception.Message);
                    return false;
                }

            default:
                _sourceConfigured = false;
                return false;
        }
    }

    /// <summary>
    /// Reloads the current page and resets <see cref="LoadState"/> to <see cref="ReactLoadState.Loading"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the platform accepted the refresh request.</returns>
    public bool Reload()
    {
        ThrowIfShutdown();

        _bridgeReady = false;
        SetLoadState(ReactLoadState.Loading, null);
        var refreshed = Refresh();
        if (!refreshed)
        {
            SetLoadState(ReactLoadState.Failed, "The WebView is not ready to reload.");
        }

        return refreshed;
    }

    /// <summary>
    /// Stops the WebView, stops the embedded asset transport, fails queued messages, and clears
    /// registered APIs. The method is idempotent; after it is called, bridge operations throw
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Shutdown()
    {
        if (_isShutdown)
        {
            return;
        }

        _isShutdown = true;
        ReleaseReparentingScope();
        DetachAssetServerWindow();
        var transport = _assetTransport;
        _assetTransport = null;
        CancelPendingInvocations(dispose: true);
        CancelPendingReactInvocations("The WebView was shut down before the React handler responded.");
        _messageQueue.FailAll(new ObjectDisposedException(nameof(AvaloniaReactWebView)));
        _apis.Clear();
        _apiHandlers.Clear();
        _bridgeReady = false;
        if (transport is not null)
        {
            _ = transport.DisposeAsync().AsTask();
        }

        SetLoadState(ReactLoadState.Idle, null);
        try
        {
            Stop();
        }
        catch (Exception)
        {
            // The adapter can already be gone when the host window is closing.
        }
    }

    /// <summary>
    /// Registers an implementation of a TypeScript-generated API contract so React can invoke its methods.
    /// </summary>
    /// <typeparam name="TApi">The generated C# interface for an exported TypeScript API.</typeparam>
    /// <param name="implementation">The host-side implementation.</param>
    /// <param name="apiName">The JavaScript API name. Defaults to the generated interface name without its leading I.</param>
    [RequiresUnreferencedCode("Host API registration reflects over the interface methods and the implementation type.")]
    [RequiresDynamicCode("Host API invocation may require runtime code generation for JSON serialization.")]
    public void RegisterApi<TApi>(TApi implementation, string? apiName = null) where TApi : class
    {
        ThrowIfShutdown();
        ArgumentNullException.ThrowIfNull(implementation);
        var contract = typeof(TApi);
        if (!contract.IsInterface)
        {
            throw new ArgumentException("TApi must be an interface generated from a TypeScript API contract.", nameof(TApi));
        }

        var name = apiName
            ?? contract.GetCustomAttribute<ReactApiContractAttribute>()?.Name
            ?? (contract.Name.StartsWith("I", StringComparison.Ordinal) ? contract.Name[1..] : contract.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_apis.ContainsKey(name) || _apiHandlers.ContainsKey(name))
        {
            throw new ArgumentException($"An API named '{name}' is already registered on this WebView.", nameof(apiName));
        }

        var methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var method in contract.GetMethods())
        {
            var javascriptName = method.GetCustomAttribute<ReactApiMethodAttribute>()?.Name ?? GetJavaScriptMethodName(method);
            if (!methods.TryAdd(javascriptName, method))
            {
                throw new ArgumentException(
                    $"The API contract '{contract.Name}' contains two methods that map to '{javascriptName}'.",
                    nameof(implementation));
            }
        }

        _apis[name] = new ApiRegistration(implementation, methods);
    }

    /// <summary>
    /// Registers typed handlers generated for a TypeScript API contract. This overload avoids
    /// reflection-based method discovery and can be used by trimming/AOT-oriented applications.
    /// </summary>
    /// <param name="apiName">The JavaScript API name.</param>
    /// <param name="methods">Handlers keyed by the JavaScript method name.</param>
    public void RegisterApi(string apiName, IReadOnlyDictionary<string, ReactApiMethodHandler> methods)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentNullException.ThrowIfNull(methods);
        if (_apis.ContainsKey(apiName) || _apiHandlers.ContainsKey(apiName))
        {
            throw new ArgumentException($"An API named '{apiName}' is already registered on this WebView.", nameof(apiName));
        }

        _apiHandlers[apiName] = new Dictionary<string, ReactApiMethodHandler>(methods, StringComparer.Ordinal);
    }

    /// <summary>Removes a previously registered API.</summary>
    /// <param name="apiName">The JavaScript API name passed to <see cref="RegisterApi{TApi}"/>.</param>
    /// <returns><see langword="true"/> when an API was removed.</returns>
    public bool UnregisterApi(string apiName)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return _apis.Remove(apiName) | _apiHandlers.Remove(apiName);
    }

    /// <summary>
    /// Sends a JSON message to listeners registered with <c>subscribe</c> in the React client.
    /// Messages sent before the page finishes loading are queued and delivered on load.
    /// </summary>
    /// <param name="type">A message name, such as <c>account:changed</c>.</param>
    /// <param name="payload">Any value supported by <see cref="JsonSerializer"/>.</param>
    /// <returns>The result returned by the native WebView JavaScript execution.</returns>
    [RequiresUnreferencedCode("PostMessageAsync serializes the payload with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("PostMessageAsync JSON serialization may require runtime code generation.")]
    public Task<string?> PostMessageAsync<T>(string type, T payload)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var payloadJson = SerializeBridgeValue(payload);
        return PostJsonAsync(type, payloadJson);
    }

    /// <summary>
    /// Invokes a handler registered with <c>registerReactHandler</c> in the React client.
    /// </summary>
    /// <param name="handler">The handler name registered by React.</param>
    /// <param name="payload">Any JSON serializable payload.</param>
    /// <param name="timeout">Timeout override; defaults to <see cref="ReactInvokeTimeout"/>.</param>
    /// <param name="cancellationToken">Cancels waiting and asks React to abort the handler.</param>
    [RequiresUnreferencedCode("InvokeReactAsync serializes the payload with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("InvokeReactAsync JSON serialization may require runtime code generation.")]
    public Task InvokeReactAsync(
        string handler,
        object? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        InvokeReactAsync<object?>(handler, payload, timeout, cancellationToken);

    /// <summary>
    /// Invokes a handler registered with <c>registerReactHandler</c> and deserializes its result.
    /// </summary>
    [RequiresUnreferencedCode("InvokeReactAsync serializes the payload and deserializes the result with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("InvokeReactAsync JSON serialization may require runtime code generation.")]
    public async Task<T?> InvokeReactAsync<T>(
        string handler,
        object? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ReactInvokeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingReactInvocations.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Could not allocate a React invocation request id.");
        }

        var effectiveTimeout = timeout ?? ReactInvokeTimeout;
        var payloadJson = SerializeBridgeValue(payload);
        var body = "{\"requestId\":" + QuoteJson(requestId) +
            ",\"handler\":" + QuoteJson(handler) +
            ",\"payload\":" + payloadJson + "}";

        try
        {
            await PostJsonAsync(ReactBridgeMessages.ReactInvoke, body).ConfigureAwait(false);

            using var timeoutSource = new CancellationTokenSource(
                effectiveTimeout > TimeSpan.Zero ? effectiveTimeout : Timeout.InfiniteTimeSpan);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            var result = await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            if (result.Error is not null)
            {
                throw new InvalidOperationException(result.Error);
            }

            if (result.Result is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return default;
            }

            return element.Deserialize<T>(BridgeJsonSerializerOptions);
        }
        catch (OperationCanceledException)
        {
            SendReactInvokeCancel(requestId);
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The React handler '{handler}' did not respond within {effectiveTimeout.TotalSeconds:0.##} seconds.");
            }

            throw;
        }
        finally
        {
            _pendingReactInvocations.TryRemove(requestId, out _);
        }
    }

    private void SendReactInvokeCancel(string requestId)
    {
        try
        {
            _ = PostJsonAsync(
                ReactBridgeMessages.ReactInvokeCancel,
                "{\"requestId\":" + QuoteJson(requestId) + "}");
        }
        catch (Exception)
        {
            // The page may already be unloading; cancellation is best-effort.
        }
    }

    private void HandleReactResult(JsonElement payload)
    {
        if (!IsHostApiOriginTrusted(out var pageOrigin))
        {
            RaiseSecurityViolation(
                "React invocation results are not allowed from this page origin.",
                pageOrigin);
            return;
        }

        if (!payload.TryGetProperty("requestId", out var idElement) ||
            idElement.GetString() is not { Length: > 0 } requestId ||
            !_pendingReactInvocations.TryRemove(requestId, out var completion))
        {
            return;
        }

        string? error = null;
        if (payload.TryGetProperty("error", out var errorElement) &&
            errorElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            error = errorElement.ToString();
        }

        var result = payload.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : (JsonElement?)null;
        completion.TrySetResult(new ReactInvokeResult(result, error));
    }

    private void CancelPendingReactInvocations(string message)
    {
        foreach (var entry in _pendingReactInvocations)
        {
            if (_pendingReactInvocations.TryRemove(entry.Key, out var completion))
            {
                completion.TrySetException(new InvalidOperationException(message));
            }
        }
    }

    private Task<string?> PostJsonAsync(string type, string payloadJson)
    {
        var script = $"window.dispatchEvent(new CustomEvent('{ReactBridgeMessages.DomainEventName}', {{ detail: {{ type: {QuoteJson(type)}, payload: {payloadJson} }} }}));";
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PostScriptAsync(script);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                completion.TrySetResult(await PostScriptAsync(script));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_isShutdown)
        {
            return;
        }

        if (!_sourceConfigured)
        {
            _ = ApplySourceMode();
        }

        base.OnAttachedToVisualTree(e);
        ReleaseReparentingScope();
        if (_embeddedAssetsEnabled && TopLevel.GetTopLevel(this) is Window window)
        {
            AttachAssetServerWindow(window);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!_isShutdown)
        {
            if (PreserveWebViewOnDetach)
            {
                BeginReparentingScope();
                // Keep the current window subscription: if the control is not reattached, the old
                // window can still shut down the asset server.
            }
            else
            {
                DetachAssetServerWindow();
            }
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void BeginReparentingScope()
    {
        if (_reparentingScope is not null)
        {
            return;
        }

        try
        {
            _reparentingScope = BeginReparenting(yieldOnLayoutBeforeExiting: true);
        }
        catch (Exception)
        {
            // Reparenting is an optimization; fall back to the platform's normal detach behavior.
            return;
        }

        if (ReparentingTimeout > TimeSpan.Zero)
        {
            _reparentingTimer.Interval = ReparentingTimeout;
            _reparentingTimer.Start();
        }
    }

    private void ReleaseReparentingScope()
    {
        _reparentingTimer.Stop();
        var scope = _reparentingScope;
        _reparentingScope = null;
        scope?.Dispose();
    }

    private void HandleReparentingTimeout(object? sender, EventArgs e) => ReleaseReparentingScope();

    private Uri EnableEmbeddedAssets(Uri baseUri)
    {
        _embeddedAssetsEnabled = true;
        _sourceConfigured = true;
        return baseUri;
    }

    private Uri StartLoopbackAssetServer()
    {
        var server = new EmbeddedAssetServer(_embeddedAssetOptions ?? new EmbeddedAssetOptions());
        _assetTransport = server;
        Source = server.BaseUri;
        return server.BaseUri;
    }

    private void AttachAssetServerWindow(Window window)
    {
        if (ReferenceEquals(_assetServerWindow, window))
        {
            return;
        }

        DetachAssetServerWindow();
        _assetServerWindow = window;
        window.Closed += HandleAssetServerWindowClosed;
    }

    private void DetachAssetServerWindow()
    {
        if (_assetServerWindow is not null)
        {
            _assetServerWindow.Closed -= HandleAssetServerWindowClosed;
            _assetServerWindow = null;
        }
    }

    private void HandleAssetServerWindowClosed(object? sender, EventArgs e) => Shutdown();

    private Task<string?> PostScriptAsync(string script)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PostScriptCoreAsync(script);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                completion.TrySetResult(await PostScriptCoreAsync(script));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<string?> PostScriptCoreAsync(string script)
    {
        if (_loadState == ReactLoadState.Failed)
        {
            throw new InvalidOperationException(LoadError ?? "The React page failed to load.");
        }

        return _loadState == ReactLoadState.Loaded
            ? InvokeScript(script)
            : _messageQueue.Enqueue(script, MaxQueuedBridgeMessages);
    }

    private async Task FlushMessageQueueAsync()
    {
        await _messageQueue.FlushAsync(InvokeScript).ConfigureAwait(true);
    }

    private void HandleEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = EnableDevTools;
        if (EnableWindowsOffscreenComposition && args is WindowsWebView2EnvironmentRequestedEventArgs windows)
        {
            windows.ExperimentalOffscreen = true;
        }
    }

    private void HandleNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        var request = args.Request;
        if (request is null)
        {
            _bridgeReady = false;
            SetLoadState(ReactLoadState.Loading, null);
            return;
        }

        if (!ReactUriPolicy.IsAllowedNavigation(request))
        {
            // Never let javascript:, data:, file:, or other non-HTTP schemes replace the app page,
            // even when ExternalNavigationPolicy allows external HTTP navigation.
            RaiseSecurityViolation(
                "Blocked a navigation to a non-HTTP scheme.",
                ReactUriPolicy.GetOrigin(request));
            args.Cancel = true;
            return;
        }

        if (ExternalNavigationPolicy != ReactExternalNavigationPolicy.Allow && !IsInternalNavigation(request))
        {
            if (ExternalNavigationPolicy == ReactExternalNavigationPolicy.OpenInSystemBrowser)
            {
                TryOpenInSystemBrowser(request);
            }

            args.Cancel = true;
            return;
        }

        _bridgeReady = false;
        CancelPendingInvocations(dispose: false);
        CancelPendingReactInvocations("The page navigated before the React handler responded.");
        RememberNavigationOrigin(request);
        SetLoadState(ReactLoadState.Loading, null);
    }

    private void HandleNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            CancelPendingInvocations(dispose: false);
            CancelPendingReactInvocations("The page failed to load before the React handler responded.");
            SetLoadState(ReactLoadState.Failed, args.Request is null
                ? "The React page failed to load."
                : $"Navigation to '{args.Request}' failed.");
            return;
        }

        if (args.Request is not null)
        {
            RememberNavigationOrigin(args.Request);
        }

        SetLoadState(ReactLoadState.Loaded, null);
        _ = FlushMessageQueueAsync();
        PostCurrentTheme();
    }

    private void HandleNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args)
    {
        if (args.Handled)
        {
            return;
        }

        var request = args.Request;
        if (request is null)
        {
            args.Handled = true;
            return;
        }

        if (!ReactUriPolicy.IsHttp(request))
        {
            // about:blank and data: popups are not supported; leave the current page untouched.
            args.Handled = true;
            return;
        }

        if (IsInternalNavigation(request))
        {
            args.Handled = true;
            Source = request;
            return;
        }

        switch (ExternalNavigationPolicy)
        {
            case ReactExternalNavigationPolicy.OpenInSystemBrowser:
                TryOpenInSystemBrowser(request);
                args.Handled = true;
                break;

            case ReactExternalNavigationPolicy.Block:
                args.Handled = true;
                break;
        }
    }

    private bool IsInternalNavigation(Uri request)
    {
        if (ReactUriPolicy.IsAboutBlank(request))
        {
            _currentNavigationOrigin = null;
            return true;
        }

        if (!ReactUriPolicy.IsHttp(request))
        {
            return false;
        }

        if (_navigationOrigin is null)
        {
            return true;
        }

        if (ReactUriPolicy.IsSameOrigin(request, _navigationOrigin))
        {
            return true;
        }

        foreach (var allowedOrigin in _allowedOrigins)
        {
            if (allowedOrigin is not null && ReactUriPolicy.IsSameOrigin(request, allowedOrigin))
            {
                return true;
            }
        }

        return false;
    }

    private void RememberNavigationOrigin(Uri request)
    {
        if (!ReactUriPolicy.IsHttp(request))
        {
            return;
        }

        _currentNavigationOrigin = ReactUriPolicy.GetOrigin(request);
        _navigationOrigin ??= _currentNavigationOrigin;
    }

    private bool IsHostApiOriginTrusted(out Uri? pageOrigin)
    {
        var sourceOrigin = ReactUriPolicy.GetOrigin(Source);
        pageOrigin = sourceOrigin ?? _currentNavigationOrigin;
        if (sourceOrigin is null ||
            !ReactUriPolicy.IsTrustedApiOrigin(sourceOrigin, _navigationOrigin, _trustedApiOrigins))
        {
            return false;
        }

        if (_currentNavigationOrigin is not null &&
            !ReactUriPolicy.IsTrustedApiOrigin(_currentNavigationOrigin, _navigationOrigin, _trustedApiOrigins))
        {
            return false;
        }

        return true;
    }

    private void RaiseSecurityViolation(
        string reason,
        Uri? origin,
        string? apiName = null,
        string? methodName = null)
    {
        var handler = SecurityViolation;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ReactSecurityViolationEventArgs(reason, origin, apiName, methodName));
        }
        catch (Exception)
        {
            // A security telemetry subscriber must not break navigation or message handling.
        }
    }

    private static bool TryOpenInSystemBrowser(Uri uri)
    {
        if (!ReactUriPolicy.IsHttp(uri))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void SetLoadState(ReactLoadState state, string? error)
    {
        if (_loadState == state && string.Equals(LoadError, error, StringComparison.Ordinal))
        {
            return;
        }

        _loadState = state;
        LoadError = error;
        if (state != ReactLoadState.Loaded)
        {
            _bridgeReady = false;
        }

        if (state == ReactLoadState.Failed)
        {
            _messageQueue.FailAll(new InvalidOperationException(error ?? "The React page failed to load."));
        }

        LoadStateChanged?.Invoke(this, new ReactLoadStateChangedEventArgs(state, error));
    }

    private void HandleActualThemeVariantChanged(object? sender, EventArgs e) => PostCurrentTheme();

    private void PostCurrentTheme()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = PostCurrentThemeAsync();
            return;
        }

        Dispatcher.UIThread.Post(() => _ = PostCurrentThemeAsync());
    }

    private async Task PostCurrentThemeAsync()
    {
        try
        {
            var payload = "{\"theme\":" + QuoteJson(GetThemeName(ActualThemeVariant)) + "}";
            await PostJsonAsync(ReactBridgeMessages.Theme, payload);
        }
        catch (Exception)
        {
            // The page can still be navigating; the next navigation or theme change syncs it again.
        }
    }

    private static string GetThemeName(ThemeVariant variant)
    {
        if (variant == ThemeVariant.Dark)
        {
            return "dark";
        }

        if (variant == ThemeVariant.Light)
        {
            return "light";
        }

        if (variant.InheritVariant is { } inherited && inherited != variant)
        {
            return GetThemeName(inherited);
        }

        return GetSystemTheme();
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Only complex values fall back to JSON reflection; primitive and JsonElement payloads are written directly.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Only complex values fall back to JSON reflection; configure BridgeJsonSerializerOptions with a source-generated context for Native AOT.")]
    private string SerializeBridgeValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return ReactBridgeProtocol.QuoteJson(text);
            case bool boolean:
                return boolean ? "true" : "false";
            case double number when double.IsFinite(number):
                return number.ToString("R", CultureInfo.InvariantCulture);
            case float number when float.IsFinite(number):
                return number.ToString("R", CultureInfo.InvariantCulture);
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case long number:
                return number.ToString(CultureInfo.InvariantCulture);
            case short number:
                return number.ToString(CultureInfo.InvariantCulture);
            case byte number:
                return number.ToString(CultureInfo.InvariantCulture);
            case uint number:
                return number.ToString(CultureInfo.InvariantCulture);
            case ulong number:
                return number.ToString(CultureInfo.InvariantCulture);
            case ushort number:
                return number.ToString(CultureInfo.InvariantCulture);
            case sbyte number:
                return number.ToString(CultureInfo.InvariantCulture);
            case decimal number:
                return number.ToString(CultureInfo.InvariantCulture);
            case DateTimeOffset timestamp:
                return ReactBridgeProtocol.QuoteJson(timestamp.ToString("O", CultureInfo.InvariantCulture));
            case DateTime timestamp:
                return ReactBridgeProtocol.QuoteJson(timestamp.ToString("O", CultureInfo.InvariantCulture));
            case Guid guid:
                return ReactBridgeProtocol.QuoteJson(guid.ToString("D"));
            case JsonElement element:
                return element.GetRawText();
            case byte[] bytes:
                return ReactBridgeProtocol.QuoteJson(Convert.ToBase64String(bytes));
            default:
                return JsonSerializer.Serialize(value, value.GetType(), BridgeJsonSerializerOptions);
        }
    }

    private static string GetSystemTheme() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            ? "dark"
            : "light";

    private static string QuoteJson(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private void HandleWebMessageReceived(object? sender, WebMessageReceivedEventArgs args)
    {
        if (_isShutdown)
        {
            return;
        }

        if (MaxIncomingMessageCharacters > 0 &&
            args.Body is { Length: > 0 } body &&
            body.Length > MaxIncomingMessageCharacters)
        {
            RaiseSecurityViolation(
                $"Incoming bridge message exceeded {MaxIncomingMessageCharacters} characters.",
                ReactUriPolicy.GetOrigin(Source));
            return;
        }

        if (!ReactBridgeProtocol.TryParse(args.Body, out var messageType, out var payload))
        {
            return;
        }

        if (messageType == ReactBridgeMessages.Ready)
        {
            MarkBridgeReady(payload);
            return;
        }

        if (messageType == ReactBridgeMessages.ThemeRequest)
        {
            PostCurrentTheme();
            return;
        }

        if (messageType == ReactBridgeMessages.Invoke)
        {
            Dispatcher.UIThread.Post(() => _ = HandleApiInvocationAsync(payload));
            return;
        }

        if (messageType == ReactBridgeMessages.InvokeCancel)
        {
            Dispatcher.UIThread.Post(() => CancelApiInvocation(payload));
            return;
        }

        if (messageType == ReactBridgeMessages.ReactInvokeResult)
        {
            Dispatcher.UIThread.Post(() => HandleReactResult(payload));
            return;
        }

        if (messageType == ReactBridgeMessages.Error)
        {
            var error = ReactJavaScriptErrorParser.Parse(payload);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    JavaScriptError?.Invoke(this, error);
                }
                catch (Exception exception)
                {
                    BridgeError?.Invoke(this, new ReactBridgeErrorEventArgs(exception));
                }
            });
            return;
        }

        var message = new ReactMessageReceivedEventArgs(messageType, payload);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                MessageReceived?.Invoke(this, message);
            }
            catch (Exception exception)
            {
                BridgeError?.Invoke(this, new ReactBridgeErrorEventArgs(exception));
            }
        });
    }

    private void MarkBridgeReady(JsonElement payload)
    {
        if (_bridgeReady)
        {
            return;
        }

        _bridgeReady = true;
        var args = new ReactBridgeReadyEventArgs(payload);
        if (Dispatcher.UIThread.CheckAccess())
        {
            BridgeReady?.Invoke(this, args);
        }
        else
        {
            Dispatcher.UIThread.Post(() => BridgeReady?.Invoke(this, args));
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Only the annotated reflection-based RegisterApi path reads Task.Result by reflection.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Reflection-based API results require preserved JSON metadata; use generated registration or a source-generated context.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Reflection-based API results require a source-generated JSON context for Native AOT; generated registration avoids this path.")]
    private async Task HandleApiInvocationAsync(JsonElement payload)
    {
        var requestId = payload.TryGetProperty("requestId", out var idElement) ? idElement.GetString() : null;
        CancellationTokenSource? cancellation = null;
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            cancellation = new CancellationTokenSource();
            lock (_pendingInvocations)
            {
                if (_pendingInvocations.TryGetValue(requestId!, out var previous))
                {
                    previous.Cancel();
                    previous.Dispose();
                }

                _pendingInvocations[requestId!] = cancellation;
            }
        }

        object? result = null;
        string? error = null;

        try
        {
            var apiName = payload.GetProperty("api").GetString() ?? throw new InvalidOperationException("API name is missing.");
            var methodName = payload.GetProperty("method").GetString() ?? throw new InvalidOperationException("Method name is missing.");
            if (!IsHostApiOriginTrusted(out var pageOrigin))
            {
                const string reason = "Host API calls are not allowed from this page origin.";
                RaiseSecurityViolation(reason, pageOrigin, apiName, methodName);
                throw new InvalidOperationException(reason);
            }

            var jsonArguments = payload.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array
                ? argsElement.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();

            if (_apiHandlers.TryGetValue(apiName, out var handlers))
            {
                if (!handlers.TryGetValue(methodName, out var handler))
                {
                    throw new InvalidOperationException($"The API method '{apiName}.{methodName}' is not registered.");
                }

                var context = new ReactApiInvocationContext(
                    jsonArguments,
                    cancellation?.Token ?? CancellationToken.None,
                    BridgeJsonSerializerOptions);
                var task = handler(context) ?? throw new InvalidOperationException($"The host API handler '{apiName}.{methodName}' returned null.");
                await AwaitInvocationAsync(task, cancellation).ConfigureAwait(false);
                result = task.GetAwaiter().GetResult();
            }
            else
            {
                if (!_apis.TryGetValue(apiName, out var registration) || !registration.Methods.TryGetValue(methodName, out var method))
                {
                    throw new InvalidOperationException($"The API method '{apiName}.{methodName}' is not registered.");
                }

                var arguments = ReactApiMethodBinder.Bind(
                    method.GetParameters(),
                    jsonArguments,
                    apiName,
                    methodName,
                    cancellation?.Token ?? CancellationToken.None,
                    BridgeJsonSerializerOptions);
                var implementationMethod = FindImplementationMethod(registration.Implementation.GetType(), method);
                var invocation = implementationMethod.Invoke(registration.Implementation, arguments);
                if (invocation is Task task)
                {
                    await AwaitInvocationAsync(task, cancellation).ConfigureAwait(false);
                    result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
                }
                else
                {
                    result = invocation;
                }
            }
        }
        catch (Exception exception)
        {
            error = exception is TargetInvocationException { InnerException: not null } targetInvocation
                ? targetInvocation.InnerException!.Message
                : exception.Message;
        }
        finally
        {
            if (cancellation is not null)
            {
                lock (_pendingInvocations)
                {
                    _pendingInvocations.Remove(requestId!);
                }

                cancellation.Dispose();
            }
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            try
            {
                var resultJson = SerializeBridgeValue(result);
                var errorJson = error is null ? "null" : ReactBridgeProtocol.QuoteJson(error);
                var resultPayload = "{\"requestId\":" + ReactBridgeProtocol.QuoteJson(requestId!) +
                    ",\"result\":" + resultJson +
                    ",\"error\":" + errorJson + "}";
                await PostJsonAsync(ReactBridgeMessages.InvokeResult, resultPayload);
            }
            catch (Exception)
            {
                // The page navigated away or failed; the JavaScript caller will time out or unload.
            }
        }
    }

    private void CancelApiInvocation(JsonElement payload)
    {
        var requestId = payload.TryGetProperty("requestId", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(requestId))
        {
            return;
        }

        lock (_pendingInvocations)
        {
            if (_pendingInvocations.TryGetValue(requestId, out var cancellation))
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (Exception)
                {
                    // The invocation completed while the cancel message was in flight, or a
                    // cancellation callback threw; cancellation is best-effort.
                }
            }
        }
    }

    private void CancelPendingInvocations(bool dispose)
    {
        CancellationTokenSource[] pending;
        lock (_pendingInvocations)
        {
            if (_pendingInvocations.Count == 0)
            {
                return;
            }

            pending = [.. _pendingInvocations.Values];
            if (dispose)
            {
                _pendingInvocations.Clear();
            }
        }

        foreach (var cancellation in pending)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (Exception)
            {
            }

            if (dispose)
            {
                cancellation.Dispose();
            }
        }
    }

    private async Task AwaitInvocationAsync(Task task, CancellationTokenSource? cancellation)
    {
        if (ApiInvokeTimeout <= TimeSpan.Zero)
        {
            await task.ConfigureAwait(false);
            return;
        }

        using var timeoutCancellation = new CancellationTokenSource();
        var timeout = Task.Delay(ApiInvokeTimeout, timeoutCancellation.Token);
        var completed = await Task.WhenAny(task, timeout).ConfigureAwait(false);
        if (ReferenceEquals(completed, task))
        {
            await timeoutCancellation.CancelAsync();
            await task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (cancellation is not null)
            {
                await cancellation.CancelAsync();
            }
        }
        catch (Exception)
        {
        }

        // Observe a fault that arrives after the timeout so it does not surface as an unobserved exception.
        _ = task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException($"The host API call did not complete within {ApiInvokeTimeout.TotalSeconds:0.##} seconds.");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Only the annotated reflection-based RegisterApi path resolves implementation methods.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Only the annotated reflection-based RegisterApi path uses interface maps.")]
    private static MethodInfo FindImplementationMethod(Type implementationType, MethodInfo interfaceMethod)
    {
        var interfaceType = interfaceMethod.DeclaringType;
        if (interfaceType is not null &&
            interfaceType.IsInterface &&
            implementationType.GetInterfaces().Contains(interfaceType))
        {
            var map = implementationType.GetInterfaceMap(interfaceType);
            for (var index = 0; index < map.InterfaceMethods.Length; index++)
            {
                if (map.InterfaceMethods[index] == interfaceMethod)
                {
                    return map.TargetMethods[index];
                }
            }
        }

        return implementationType.GetMethod(interfaceMethod.Name)
            ?? throw new InvalidOperationException($"The implementation of '{interfaceType?.Name}.{interfaceMethod.Name}' is missing.");
    }

    private void ThrowIfShutdown() => ObjectDisposedException.ThrowIf(_isShutdown, this);

    private static string GetJavaScriptMethodName(MethodInfo method)
    {
        var name = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static bool RegisterDevServerLifetime()
    {
        var pidFile = Path.Combine(AppContext.BaseDirectory, "avalonia-react-vite.pid");
        if (!File.Exists(pidFile)) return false;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                var parts = File.ReadAllText(pidFile).Split('|');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
                    !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var startTicks)) return;

                using var process = Process.GetProcessById(processId);
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTicks) return;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                // The development server may already have exited; shutdown should not prevent the Avalonia app from closing.
            }
            finally
            {
                try { File.Delete(pidFile); }
                catch (IOException) { }
            }
        };
        return true;
    }

    private sealed record ApiRegistration(object Implementation, IReadOnlyDictionary<string, MethodInfo> Methods);

    private readonly record struct ReactInvokeResult(JsonElement? Result, string? Error);

}

/// <summary>
/// A decoded message received from the React frontend.
/// </summary>
public sealed class ReactMessageReceivedEventArgs(string type, JsonElement payload) : EventArgs
{
    /// <summary>Gets the message name.</summary>
    public string Type { get; } = type;

    /// <summary>Gets the message payload as a JSON value.</summary>
    public JsonElement Payload { get; } = payload;

    /// <summary>Deserializes the message payload to a .NET type.</summary>
    [RequiresUnreferencedCode("GetPayload uses reflection-based System.Text.Json deserialization.")]
    [RequiresDynamicCode("GetPayload JSON deserialization may require runtime code generation.")]
    public T? GetPayload<T>(JsonSerializerOptions? options = null) =>
        Payload.Deserialize<T>(options ?? JsonSerializerOptions.Web);
}
