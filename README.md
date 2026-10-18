# AvaloniaWebview.React

A small .NET 10 toolkit for hosting a React frontend in Avalonia 12 with Avalonia's official `NativeWebView` and a JSON message bridge.

## Install

```sh
dotnet add package AvaloniaWebview.React
```

The package references `Avalonia.Controls.WebView` 12.1.0. Windows uses WebView2, macOS uses the system WKWebView, and Linux needs the WebKitGTK or WPE WebKit runtime. The browser runtime is provided by the operating system; Chromium is not bundled.

On Windows, the host executable must embed an application manifest that declares Windows 10/11 compatibility before `NativeWebView` can create its native child window. The sample does this with `samples/ReactHost/app.manifest` and the `ApplicationManifest` project property; add the same compatibility declaration to your host application's manifest.

The main package also imports an MSBuild frontend build target. When the consuming project contains a `ClientApp/package.json`, it installs dependencies with `npm ci` when needed, runs `npm run build` before the .NET build, and copies `dist` to the app's `wwwroot` output. Override `AvaloniaReactProjectDirectory`, `AvaloniaReactBuildCommand`, `AvaloniaReactInstallCommand`, or set `AvaloniaReactBuildOnBuild=false` in the project file to customize it.

For Vite development, install the optional extension:

```sh
dotnet add package AvaloniaWebview.React.Vite
```

In Debug builds it checks the configured Vite server before the .NET build, starts it when needed, and waits for the server to accept connections before continuing. Visual Studio's fast up-to-date check is disabled for Debug when this auto-start target applies, so each F5 verifies/restarts Vite if its process exited after the previous debug session. This adds a no-op MSBuild check to each Debug launch. Set `AvaloniaReactDevServerDirectory` to the React project directory; `AvaloniaReactDevServerUrl`, `AvaloniaReactDevServerCommand`, and `AvaloniaReactAutoStart` can override the defaults. Set `AvaloniaReactDevServerWarmupPath` to a frequently used client entry (for example, `src/main.tsx`) to wait until Vite has transformed it before launching the host. Node.js and npm must be installed, and frontend dependencies must be available.

To generate C# interfaces from TypeScript API contracts, install:

```sh
dotnet add package AvaloniaWebview.React.SourceGenerator
```

Add an exported interface in a `.d.ts` file under the .NET project. For example:

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
  saveCount(value: number): Promise<void>;
  findUsers(limit: number, filter?: string): Promise<string[]>;
}
```

This generates `MyApp.Frontend.IHostApi` with `Task<string> GetGreetingAsync(string name)`, `Task SaveCountAsync(double value)`, and `Task<IReadOnlyList<string>> FindUsersAsync(double limit, string? filter = default)`. Generated types include `string`, `number`, `boolean`, `void`, `object`, `unknown`, `any`, arrays, `Record<string, T>`, `Date`, nullable unions, and literal unions; any type that cannot be represented is mapped to `JsonElement` with an `AVR0002` diagnostic. Unsupported members produce `AVR0001`, generic interfaces are skipped with `AVR0003`, and unsupported enums are skipped with `AVR0004`.

Interfaces without methods and object `type` aliases generate DTO classes instead of API contracts:

```ts
// @namespace MyApp.Frontend
export enum Status {
  Active = "active",
  Disabled = "disabled",
}

export interface UserProfile {
  id: number;
  name: string;
  nickname?: string;
  status: Status;
}

