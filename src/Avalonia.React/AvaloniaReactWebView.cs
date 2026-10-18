using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.React.EmbeddedAssets;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avalonia.React;

/// <summary>
/// Hosts a React application in Avalonia's official native WebView and exposes a JSON message bridge.
/// </summary>
public sealed class AvaloniaReactWebView : NativeWebView
{
    private static readonly Lazy<bool> DevServerLifetime = new(RegisterDevServerLifetime);
    private readonly Dictionary<string, ApiRegistration> _apis = new(StringComparer.Ordinal);
    private readonly ReactMessageQueue _messageQueue = new();
    private readonly List<Uri> _allowedOrigins = new();
    private IAssetTransport? _assetTransport;
#if WINDOWS
    private NativeAssetHost? _nativeAssetHost;
#endif
    private EmbeddedAssetOptions? _embeddedAssetOptions;
    private bool _embeddedAssetsEnabled;
    private Window? _assetServerWindow;
    private Uri? _navigationOrigin;
    private bool _sourceConfigured;
    private bool _bridgeReady;
    private ReactLoadState _loadState;

    /// <summary>
    /// Creates a React WebView and listens for messages posted by the React client package.
    /// </summary>
    public AvaloniaReactWebView()
    {
        _ = DevServerLifetime.Value;
        EnableDevTools = ReactDefaults.EnableDevTools;
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

    /// <summary>Gets the current page lifecycle state.</summary>
    public ReactLoadState LoadState => _loadState;

    /// <summary>Gets the failure message when <see cref="LoadState"/> is <see cref="ReactLoadState.Failed"/>.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Gets whether the page navigated successfully and can receive host messages.</summary>
    public bool IsPageLoaded => _loadState == ReactLoadState.Loaded;

    /// <summary>Gets whether the React client has sent the <c>avalonia:ready</c> handshake for the current page.</summary>
    public bool IsBridgeReady => _bridgeReady;

    /// <summary>
    /// Gets or sets how the initial source is selected. <see cref="ReactSourceMode.Auto"/> resolves to the
    /// Vite server for Debug builds and embedded assets otherwise, based on build-time metadata emitted by
    /// the Avalonia.React MSBuild targets. Defaults to <see cref="ReactSourceMode.Manual"/> so existing
    /// applications that assign <see cref="NativeWebView.Source"/> are unaffected.
    /// </summary>
    public ReactSourceMode SourceMode { get; set; } = ReactSourceMode.Manual;

    /// <summary>Gets or sets the Vite server URL used by <see cref="ReactSourceMode.DevServer"/>.</summary>
    public Uri? DevServerUrl { get; set; }

    /// <summary>Gets or sets the published frontend directory. Defaults to <c>wwwroot</c> next to the executable.</summary>
    public string? EmbeddedAssetsRoot { get; set; }

    /// <summary>Gets or sets the transport used for embedded assets.</summary>
    public EmbeddedAssetTransport EmbeddedAssetsTransport { get; set; } = EmbeddedAssetTransport.Loopback;

    /// <summary>
    /// Gets or sets what happens when a navigation leaves the application origin. Defaults to
    /// <see cref="ReactExternalNavigationPolicy.OpenInSystemBrowser"/>.
    /// </summary>
    public ReactExternalNavigationPolicy ExternalNavigationPolicy { get; set; } = ReactExternalNavigationPolicy.OpenInSystemBrowser;

    /// <summary>Gets or sets whether opening the WebView dev tools is requested. Defaults from build metadata (Debug on).</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>
    /// Gets or sets whether WebView2 offscreen composition is requested on Windows. Enable this when Avalonia
    /// content must render above the WebView, for example a loading overlay. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableWindowsOffscreenComposition { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of host-to-React messages held while the page is loading.
    /// Defaults to 1024.
    /// </summary>
    public int MaxQueuedBridgeMessages { get; set; } = 1024;

    /// <summary>
    /// Gets or sets how long a host API implementation may run before the caller receives a timeout
    /// error. Defaults to 30 seconds; set to <see cref="TimeSpan.Zero"/> to disable the timeout.
    /// </summary>
    public TimeSpan ApiInvokeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the additional origins allowed to navigate inside the WebView when
    /// <see cref="ExternalNavigationPolicy"/> is not <see cref="ReactExternalNavigationPolicy.Allow"/>.
    /// </summary>
    public IList<Uri> AllowedOrigins => _allowedOrigins;

    /// <summary>
    /// Starts serving a published frontend and navigates the WebView to it.
    /// </summary>
    /// <param name="configure">Configures the asset root, transport, SPA fallback, and caching.</param>
    /// <returns>The URI that the WebView navigates to.</returns>
    /// <remarks>
    /// The default loopback transport binds to <c>127.0.0.1</c> on an ephemeral port, supports SPA fallback,
    /// and stops when the host window closes. On Windows target frameworks,
    /// <see cref="EmbeddedAssetTransport.NativeHost"/> maps the folder to a WebView2 virtual host instead
    /// and starts no server; the virtual host serves static files only, so SPA route fallback requires the
    /// loopback transport. The cross-platform target keeps using loopback.
    /// Use this for Release builds after the frontend has been copied to <c>wwwroot</c>; point
    /// <see cref="NativeWebView.Source"/> at the Vite server during development instead.
    /// </remarks>
    public Uri UseEmbeddedAssets(Action<EmbeddedAssetOptions>? configure = null)
    {
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

            var transport = options.Transport;
#if WINDOWS
            if (transport == EmbeddedAssetTransport.Auto)
            {
                transport = OperatingSystem.IsWindows() ? EmbeddedAssetTransport.NativeHost : EmbeddedAssetTransport.Loopback;
            }

            if (transport == EmbeddedAssetTransport.NativeHost)
            {
                var native = new NativeAssetHost(options);
                _assetTransport = native;
                _nativeAssetHost = native;

                if (TryGetPlatformHandle() is null)
                {
                    // The adapter is created on attach; map the virtual host before navigating.
                    AdapterCreated += HandleNativeAssetAdapterCreated;
                    return EnableEmbeddedAssets(native.BaseUri);
                }

                if (ApplyNativeAssetHost(native))
                {
                    return EnableEmbeddedAssets(native.BaseUri);
                }

                return EnableEmbeddedAssets(StartLoopbackAssetServer());
            }
#else
            if (transport == EmbeddedAssetTransport.NativeHost)
            {
                throw new PlatformNotSupportedException(
                    "NativeHost asset embedding requires a Windows target framework such as net10.0-windows. Use Loopback or Auto on this target.");
            }
#endif

            return EnableEmbeddedAssets(StartLoopbackAssetServer());
        }
        catch
        {
            _embeddedAssetOptions = null;
            _assetTransport = null;
#if WINDOWS
            _nativeAssetHost = null;
            AdapterCreated -= HandleNativeAssetAdapterCreated;
#endif
            throw;
        }
    }

