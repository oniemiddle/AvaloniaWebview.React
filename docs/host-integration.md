# Host integration

Source selection, the built-in host view, navigation and security, multi-window sessions, embedded assets, resource cleanup, and platform requirements.

## Host a React development server

Add the control to an Avalonia view. Point it to the Vite development server while developing, or let `SourceMode="Auto"` select the source. The included host uses Vite in Debug and embedded assets in Release; set `Source` or `SourceMode` to override both.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:react="using:AvaloniaWebview.React">
  <react:AvaloniaReactWebView Source="http://localhost:5173/" />
</Window>
```

Start the frontend with `npm run dev -- --host 0.0.0.0`. Keep the Vite port in sync with the `Source` URI.

### Configure the source in XAML

`SourceMode="Auto"` removes the usual Debug/Release branching from the application:

```xml
<react:AvaloniaReactWebView SourceMode="Auto" />
```

The AvaloniaWebview.React MSBuild targets emit assembly metadata into the host executable: Debug builds resolve `Auto` to the Vite server, other configurations resolve to embedded assets (or `AssemblyEmbeddedAssets` when `AvaloniaReactEmbedAssetsInAssembly` is enabled). Override the defaults with `AvaloniaReactDefaultSourceMode`, `AvaloniaReactDevServerUrl`, or `AvaloniaReactEnableDevTools`, or set `SourceMode` to `Manual`, `DevServer`, `EmbeddedAssets`, or `AssemblyEmbeddedAssets`. Set `AvaloniaReactEmitAssemblyMetadata=false` to opt out of the generated metadata. `Source` is only assigned when the control enters the visual tree, so the source can be configured from XAML or a constructor.

### Use the built-in host view

`ReactHostView` wraps `AvaloniaReactWebView` with a themed loading/error overlay and forwards the common configuration, events, and methods. The overlay hides after navigation, or after the React client's `signalReady()` when `WaitForBridgeReady` is enabled, with a fallback timeout:

```xml
<react:ReactHostView x:Name="ReactHost"
                     SourceMode="Auto"
                     WaitForBridgeReady="True"
                     LoadingText="正在启动 Avalonia + React…"
                     RetryText="重试加载"
                     ErrorTextFormat="页面加载失败：{0}" />
```

`ReactHostView` exposes `RegisterApi`, `PostMessageAsync`, `Reload`, `SourceMode`, the load/bridge events, and the underlying `WebView` for advanced use. Set `WaitForBridgeReady="False"` (the default) to hide the overlay as soon as navigation completes. `BridgeReadyTimeout` (10 seconds by default) prevents a missing handshake from keeping the overlay visible forever. `ShowOverlay="False"` disables the built-in overlay entirely. Call `Shutdown()` (or `Dispose()`) when discarding a host view; shutdown is idempotent and releases the embedded asset transport.

### Load state and recovery

The control tracks the page lifecycle and waits for the React ready handshake:

```csharp
webView.LoadStateChanged += (_, args) =>
{
    // Idle, Loading, Loaded, Failed
    if (args.State == ReactLoadState.Failed) Console.WriteLine(args.Error);
};

webView.BridgeReady += (_, _) => overlay.IsVisible = false;