export interface HostApi {
  save(profile: UserProfile): Promise<UserProfile>;
}
```

This emits a `UserProfile` class with `[JsonPropertyName]` properties, a `Status` enum using `JsonStringEnumMemberName` values, and an `IHostApi.SaveAsync(UserProfile)` method. Generated DTOs and enums declared in other `.d.ts` files are resolved when their names are unique; otherwise they fall back to `JsonElement`.

Generated interfaces are `partial` and carry `[ReactApiContract]`/`[ReactApiMethod]`, so the bridge uses the exact TypeScript API and method names. Keep the contract in the React project and reference it from the .NET project with `<AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />` to use one file for both sides.

Implement the generated interface and register it with the generated reflection-free helper `HostApiBridge.Register(webView, implementation)` (or the reflection-based `webView.RegisterApi<IHostApi>(implementation)`). On the React side, `createHostApiProxy<T>("HostApi")` returns a compiler-checked proxy, so method names and arguments no longer have to be written as strings:

```ts
import { createHostApiProxy } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
```

The lower-level `invokeHostApi<T>("HostApi", "getGreeting", "Ada")` form remains available.

Host API methods can observe cancellation. Set `<AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>` in the host project; generated interfaces then append `CancellationToken cancellationToken = default` to every method. The bridge passes a token that is canceled when React aborts or times out a call, when the page navigates or fails to load, and on shutdown. The bridge client sends `avalonia:invoke:cancel` automatically in those cases. The option defaults to `false`, so existing contracts and implementations are unchanged.

## React client package

The frontend bridge is published as `@avalonia-react/bridge`:

```sh
npm install @avalonia-react/bridge
```

It is ESM-only, includes TypeScript declarations, and forwards the reserved `avalonia:*` messages, host API calls, theme requests, JavaScript errors, cancellation, and timeouts. React projects can use `@avalonia-react/bridge/react` for `useHostApi`, `useHostApiInvoke`, `useAvaloniaMessage`, `useHostTheme`, `useSignalReady`, `useBridgeAvailable`, and `useReactHandler`. Inside this repository the sample uses the local package through `file:../../client`; run `npm --prefix client run build` before installing it.

## Agent skill

[`skills/avalonia-webview-react-hybrid`](skills/avalonia-webview-react-hybrid/SKILL.md) is an installable Agent Skill for teams using the library. It covers the hybrid architecture, contract-first host APIs, source modes, security boundaries, runtime diagnostics, multi-window reparenting, and trimming/AOT. See [`skills/README.md`](skills/README.md) for DSH, Claude Code, and Codex installation.

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

Configure the root directory, fallback file, caching, keep-alive, or connection limits through `EmbeddedAssetOptions` (`EnableKeepAlive`, `IdleConnectionTimeout`, `MaxRequestsPerConnection`, `MaxConnections`). The server binds only to `127.0.0.1` and stops when the host window closes. Set `SourceMode="EmbeddedAssets"` in a Debug build to test the published path without switching to Release. On macOS, plain HTTP to localhost may require an App Transport Security exception (`NSAllowsLocalNetworking`). Set `AvaloniaReactEmbeddedAssets=true` in the project file to make the build warn when `wwwroot/index.html` is missing.

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

## React to Avalonia messages

The TypeScript helper is published as [`@avalonia-react/bridge`](client/README.md):

```sh
npm install @avalonia-react/bridge
```

The NuGet package itself needs no npm dependency: React can call Avalonia's built-in JavaScript bridge directly.

```tsx
import { postMessage, requestTheme, signalReady, subscribe } from "@avalonia-react/bridge";

// Call after React mounts: tells the host the bridge is ready.
signalReady({ version: 1 });

const unsubscribe = subscribe<{ theme: "light" | "dark" }>("theme", ({ theme }) => {
  document.documentElement.dataset.theme = theme;
});
requestTheme();

postMessage("account:changed", { id: 42 });
```

The Avalonia WebView exposes JavaScript's `invokeCSharpAction` helper. The client uses it to send JSON to .NET. Subscribe after your React app mounts so frontend handlers are ready before the host sends messages. For the initial theme, subscribe in the entry module before rendering.

`avalonia:ready`, `avalonia:invoke`, `avalonia:invoke:result`, `avalonia:invoke:cancel`, `avalonia:error`, `theme`, and `theme:request` are reserved bridge messages handled by the control; application messages must use different names. `MessageReceived` only receives application messages. The first `avalonia:ready` from a page raises `BridgeReady`; use it to dismiss startup UI once React has mounted.

`AvaloniaReactWebView` sends a `theme` message when the WebView's actual theme variant changes and once after every successful navigation. The payload is `{ theme: "light" | "dark" }`: `ThemeVariant.Default` follows the system theme, and custom variants resolve through their inherited variant or fall back to the system theme, so React always receives a semantic value. Send `{ type: "theme:request", payload: {} }` after subscribing to receive the current theme immediately, which is the reliable way to capture the initial value before the first navigation message arrives.

If your React project only consumes the NuGet package, use the same message contract directly:

```ts
declare global {
  interface Window {
    invokeCSharpAction: (body: string) => void;
  }
}