    /// <summary>
    /// Applies <see cref="SourceMode"/> immediately. This is called automatically when the control enters the
    /// visual tree; call it explicitly only when configuring the control after it is already attached.
    /// </summary>
    /// <returns><see langword="true"/> when a source was configured; otherwise <see langword="false"/>.</returns>
    public bool ApplySourceMode()
    {
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

                        options.Transport = EmbeddedAssetsTransport;
                    });
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
        if (Source is null && !_sourceConfigured)
        {
            _ = ApplySourceMode();
        }

        if (Source is null)
        {
            SetLoadState(ReactLoadState.Idle, null);
            return false;
        }

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
    /// Registers an implementation of a TypeScript-generated API contract so React can invoke its methods.
    /// </summary>
    /// <typeparam name="TApi">The generated C# interface for an exported TypeScript API.</typeparam>
    /// <param name="implementation">The host-side implementation.</param>
    /// <param name="apiName">The JavaScript API name. Defaults to the generated interface name without its leading I.</param>
    public void RegisterApi<TApi>(TApi implementation, string? apiName = null) where TApi : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        var contract = typeof(TApi);
        if (!contract.IsInterface)
        {
            throw new ArgumentException("TApi must be an interface generated from a TypeScript API contract.", nameof(TApi));
        }

        var name = apiName
            ?? contract.GetCustomAttribute<ReactApiContractAttribute>()?.Name
            ?? (contract.Name.StartsWith("I", StringComparison.Ordinal) ? contract.Name.Substring(1) : contract.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_apis.ContainsKey(name))
        {
            throw new ArgumentException($"An API named '{name}' is already registered on this WebView.", nameof(apiName));
        }

        var methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var method in contract.GetMethods())
        {
            var javascriptName = method.GetCustomAttribute<ReactApiMethodAttribute>()?.Name ?? GetJavaScriptMethodName(method);
            if (methods.ContainsKey(javascriptName))
            {
                throw new ArgumentException(
                    $"The API contract '{contract.Name}' contains two methods that map to '{javascriptName}'.",
                    nameof(implementation));
            }

            methods[javascriptName] = method;
        }

        _apis[name] = new ApiRegistration(implementation, methods);
    }

    /// <summary>Removes a previously registered API.</summary>
    /// <param name="apiName">The JavaScript API name passed to <see cref="RegisterApi{TApi}"/>.</param>
    /// <returns><see langword="true"/> when an API was removed.</returns>
    public bool UnregisterApi(string apiName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return _apis.Remove(apiName);
    }

    /// <summary>
    /// Sends a JSON message to listeners registered with <c>subscribe</c> in the React client.
    /// Messages sent before the page finishes loading are queued and delivered on load.
    /// </summary>
    /// <param name="type">A message name, such as <c>account:changed</c>.</param>
    /// <param name="payload">Any value supported by <see cref="JsonSerializer"/>.</param>
    /// <returns>The result returned by the native WebView JavaScript execution.</returns>
    public Task<string?> PostMessageAsync<T>(string type, T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var message = JsonSerializer.Serialize(new ReactMessage<T>(type, payload));
        var script = $"window.dispatchEvent(new CustomEvent('{ReactBridgeMessages.DomainEventName}', {{ detail: {message} }}));";
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PostScriptAsync(script);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
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
        if (!_sourceConfigured)
        {
            _ = ApplySourceMode();
        }

        base.OnAttachedToVisualTree(e);
        if (_embeddedAssetsEnabled && TopLevel.GetTopLevel(this) is Window window)
        {
            AttachAssetServerWindow(window);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachAssetServerWindow();
        base.OnDetachedFromVisualTree(e);
    }

    private Uri EnableEmbeddedAssets(Uri baseUri)
    {
        _embeddedAssetsEnabled = true;
        _sourceConfigured = true;
        return baseUri;
    }

#if WINDOWS
    private bool ApplyNativeAssetHost(NativeAssetHost native)
    {
        if (!native.TryApply(TryGetPlatformHandle()))
        {
            return false;
        }

        _nativeAssetHost = null;
        Source = native.BaseUri;
        return true;
    }
#endif

    private Uri StartLoopbackAssetServer()
    {
#if WINDOWS
        _nativeAssetHost = null;
        AdapterCreated -= HandleNativeAssetAdapterCreated;

        if (_assetTransport is NativeAssetHost native)
        {
            _ = native.DisposeAsync().AsTask();
        }
#endif

        var server = new EmbeddedAssetServer(_embeddedAssetOptions ?? new EmbeddedAssetOptions());
        _assetTransport = server;
        Source = server.BaseUri;
        return server.BaseUri;
    }

#if WINDOWS
    private void HandleNativeAssetAdapterCreated(object? sender, WebViewAdapterEventArgs args)
    {
        AdapterCreated -= HandleNativeAssetAdapterCreated;
        var native = _nativeAssetHost;
        if (native is not null && ApplyNativeAssetHost(native))
        {
            return;
        }

        StartLoopbackAssetServer();
    }
#endif

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

    private void HandleAssetServerWindowClosed(object? sender, EventArgs e)
    {
        DetachAssetServerWindow();
#if WINDOWS
        AdapterCreated -= HandleNativeAssetAdapterCreated;
        _nativeAssetHost = null;
#endif

        _messageQueue.FailAll(new InvalidOperationException("The host window closed before the message could be delivered."));
        var transport = _assetTransport;
        _assetTransport = null;
        if (transport is not null)
        {
            _ = transport.DisposeAsync().AsTask();
        }
    }

    private Task<string?> PostScriptAsync(string script)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PostScriptCoreAsync(script);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
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
        RememberNavigationOrigin(request);
        SetLoadState(ReactLoadState.Loading, null);
    }

    private void HandleNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
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
        if (!ReactUriPolicy.IsHttp(request))
        {
            return true;
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
        if (_navigationOrigin is null && ReactUriPolicy.IsHttp(request))
        {
            _navigationOrigin = ReactUriPolicy.GetOrigin(request);
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
            await PostMessageAsync(ReactBridgeMessages.Theme, new { theme = GetThemeName(ActualThemeVariant) });
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

    private static string GetSystemTheme() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            ? "dark"
            : "light";

    private void HandleWebMessageReceived(object? sender, WebMessageReceivedEventArgs args)
    {
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

    private async Task HandleApiInvocationAsync(JsonElement payload)
    {
        var requestId = payload.TryGetProperty("requestId", out var idElement) ? idElement.GetString() : null;
        object? result = null;
        string? error = null;

        try
        {
            var apiName = payload.GetProperty("api").GetString() ?? throw new InvalidOperationException("API name is missing.");
            var methodName = payload.GetProperty("method").GetString() ?? throw new InvalidOperationException("Method name is missing.");
            if (!_apis.TryGetValue(apiName, out var registration) || !registration.Methods.TryGetValue(methodName, out var method))
            {
                throw new InvalidOperationException($"The API method '{apiName}.{methodName}' is not registered.");
            }

            var jsonArguments = payload.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array
                ? argsElement.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
            var arguments = ReactApiMethodBinder.Bind(method.GetParameters(), jsonArguments, apiName, methodName);
            var implementationMethod = FindImplementationMethod(registration.Implementation.GetType(), method);
            var invocation = implementationMethod.Invoke(registration.Implementation, arguments);
            if (invocation is Task task)
            {
                await AwaitInvocationAsync(task).ConfigureAwait(false);
                result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
            }
            else
            {
                result = invocation;
            }
        }
        catch (Exception exception)
        {
            error = exception is TargetInvocationException { InnerException: not null } targetInvocation
                ? targetInvocation.InnerException!.Message
                : exception.Message;
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            try
            {
                await PostMessageAsync(ReactBridgeMessages.InvokeResult, new { requestId, result, error });
            }
            catch (Exception)
            {
                // The page navigated away or failed; the JavaScript caller will time out or unload.
            }
        }
    }

    private async Task AwaitInvocationAsync(Task task)
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
            timeoutCancellation.Cancel();
            await task.ConfigureAwait(false);
            return;
        }

        // Observe a fault that arrives after the timeout so it does not surface as an unobserved exception.
        _ = task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException($"The host API call did not complete within {ApiInvokeTimeout.TotalSeconds:0.##} seconds.");
    }

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

    private static string GetJavaScriptMethodName(MethodInfo method)
    {
        var name = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name.Substring(0, method.Name.Length - 5) : method.Name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
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

    private sealed record ReactMessage<T>(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("payload")] T Payload);
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
    public T? GetPayload<T>(JsonSerializerOptions? options = null) =>
        Payload.Deserialize<T>(options ?? JsonSerializerOptions.Web);
}