// Reload the current source and reset the state.
webView.Reload();
```

`PostMessageAsync` calls made before the page finishes loading are queued (up to `MaxQueuedBridgeMessages`, 1024 by default) and flushed when navigation completes. If navigation fails, queued callers receive an `InvalidOperationException` instead of hanging. The React client sends the reserved `avalonia:ready` message through `signalReady()` from `@avalonia-react/bridge`; the host raises `BridgeReady` once per page.

### Navigation, diagnostics, and security

- `ExternalNavigationPolicy` defaults to `OpenInSystemBrowser`: navigations that leave the initial origin are cancelled and opened in the system browser. Use `Block` to ignore them or `Allow` to navigate the WebView; add trusted origins to `AllowedOrigins`.
- `NewWindowRequested` is handled by default: same-origin requests navigate the current view, external requests follow `ExternalNavigationPolicy`.
- `EnableDevTools` requests the platform dev tools (defaults to true in Debug through build metadata). `ReactHostView` automatically enables WebView2 offscreen composition on Windows so its overlay renders above the WebView; direct `AvaloniaReactWebView` users can set `EnableWindowsOffscreenComposition` when they layer Avalonia content above the WebView.
- `BridgeError` reports exceptions thrown by application `MessageReceived` handlers instead of letting them escape the WebView event loop.
- `JavaScriptError` receives window errors and unhandled promise rejections forwarded by the bridge client, including message, stack, source, line, and column.
- `ApiInvokeTimeout` (30 seconds by default) returns a timeout error to React when a host API implementation runs too long; set it to `TimeSpan.Zero` to disable.
- `RegisterApi` rejects duplicate API names; `UnregisterApi` removes one.
- Host APIs are origin-gated. The initial application origin is trusted automatically; add additional page origins to `TrustedApiOrigins`. `AllowedOrigins` only grants navigation, not host API access. Requests from other origins are rejected with a `SecurityViolation` event and an error returned to React.
- Main-frame navigation to non-HTTP schemes (`javascript:`, `data:`, `file:`, and similar) is always cancelled, even when `ExternalNavigationPolicy="Allow"`. Only HTTP(S) and `about:blank` are allowed.
- `MaxIncomingMessageCharacters` (4,000,000 by default) drops oversized bridge messages and reports them through `SecurityViolation`. Set it to `0` or a negative value to disable the limit.
- `ReactHostView.AdapterStartupTimeout` (10 seconds by default) shows a runtime-availability error if the native adapter never initializes. `AdapterDiagnostics`, `IsWebViewRuntimeInstalled`, and `WebViewUnavailableReason` expose the platform runtime state.

### Multi-window and reparenting

`ReactHostView` sets `PreserveWebViewOnDetach` by default. When the control is removed from a visual tree, it starts a native reparenting scope instead of destroying the WebView, so moving it to another window or tab keeps the page, history, cookies, and bridge state. Reattaching releases the scope and reparents the native control; if it is not reattached, `ReparentingTimeout` (10 seconds by default) releases the scope and destroys it.

```csharp
reactHost.ReparentingTimeout = TimeSpan.FromSeconds(30);

// Optional manual scope when the application controls the detach/attach sequence.
using (reactHost.BeginReparenting())
{
    // detach the control and attach it to another window
}
```

Raw `AvaloniaReactWebView` leaves `PreserveWebViewOnDetach` disabled and exposes the same `BeginReparenting`/`BeginReparentingAsync` methods. Each WebView still owns its own loopback asset server; the server's window subscription follows the control as it moves. Disposing or shutting down the host releases everything.

### Native WebView capabilities

`AvaloniaReactWebView` derives from `NativeWebView`, so the platform capabilities are available directly: `UserAgent`, `CanGoBack`/`CanGoForward`, `GoBack`/`GoForward`, `Stop`, `Navigate`, `ShowPrintUI`, `PrintToPdfStreamAsync`, `TryGetCookieManager`, `TryGetCommandManager`, and `AdapterInfo`. `ReactHostView` forwards the common ones, so applications using the overlay do not need to reach through `WebView`:

```csharp
reactHost.UserAgent = "MyApp/1.0";
if (reactHost.CanGoBack) reactHost.GoBack();
reactHost.ShowPrintUI();

await using var pdf = await reactHost.PrintToPdfStreamAsync();