export function postToAvalonia<T>(type: string, payload: T): void {
  window.invokeCSharpAction(JSON.stringify({ type, payload }));
}

export function onAvaloniaMessage<T>(type: string, listener: (payload: T) => void): () => void {
  const handler = (event: Event) => {
    const message = (event as CustomEvent<{ type: string; payload: T }>).detail;
    if (message?.type === type) listener(message.payload);
  };
  window.addEventListener("avalonia-react:message", handler);
  return () => window.removeEventListener("avalonia-react:message", handler);
}
```

## Avalonia to React messages

```csharp
webView.MessageReceived += (_, message) =>
{
    if (message.Type == "ready")
    {
        var version = message.GetPayload<ReadyPayload>();
        // Handle the message in the host application.
    }
};

await webView.PostMessageAsync("theme", new { theme = "dark" });

public sealed record ReadyPayload(int Version);
```

### Host-to-React calls

For request/response flows initiated by .NET, register a React handler and call it from the host:

```tsx
import { registerReactHandler } from "@avalonia-react/bridge";

const unregister = registerReactHandler<{ title: string }>(
  "dialog:confirm",
  async (payload, { signal }) => {
    // Use signal to dismiss the dialog when the host times out or cancels.
    return await showConfirm(payload.title, signal);
  },
);
```

```csharp
var confirmed = await ReactHost.InvokeReactAsync<bool>(
    "dialog:confirm",
    new { title = "Delete this item?" },
    TimeSpan.FromSeconds(10));
```

The host waits for `avalonia:react:result`, throws when the handler rejects, and times out after `ReactInvokeTimeout` (10 seconds by default) or the explicit timeout. A timeout or cancellation sends `avalonia:react:cancel`, which aborts the handler's `AbortSignal`. Use `useReactHandler` from `@avalonia-react/bridge/react` to keep a React handler current without re-registering on every render. Unknown handlers return an error to .NET instead of hanging.

`PostMessageAsync` completes when the script runs, but messages sent before the page loads are queued and delivered after a successful navigation. Use `LoadState` and `BridgeReady` to decide when the UI is ready instead of relying on navigation alone:

```csharp
webView.LoadStateChanged += (_, args) =>
{
    if (args.State == ReactLoadState.Failed) ShowError(args.Error);
};

webView.BridgeReady += (_, _) => HideStartupOverlay();
```

## Package layout

- `src/AvaloniaWebview.React`: the NuGet control and C# bridge.
- `src/AvaloniaWebview.React.Vite`: optional Debug-time Vite server startup target.
- `src/AvaloniaWebview.React.SourceGenerator`: C# interface generation from `.d.ts` declarations.
- `client`: TypeScript helpers for sending and subscribing to bridge messages.
- `skills`: installable Agent Skills for library users.
- `samples/ReactHost` and `samples/react-app`: a runnable desktop and Vite example.

## Run the sample

The sample UI uses Semi Design with React 19's adapter enabled. Debug serves the live Vite app and skips the separate production bundle build; Release builds the static frontend for packaging. Restore the frontend once:

```sh
cd client
npm install
npm run build

cd samples/react-app
npm install
```

Then start the Avalonia host in Debug configuration. The sample multi-targets the Windows WebView2 host and the cross-platform target; pass `-f` to select one. The Vite extension starts the frontend automatically; Vite hot reload continues inside the official native WebView.

VS Code users can select the **ReactHost** launch configuration (or **ReactHost (cross-platform)**) from `.vscode/launch.json`; the matching build tasks are in `.vscode/tasks.json`.

```sh
# Windows (WebView2)
dotnet run --project samples/ReactHost -f net10.0-windows

