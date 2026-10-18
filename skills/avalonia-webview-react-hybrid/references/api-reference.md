# API reference

This reference describes the public surface of AvaloniaWebview.React `0.17.x`.

## Identity

| Item | Value |
| --- | --- |
| NuGet control package | `AvaloniaWebview.React` |
| NuGet Vite package | `AvaloniaWebview.React.Vite` |
| NuGet generator package | `AvaloniaWebview.React.SourceGenerator` |
| Namespace / assemblies | `AvaloniaWebview.React` |
| WebView control type | `AvaloniaReactWebView` (the type name is intentionally unchanged) |
| High-level host control | `ReactHostView` |
| npm client | `@avalonia-react/bridge` |
| Minimum framework | .NET 10, Avalonia 12.1, Node 18 for the client |

## `ReactHostView`

`ReactHostView` is a `Grid` that composes an `AvaloniaReactWebView` and a themed overlay.

### State and diagnostics

| Member | Meaning |
| --- | --- |
| `AvaloniaReactWebView WebView` | Raw WebView for advanced APIs and events. |
| `bool IsShutdown` | Shutdown has been called. |
| `bool IsOverlayVisible` | The built-in overlay is visible. |
| `ReactLoadState LoadState` | `Idle`, `Loading`, `Loaded`, or `Failed`. |
| `string? LoadError` | Failure message when `LoadState == Failed`. |
| `bool IsPageLoaded` | Navigation completed successfully. |
| `bool IsBridgeReady` | React called `signalReady()`. |
| `WebViewAdapterInfo? AdapterInfo` | Platform adapter information, when exposed. |
| `DetailedWebViewAdapterInfo? AdapterDiagnostics` | Detailed installation/support diagnostics. |
| `bool? IsWebViewRuntimeInstalled` | `null` when the platform does not expose the answer. |
| `string? WebViewUnavailableReason` | Human-readable runtime failure reason. |
| `bool CanGoBack`, `bool CanGoForward` | History state. |

### Configuration

| Member | Default | Meaning |
| --- | --- | --- |
| `ReactSourceMode SourceMode` | `Manual` | `Auto`, `DevServer`, `EmbeddedAssets`, `AssemblyEmbeddedAssets`, or `Manual`. |
| `Uri? DevServerUrl` | `null` | Explicit Vite URL; non-null makes `Auto` choose `DevServer`. |
| `string? EmbeddedAssetsRoot` | `null` | Root served by the loopback asset server; defaults to `wwwroot`. |
| `ReactExternalNavigationPolicy ExternalNavigationPolicy` | `OpenInSystemBrowser` | `OpenInSystemBrowser`, `Block`, or `Allow`. |
| `IList<Uri> AllowedOrigins` | empty | Additional origins allowed to navigate the WebView. Not an API trust list. |
| `IList<Uri> TrustedApiOrigins` | empty | Additional origins allowed to invoke host APIs. |
| `bool ShowOverlay` | `true` | Enables the built-in loading/error overlay. |
| `bool WaitForBridgeReady` | `false` | Wait for `signalReady()` before hiding the overlay. |
| `TimeSpan BridgeReadyTimeout` | 10s | Fallback when waiting for the ready handshake. |
| `TimeSpan AdapterStartupTimeout` | 10s | Adapter watchdog; 0 disables it. |
| `string LoadingText` | `Loading React…` | Overlay loading text. |
| `string ErrorTextFormat` | `The React app failed to load: {0}` | Uses `LoadError` as `{0}`. |
| `string RetryText` | `Retry` | Retry button label. |
| `IBrush? OverlayBackground` | `null` | Custom overlay background; otherwise theme-derived. |
| `bool EnableDevTools` | build metadata | Requests platform developer tools. |
| `TimeSpan ApiInvokeTimeout` | 30s | Host API timeout; `TimeSpan.Zero` disables. |
| `int MaxIncomingMessageCharacters` | 4,000,000 | Incoming bridge message limit; `<= 0` disables. |
| `JsonSerializerOptions BridgeJsonSerializerOptions` | generated defaults or Web defaults | Payload/result serialization options. |
| `bool PreserveWebViewOnDetach` | `true` on `ReactHostView` | Reparent instead of destroying on detach. |
| `TimeSpan ReparentingTimeout` | 10s | Time to wait for reattachment before releasing the native control. |

### Methods