var cookies = reactHost.TryGetCookieManager();
if (cookies is not null)
{
    var current = await cookies.GetCookiesAsync();
}
```

`GetAdapterDiagnostics()` returns the platform `DetailedWebViewAdapterInfo` when available, and `ReactHostView` exposes `AdapterInfo`, `AdapterDiagnostics`, `IsWebViewRuntimeInstalled`, and `WebViewUnavailableReason`. If the platform adapter is missing or unsupported, the host view shows the runtime reason in its error overlay instead of leaving a blank loading screen.

### Serve published assets

Release builds copy the Vite output to `wwwroot` next to the host executable. Call `UseEmbeddedAssets()` instead of assigning a dev-server URL:

```csharp
reactView.UseEmbeddedAssets();
```

This starts a loopback HTTP server on an ephemeral port and serves `wwwroot` with:

- SPA fallback to `index.html` for extensionless routes and for routes whose browser request accepts `text/html` (including dotted client-side routes); missing asset requests still return 404;
- immutable cache headers for files under `assets/` and `no-cache` for `index.html`;
- `ETag` (including `If-None-Match` lists), `Last-Modified`/`If-Modified-Since`, `If-Range`, `HEAD`, and single-range requests;
- HTTP/1.1 keep-alive connections with idle timeout and a bounded concurrent connection count;
- path traversal protection and a strict `Host` header check.

Configure the root directory, fallback file, caching, keep-alive, connection limits, or a fixed port through `EmbeddedAssetOptions` (`EnableKeepAlive`, `IdleConnectionTimeout`, `MaxRequestsPerConnection`, `MaxConnections`, `Port`). The server binds only to `127.0.0.1` and stops when the host window closes.

By default the port is ephemeral, so the loopback origin changes on every application start. Browser storage such as `localStorage` and IndexedDB is origin-scoped; set the same port and browser profile when that data must survive restarts:

```csharp
reactView.EmbeddedAssetsPort = 51823;
reactView.Session = ReactWebViewSession.Shared;
```

`EmbeddedAssetsPort` also applies to `SourceMode="Auto"`/`AssemblyEmbeddedAssets`; `UseEmbeddedAssets(options => options.Port = ...)` is the manual equivalent. Pick a port owned by the app because a bound port makes startup fail with a clear "port may be in use" error instead of silently falling back. Set `SourceMode="EmbeddedAssets"` in a Debug build to test the published path without switching to Release. On macOS, plain HTTP to localhost may require an App Transport Security exception (`NSAllowsLocalNetworking`). Set `AvaloniaReactEmbeddedAssets=true` in the project file to make the build warn when `wwwroot/index.html` is missing.

Every platform uses the same loopback server for embedded assets. There is no platform-specific virtual-host or scheme-handler path, so SPA fallback, caching, conditional requests, keep-alive, and connection limits behave identically on Windows, macOS, and Linux. The loopback server is the only asset transport the package exposes.

### Embed assets in the assembly

For single-file or installer deployments, embed the built frontend in the host assembly instead of shipping a `wwwroot` folder:

```xml
<PropertyGroup>
  <AvaloniaReactEmbedAssetsInAssembly>true</AvaloniaReactEmbedAssetsInAssembly>
</PropertyGroup>
```

The MSBuild target embeds `$(AvaloniaReactProjectDirectory)\dist\**` as manifest resources with the `AvaloniaWebview.React.Assets/` prefix before compilation (it runs the frontend build when it is enabled). In Release builds, `SourceMode="Auto"` resolves to `AssemblyEmbeddedAssets`: on startup the resources are extracted to a versioned directory under the system temporary directory and served by the loopback server, so no frontend files need to ship beside the executable. The extraction directory is reused across launches and changes when the assembly build changes.

Call `UseAssemblyEmbeddedAssets` directly when the source mode should not use build metadata:

```csharp
reactView.UseAssemblyEmbeddedAssets(
    extraction => extraction.ExtractionRoot = Path.Combine(AppContext.BaseDirectory, "react-cache"),
    server => server.EnableCaching = true);
```

`AssemblyEmbeddedAssetOptions` also exposes `Assembly`, `ResourcePrefix`, and `ForceExtraction`. When overriding `AvaloniaReactAssemblyAssetPrefix`, pass the same value to `ResourcePrefix`.

## Multi-window apps and shared sessions

Each window can host its own `ReactHostView`. Windows created with the same embedded-asset configuration share one loopback asset server, so the React origin stays stable and `localStorage`, cookies, and IndexedDB line up across windows. The last window release stops the server; closing one window does not break the others.

Browser data is controlled by `ReactWebViewSession`. All views use `ReactWebViewSession.Shared` by default:

```csharp
// Configure once, before creating windows.
ReactWebViewSession.Shared.ProfileName = "MyAppProfile";
ReactWebViewSession.Shared.UserDataFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "WebView");