# macOS/Linux (system WebKit runtime required)
dotnet run --project samples/ReactHost -f net10.0
```

The sample uses a conventional Vite entry: `index.html` only declares the root element, `main.tsx` renders `<App />` directly, and the app calls `signalReady()` from `@avalonia-react/bridge` after React mounts. The sample window uses `ReactHostView` with `SourceMode="Auto"` and `WaitForBridgeReady="True"`, so the built-in themed overlay replaces the previous hand-written loading/error UI; the code-behind only registers the generated API and handles the demo messages. The service table is a separate dynamic module loaded only when requested. The workbench then calls the generated .NET API for its greeting, and the "发送消息到 .NET" button demonstrates React-to-host messaging plus a host acknowledgment (`react:ack`). Vite hot reload continues to work inside the native WebView. The Avalonia host uses Semi.Avalonia so the built-in overlay follows the same light/dark theme as the React UI.

Build all three NuGet packages with:

```sh
dotnet pack src/AvaloniaWebview.React/AvaloniaWebview.React.csproj -c Release
dotnet pack src/AvaloniaWebview.React.Vite/AvaloniaWebview.React.Vite.csproj -c Release
dotnet pack src/AvaloniaWebview.React.SourceGenerator/AvaloniaWebview.React.SourceGenerator.csproj -c Release
```

Build the npm tarball with `cd client && npm pack --pack-destination ../artifacts/packages`. See [RELEASING.md](RELEASING.md) for the complete release checklist.

## Build and test

```sh
dotnet build AvaloniaWebview.React.slnx -c Release -p:AvaloniaReactBuildOnBuild=false -p:AvaloniaReactAutoStart=false
dotnet test tests/AvaloniaWebview.React.Tests/AvaloniaWebview.React.Tests.csproj -c Release
dotnet test tests/AvaloniaWebview.React.SourceGenerator.Tests/AvaloniaWebview.React.SourceGenerator.Tests.csproj -c Release
npm --prefix client run build
npm --prefix client test
npm --prefix client run verify:package
```

The `AvaloniaReactBuildOnBuild` and `AvaloniaReactAutoStart` overrides skip npm and Vite during a .NET-only build. The control tests cover the bridge protocol, source-mode resolution, origin policy, message queue, API argument binding, JavaScript error parsing, and the loopback asset server (including keep-alive and conditional requests). The generator tests compile generated interfaces and assert the diagnostics. The client tests run on Node's built-in test runner and exercise messaging, the typed proxy, timeouts, aborts, page unload, and error forwarding. GitHub Actions runs all three suites on Windows and Linux and packs the four publishable packages.

## Release

Set `AvaloniaReactVersion` in `Directory.Build.props`, update `client/package.json` and `client/package-lock.json`, commit, and push a matching `vX.Y.Z` tag. The workflow validates all versions, runs tests, verifies package contents, publishes the NuGet packages (including `.snupkg` symbols) using NuGet Trusted Publishing when `NUGET_USER` and the nuget.org policy are configured, publishes `@avalonia-react/bridge` when `NPM_TOKEN` is configured, and creates a GitHub release with the `.nupkg`, `.snupkg`, and `.tgz` files. See [RELEASING.md](RELEASING.md) for details.

## Trimming and Native AOT

The library is marked `IsTrimmable` and `IsAotCompatible`; its trim and AOT analyzers build cleanly. Prefer the generated registration helper, which invokes the implementation through delegates instead of `MethodInfo` reflection:

```csharp
HostApiBridge.Register(webView, new HostApi());
```

Generated handlers read `string`, `number`, `boolean`, `DateTimeOffset`, `byte[]`, and `JsonElement` arguments directly from `JsonElement`, and the bridge writes primitive/`JsonElement` results without reflection. The generator also emits converters and a type-info resolver for generated DTOs, enums, lists, dictionaries, nullable values, and the types used by generated contracts. A module initializer installs that resolver into `ReactJsonSerializerDefaults.Options`, so most trimmed/Native AOT apps need no JSON setup at all.

For custom payload types that are not declared in a generated `.d.ts` contract (for example an arbitrary `PostMessageAsync<T>` or `InvokeReactAsync<T>` type), provide a source-generated context:

```csharp
[JsonSerializable(typeof(CustomMessage))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;
// Or per view: webView.BridgeJsonSerializerOptions = AppJsonContext.Default.Options;
```

Setting `ReactJsonSerializerDefaults.Options` keeps the generated resolver as a fallback, so generated DTO/enum contracts and custom context types can coexist. The reflection-based `RegisterApi<TApi>` path remains annotated with `RequiresUnreferencedCode`/`RequiresDynamicCode`; use `HostApiBridge.Register` for trimming/AOT. Embedded assets, the loopback server, and WebView hosting have no additional trim requirements. The sample's primitive API and a generated DTO/enum/list contract were verified with `PublishTrimmed=true` and reflection-based serialization disabled.

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