| Method | Meaning |
| --- | --- |
| `RegisterApi<TApi>(TApi implementation, string? apiName = null)` | Reflection-based registration. Use generated helpers for trim/AOT. |
| `RegisterApi(string apiName, IReadOnlyDictionary<string, ReactApiMethodHandler> methods)` | Low-level reflection-free registration used by generated helpers. |
| `UnregisterApi(string apiName)` | Removes a registered API. |
| `PostMessageAsync<T>(string type, T payload)` | Sends a host message to React; queued until the page is ready. |
| `Reload()` | Reloads the page and resets load state. |
| `ApplySourceMode()` | Applies `SourceMode` immediately. |
| `GoBack()`, `GoForward()`, `Stop()` | History/navigation controls. |
| `Navigate(Uri source)` | Explicit navigation. |
| `ShowPrintUI()` | Platform print UI. |
| `PrintToPdfStreamAsync()` / `PrintToPdfStreamAsync(WebViewPrintSettings)` | PDF output. |
| `TryGetCookieManager()` | Platform cookie manager or `null`. |
| `TryGetCommandManager()` | Platform edit command manager or `null`. |
| `BeginReparenting(bool yieldOnLayoutBeforeExiting = true)` | Manual reparenting scope. Dispose to re-evaluate the parent. |
| `BeginReparentingAsync()` | Async manual reparenting scope. |
| `Shutdown()` / `Dispose()` | Idempotent teardown; releases the asset transport and registered APIs. |

### Events

| Event | Meaning |
| --- | --- |
| `MessageReceived` | Application messages only (`ReactMessageReceivedEventArgs`). |
| `LoadStateChanged` | Lifecycle transitions. |
| `BridgeReady` | React sent `avalonia:ready`; `ReactBridgeReadyEventArgs`. |
| `BridgeError` | An application `MessageReceived` handler threw. |
| `JavaScriptError` | Window error or unhandled rejection forwarded by the client. |
| `SecurityViolation` | An origin or message-size boundary rejected a request. |

## `AvaloniaReactWebView`

Direct use gives the raw `NativeWebView` surface. Differences from `ReactHostView`:

- No overlay or overlay-related options.
- `PreserveWebViewOnDetach` defaults to `false`.
- `EnableWindowsOffscreenComposition` defaults to `false`; set it when layering Avalonia content above WebView2.
- `UseEmbeddedAssets(Action<EmbeddedAssetOptions>?)` starts the loopback asset server and navigates.
- `UseAssemblyEmbeddedAssets(...)` extracts embedded frontend files first.
- `GetAdapterDiagnostics()` returns `DetailedWebViewAdapterInfo?`.
- `MaxQueuedBridgeMessages` (default 1024), `MaxIncomingMessageCharacters`, `ApiInvokeTimeout`, `BridgeJsonSerializerOptions`, and `PreserveWebViewOnDetach`/`ReparentingTimeout` are configured here directly when not forwarded by `ReactHostView`.
- Inherited native capabilities include `UserAgent`, `AdapterInfo`, `CanGoBack`, `CanGoForward`, `GoBack`, `GoForward`, `Stop`, `Navigate`, `ShowPrintUI`, `PrintToPdfStreamAsync`, `TryGetCookieManager`, `TryGetCommandManager`, `BeginReparenting`, and `BeginReparentingAsync`.

## Client (`@avalonia-react/bridge`)

```ts
export function isBridgeAvailable(): boolean;
export function postMessage<T>(type: string, payload: T): void;
export function signalReady<T = Record<string, never>>(payload?: T): boolean;
export function requestTheme(): boolean;
export function subscribe<T>(type: string, listener: (payload: T) => void): () => void;
export function invokeHostApi<T>(api: string, method: string, ...args: unknown[]): Promise<T>;
export function createHostApiProxy<T extends object>(api: string): T;
export function invokeHostApiWithOptions<T>(
  api: string,
  method: string,
  args: readonly unknown[],
  options?: { timeoutMs?: number; signal?: AbortSignal },
): Promise<T>;
```

Constants:

```ts
export const bridgeDomainEvent = "avalonia-react:message";
export const bridgeMessageTypes = {
  ready: "avalonia:ready",
  invoke: "avalonia:invoke",
  invokeResult: "avalonia:invoke:result",
  invokeCancel: "avalonia:invoke:cancel",
  theme: "theme",
  themeRequest: "theme:request",
  error: "avalonia:error",
} as const;
```

The module is ESM-only and safe to import without a browser `window`; `isBridgeAvailable()` is `false`, and `subscribe()` returns a no-op unsubscribe. The package is SSR-safe by design.

### React hooks (`@avalonia-react/bridge/react`)

```ts
export function useHostApi<T extends object>(api: string): T;
export function useAvaloniaMessage<T>(type: string, handler: (payload: T) => void): void;
export function useBridgeAvailable(): boolean;
export function useHostTheme(): "light" | "dark" | null;
export function useSignalReady<T = Record<string, never>>(payload?: T): boolean;
export function useHostApiInvoke<T>(
  api: string,
  method: string,
  options?: { timeoutMs?: number; signal?: AbortSignal },
): {
  data: T | null;
  error: Error | null;
  loading: boolean;
  invoke: (...args: unknown[]) => Promise<T>;
  cancel: () => void;
};
```