var first = new MainWindow();
var second = new MainWindow();
first.Show();
second.Show();
```

Set `ProfileName`/`UserDataFolder` for deterministic WebView2 sharing on Windows; macOS shares the default WKWebView data store, and Linux uses `BaseDataDirectory`/`BaseCacheDirectory`. For an isolated child window, assign a fresh session before the window's WebView is created:

```csharp
var privateHost = new ReactHostView
{
    Session = ReactWebViewSession.CreateEphemeral(),
};
```

`CreateEphemeral()` uses WebView2 InPrivate mode, a non-persistent WKWebView store, or a WebKitGTK ephemeral data manager where the platform supports it. Host API registrations are still per `ReactWebView`; register the APIs each window needs, or share application services. Shared browser storage does not grant a window access to another window's registered .NET APIs.

The sample's native toolbar opens a shared window and an isolated window; setting `AVALONIA_REACT_MULTI_WINDOW=1` also opens a second shared window at startup.

## Reclaiming resources

The control exposes browser-data cleanup without shutting down the WebView:

```csharp
var result = await ReactHost.ClearBrowserDataAsync(
    ReactWebViewDataKind.LocalStorage |
    ReactWebViewDataKind.SessionStorage |
    ReactWebViewDataKind.CacheStorage |
    ReactWebViewDataKind.IndexedDb |
    ReactWebViewDataKind.ServiceWorkers |
    ReactWebViewDataKind.Cookies);

if (!result.IsSuccess)
{
    // result.Failed says which groups failed; result.Warnings has details.
}

await ReactHost.ClearCookiesAsync();
```

- `Cookies` uses the native cookie manager.
- Script-visible storage uses the loaded page and clears only the current origin.
- `CacheStorage` clears the browser Cache API, not the engine's HTTP disk cache. HTTP cache and the browser profile are engine-managed; to purge them, close the WebView/session and remove the configured profile directory.
- Shared sessions mean clearing data in one window affects other windows using the same origin/profile. Use an isolated `ReactWebViewSession` when that is not desired.
- `ClearBrowserDataAsync` requires a loaded page for script-visible storage; cookie cleanup still works. Failures are reported per `ReactWebViewDataKind` rather than throwing.

Assembly-embedded frontend files are extracted into a temporary directory. Clean that cache after the WebViews that use it have been shut down:

```csharp
await ReactWebViewResources.ClearExtractedAssetsAsync();

// Or release a single WebView's native/browser resources:
ReactHost.Shutdown();
```

`ClearExtractedAssetsAsync` deletes all extraction roots registered by this process, including a custom `AssemblyEmbeddedAssetOptions.ExtractionRoot`, and clears the extraction cache. Use `ReactHost.Shutdown()`/`Dispose()` to stop the WebView, queued bridge work, and the reference-counted asset server.

## Runtime availability preflight

Check the native WebView runtime before creating the window, so a missing runtime becomes an actionable startup message instead of a blank page:

```csharp
var availability = ReactWebViewAvailability.Check();
if (!availability.IsAvailable)
{
    ShowInstallGuidance(availability.Message, availability.Guidance);
    return;
}

// Throws PlatformNotSupportedException with the same guidance.
ReactWebViewAvailability.EnsureAvailable();
```

`Check` inspects Avalonia's platform adapter and requires no WebView instance. The report exposes `Platform`, `Status`, `AdapterType`, `Engine`, `RuntimeVersion`, `SupportedScenarios`, `Guidance`, and `Details` for logs. It detects Windows WebView2, macOS WKWebView, and Linux WebKitGTK and provides install instructions for the missing runtime.

## Platform notes

- Windows uses the installed Microsoft Edge WebView2 runtime.
- macOS uses the system WKWebView.
- Linux needs WebKitGTK or WPE WebKit runtime libraries.
- `AvaloniaWebview.React` targets `net10.0`; Windows hosts can reference it from a `net10.0-windows` project.
- The sample builds for `net10.0-windows` and `net10.0`; both use the same loopback HTTP asset server.
- The embedded asset pipeline in this repository targets desktop host applications; mobile and browser hosting are not covered by the sample or the MSBuild targets.

See the [official Avalonia WebView documentation](https://docs.avaloniaui.net/docs/app-development/embedding-web-content) for platform setup details.

