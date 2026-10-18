using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
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
    private IAssetTransport? _assetTransport;
    private NativeAssetHost? _nativeAssetHost;
    private EmbeddedAssetOptions? _embeddedAssetOptions;
    private bool _embeddedAssetsEnabled;
    private Window? _assetServerWindow;

    /// <summary>
    /// Creates a React WebView and listens for messages posted by the React client package.
    /// </summary>
    public AvaloniaReactWebView()
    {
        _ = DevServerLifetime.Value;
        WebMessageReceived += HandleWebMessageReceived;
        ActualThemeVariantChanged += HandleActualThemeVariantChanged;
        NavigationCompleted += HandleNavigationCompleted;
    }

    /// <summary>
    /// Raised when JavaScript sends a message using <c>postMessage</c> from <c>@avalonia-react/bridge</c>.
    /// </summary>
    public event EventHandler<ReactMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Starts serving a published frontend and navigates the WebView to it.
    /// </summary>
    /// <param name="configure">Configures the asset root, transport, SPA fallback, and caching.</param>
    /// <returns>The URI that the WebView navigates to.</returns>
    /// <remarks>
    /// The default loopback transport binds to <c>127.0.0.1</c> on an ephemeral port, supports SPA fallback,
    /// and stops when the host window closes. On Windows, <see cref="EmbeddedAssetTransport.NativeHost"/>
    /// maps the folder to a WebView2 virtual host instead and starts no server; the virtual host serves
    /// static files only, so SPA route fallback requires the loopback transport.
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
        _embeddedAssetsEnabled = true;

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            AttachAssetServerWindow(window);
        }

        var transport = options.Transport == EmbeddedAssetTransport.Auto
            ? OperatingSystem.IsWindows() ? EmbeddedAssetTransport.NativeHost : EmbeddedAssetTransport.Loopback
            : options.Transport;

        if (transport == EmbeddedAssetTransport.NativeHost)
        {
            var native = new NativeAssetHost(options);
            _assetTransport = native;
            _nativeAssetHost = native;

            if (TryGetPlatformHandle() is null)
            {
                // The adapter is created on attach; map the virtual host before navigating.
                AdapterCreated += HandleNativeAssetAdapterCreated;
                return native.BaseUri;
            }

            return ApplyNativeAssetHost(native) ? native.BaseUri : StartLoopbackAssetServer();
        }

        return StartLoopbackAssetServer();
    }

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

    private Uri StartLoopbackAssetServer()
    {
        _nativeAssetHost = null;
        AdapterCreated -= HandleNativeAssetAdapterCreated;

        if (_assetTransport is NativeAssetHost native)
        {
            _ = native.DisposeAsync().AsTask();
        }

        var server = new EmbeddedAssetServer(_embeddedAssetOptions ?? new EmbeddedAssetOptions());
        _assetTransport = server;
        Source = server.BaseUri;
        return server.BaseUri;
    }

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

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
        AdapterCreated -= HandleNativeAssetAdapterCreated;
        _nativeAssetHost = null;

        var transport = _assetTransport;
        _assetTransport = null;
        if (transport is not null)
        {
            _ = transport.DisposeAsync().AsTask();
        }
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

        var name = apiName ?? (contract.Name.StartsWith("I", StringComparison.Ordinal) ? contract.Name.Substring(1) : contract.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var methods = contract.GetMethods().ToDictionary(GetJavaScriptMethodName, StringComparer.Ordinal);
        _apis[name] = new ApiRegistration(implementation, methods);
    }

    /// <summary>
    /// Sends a JSON message to listeners registered with <c>subscribe</c> in the React client.
    /// </summary>
    /// <param name="type">A message name, such as <c>account:changed</c>.</param>
    /// <param name="payload">Any value supported by <see cref="JsonSerializer"/>.</param>
    /// <returns>The result returned by the native WebView JavaScript execution.</returns>
    public Task<string?> PostMessageAsync<T>(string type, T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var message = JsonSerializer.Serialize(new ReactMessage<T>(type, payload));
        var script = $"window.dispatchEvent(new CustomEvent('avalonia-react:message', {{ detail: {message} }}));";
        if (Dispatcher.UIThread.CheckAccess())
        {
            return InvokeScript(script);
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await InvokeScript(script));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private void HandleActualThemeVariantChanged(object? sender, EventArgs e) => PostCurrentTheme();

    private void HandleNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            PostCurrentTheme();
        }
    }

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
            await PostMessageAsync("theme", new { theme = GetThemeName(ActualThemeVariant) });
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
    {        if (string.IsNullOrWhiteSpace(args.Body))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(args.Body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : JsonSerializer.SerializeToElement<object?>(null);

            var messageType = typeElement.GetString()!;
            if (messageType == "theme:request")
            {
                PostCurrentTheme();
                return;
            }

            if (messageType == "avalonia:invoke")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _ = HandleApiInvocationAsync(payload);
                });
                return;
            }

            var message = new ReactMessageReceivedEventArgs(messageType, payload);
            Dispatcher.UIThread.Post(() => MessageReceived?.Invoke(this, message));
        }
        catch (JsonException)
        {
            // Ignore invalid bridge payloads; application messages remain isolated from the WebView event loop.
        }
        catch (Exception)
        {
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
            var parameters = method.GetParameters();
            if (parameters.Length != jsonArguments.Length)
            {
                throw new InvalidOperationException($"'{apiName}.{methodName}' expects {parameters.Length} arguments, but received {jsonArguments.Length}.");
            }

            var arguments = new object?[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                arguments[index] = jsonArguments[index].Deserialize(parameters[index].ParameterType, JsonSerializerOptions.Web);
            }

            var implementationMethod = registration.Implementation.GetType().GetMethod(method.Name)
                ?? throw new InvalidOperationException($"The implementation of '{apiName}.{methodName}' is missing.");
            var invocation = implementationMethod.Invoke(registration.Implementation, arguments);
            if (invocation is Task task)
            {
                await task.ConfigureAwait(false);
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
            await PostMessageAsync("avalonia:invoke:result", new { requestId, result, error });
        }
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