`react` is an optional peer dependency. The hooks follow React 18+ rules; effects/subscriptions run in the browser host, and server rendering differs only in that effects do not execute.

## Bridge protocol

Messages are JSON objects with this envelope:

```json
{ "type": "application:event", "payload": {} }
```

Host API calls use:

```json
{ "type": "avalonia:invoke", "payload": { "requestId": "…", "api": "HostApi", "method": "getGreeting", "args": ["Ada"] } }
```

Results return as:

```json
{ "type": "avalonia:invoke:result", "payload": { "requestId": "…", "result": "Hello Ada", "error": null } }
```

Cancellation uses `avalonia:invoke:cancel` with `{ "requestId": "…" }`. Timeout, abort, navigation, and page unload all trigger cancellation for host implementations that accept a `CancellationToken`.

Reserved message types cannot be reused. Do not send application payloads as `avalonia:ready`, `avalonia:invoke`, `avalonia:invoke:result`, `avalonia:invoke:cancel`, `theme`, `theme:request`, or `avalonia:error`.

## Generator contract

- Every exported interface containing methods generates a partial interface with `[ReactApiContract]` and per-method `[ReactApiMethod]`.
- A `{TypeScriptName}Bridge` static class is generated with `Register(AvaloniaReactWebView webView, IInterface implementation, string? apiName = null)`.
- `Promise<T>` → `Task<T>`; `Promise<void>` and non-Promise `void` → `Task`; `number` → `double`; `boolean` → `bool`; arrays → `IReadOnlyList<T>`; `Record<string,T>` → `IReadOnlyDictionary<string,T>`; `Date` → `DateTimeOffset`; `ArrayBuffer`/`Uint8Array` → `byte[]`.
- Optional parameters become optional C# parameters with `default`.
- Add `<AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>` to append `CancellationToken cancellationToken = default` and pass the bridge token to implementations.
- Property-only interfaces and object `export type` aliases generate partial DTO classes. Optional properties are nullable; required reference properties use `= default!`.
- String enums use `JsonStringEnumConverter<T>` plus `JsonStringEnumMemberName`; numeric enums keep explicit values.
- Cross-file DTO/enum references resolve by unique type name; ambiguous names fall back to `JsonElement`.

Diagnostics:

| ID | Meaning |
| --- | --- |
| `AVR0001` | Unsupported interface member or enum member. |
| `AVR0002` | A TypeScript type mapped to `System.Text.Json.JsonElement`. |
| `AVR0003` | Generic interface skipped. |
| `AVR0004` | Unsupported enum skipped (mixed/invalid values). |

## Source modes and build metadata

| Mode | Resolution |
| --- | --- |
| `Auto` | Debug → `DevServer`; Release → `EmbeddedAssets` unless assembly embedding is enabled; explicit `DevServerUrl` wins. |
| `DevServer` | `Source = DevServerUrl ?? default Vite URL`. |
| `EmbeddedAssets` | Loopback server rooted at `wwwroot` or `EmbeddedAssetsRoot`. |
| `AssemblyEmbeddedAssets` | Extracts embedded resources, then serves them on loopback. |
| `Manual` | Application assigns `Source`. |

Useful MSBuild properties:

- `AvaloniaReactDefaultSourceMode`
- `AvaloniaReactDevServerUrl`
- `AvaloniaReactEmbeddedAssets`
- `AvaloniaReactEmbedAssetsInAssembly`
- `AvaloniaReactAssemblyAssetPrefix`
- `AvaloniaReactProjectDirectory`
- `AvaloniaReactBuildOnBuild`
- `AvaloniaReactBuildCommand`
- `AvaloniaReactInstallCommand`
- `AvaloniaReactAutoStart` (Vite package)
- `AvaloniaReactDevServerDirectory`
- `AvaloniaReactDevServerCommand`
- `AvaloniaReactDevServerWarmupPath`
- `AvaloniaReactGenerateCancellationToken`

## Security and trimming defaults

- Application origin is trusted after the first HTTP navigation; `TrustedApiOrigins` adds origins.
- `AllowedOrigins` never grants API access.
- Non-HTTP navigation is always blocked; only HTTP(S) and `about:blank` are allowed.
- Oversized incoming messages are dropped and reported through `SecurityViolation`.
- Generated registration avoids `MethodInfo` reflection; primitive payloads avoid JSON reflection.
- For DTO/enum/collection payloads under trimming/AOT, install a source-generated `JsonSerializerContext` through `ReactJsonSerializerDefaults.Options` or `BridgeJsonSerializerOptions`.
