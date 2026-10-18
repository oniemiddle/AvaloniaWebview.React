# Avalonia.React

A small .NET 10 toolkit for hosting a React frontend in Avalonia 12 with Avalonia's official `NativeWebView` and a JSON message bridge.

## Install

```sh
dotnet add package Avalonia.React
```

The package references `Avalonia.Controls.WebView` 12.1.0. Windows uses WebView2, macOS uses the system WKWebView, and Linux needs the WebKitGTK or WPE WebKit runtime. The browser runtime is provided by the operating system; Chromium is not bundled.

On Windows, the host executable must embed an application manifest that declares Windows 10/11 compatibility before `NativeWebView` can create its native child window. The sample does this with `samples/ReactHost/app.manifest` and the `ApplicationManifest` project property; add the same compatibility declaration to your host application's manifest.

The main package also imports an MSBuild frontend build target. When the consuming project contains a `ClientApp/package.json`, it installs dependencies with `npm ci` when needed, runs `npm run build` before the .NET build, and copies `dist` to the app's `wwwroot` output. Override `AvaloniaReactProjectDirectory`, `AvaloniaReactBuildCommand`, `AvaloniaReactInstallCommand`, or set `AvaloniaReactBuildOnBuild=false` in the project file to customize it.

For Vite development, install the optional extension:

```sh
dotnet add package Avalonia.React.Vite
```

In Debug builds it checks the configured Vite server before the .NET build, starts it when needed, and waits for the server to accept connections before continuing. Visual Studio's fast up-to-date check is disabled for Debug when this auto-start target applies, so each F5 verifies/restarts Vite if its process exited after the previous debug session. This adds a no-op MSBuild check to each Debug launch. Set `AvaloniaReactDevServerDirectory` to the React project directory; `AvaloniaReactDevServerUrl`, `AvaloniaReactDevServerCommand`, and `AvaloniaReactAutoStart` can override the defaults. Set `AvaloniaReactDevServerWarmupPath` to a frequently used client entry (for example, `src/main.tsx`) to wait until Vite has transformed it before launching the host. Node.js and npm must be installed, and frontend dependencies must be available.

To generate C# interfaces from TypeScript API contracts, install:

```sh
dotnet add package Avalonia.React.SourceGenerator
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

This generates `MyApp.Frontend.IHostApi` with `Task<string> GetGreetingAsync(string name)`, `Task SaveCountAsync(double value)`, and `Task<IReadOnlyList<string>> FindUsersAsync(double limit, string? filter = default)`. Generated types include `string`, `number`, `boolean`, `void`, `object`, `unknown`, `any`, arrays, `Record<string, T>`, `Date`, nullable unions, and literal unions; any type that cannot be represented is mapped to `JsonElement` with an `AVR0002` diagnostic. Unsupported members produce `AVR0001`, and generic interfaces are skipped with `AVR0003`.

Generated interfaces are `partial` and carry `[ReactApiContract]`/`[ReactApiMethod]`, so the bridge uses the exact TypeScript API and method names. Keep the contract in the React project and reference it from the .NET project with `<AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />` to use one file for both sides.

Implement the generated interface and register it on the WebView with `webView.RegisterApi<IHostApi>(implementation)`. On the React side, `createHostApiProxy<T>("HostApi")` returns a compiler-checked proxy, so method names and arguments no longer have to be written as strings:

```ts
import { createHostApiProxy } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
```

The lower-level `invokeHostApi<T>("HostApi", "getGreeting", "Ada")` form remains available.

Host API methods can observe cancellation. Set `<AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>` in the host project; generated interfaces then append `CancellationToken cancellationToken = default` to every method. The bridge passes a token that is canceled when React aborts or times out a call, when the page navigates or fails to load, and on shutdown. The bridge client sends `avalonia:invoke:cancel` automatically in those cases. The option defaults to `false`, so existing contracts and implementations are unchanged.

## Host a React development server

Add the control to an Avalonia view. Point it to the Vite development server while developing, or let `SourceMode="Auto"` select the source. The included host uses Vite in Debug and embedded assets in Release; set `Source` or `SourceMode` to override both.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:react="using:Avalonia.React">
  <react:AvaloniaReactWebView Source="http://localhost:5173/" />
</Window>
```

Start the frontend with `npm run dev -- --host 0.0.0.0`. Keep the Vite port in sync with the `Source` URI.

### Configure the source in XAML

`SourceMode="Auto"` removes the usual Debug/Release branching from the application:

```xml
<react:AvaloniaReactWebView SourceMode="Auto"
                            EnableWindowsOffscreenComposition="True" />
```

The Avalonia.React MSBuild targets emit assembly metadata into the host executable: Debug builds resolve `Auto` to the Vite server, other configurations resolve to embedded assets. Override the defaults with `AvaloniaReactDefaultSourceMode`, `AvaloniaReactDevServerUrl`, or `AvaloniaReactEnableDevTools`, or set `SourceMode` to `Manual`, `DevServer`, or `EmbeddedAssets`. Set `AvaloniaReactEmitAssemblyMetadata=false` to opt out of the generated metadata. `Source` is only assigned when the control enters the visual tree, so the source can be configured from XAML or a constructor.

### Use the built-in host view

`ReactHostView` wraps `AvaloniaReactWebView` with a themed loading/error overlay and forwards the common configuration, events, and methods. The overlay hides after navigation, or after the React client's `signalReady()` when `WaitForBridgeReady` is enabled, with a fallback timeout:

```xml
<react:ReactHostView x:Name="ReactHost"
                     SourceMode="Auto"
                     EnableWindowsOffscreenComposition="True"
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
- `EnableDevTools` requests the platform dev tools (defaults to true in Debug through build metadata). `EnableWindowsOffscreenComposition` sets WebView2's experimental offscreen composition, which is required when Avalonia content renders above the WebView (for example a loading overlay).
- `BridgeError` reports exceptions thrown by application `MessageReceived` handlers instead of letting them escape the WebView event loop.
- `JavaScriptError` receives window errors and unhandled promise rejections forwarded by the bridge client, including message, stack, source, line, and column.
- `ApiInvokeTimeout` (30 seconds by default) returns a timeout error to React when a host API implementation runs too long; set it to `TimeSpan.Zero` to disable.
- `RegisterApi` rejects duplicate API names; `UnregisterApi` removes one.

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

On Windows, `EmbeddedAssetTransport.NativeHost` maps the asset folder to a WebView2 virtual host instead of opening a loopback port. It requires a Windows target framework such as `net10.0-windows`; the cross-platform `net10.0` build keeps using loopback:

```csharp
reactView.UseEmbeddedAssets(options => options.Transport = EmbeddedAssetTransport.NativeHost);
```

The host defaults to `https://avalonia-react.local/` and can be changed with `NativeHostName`. It serves static files only; SPA route fallback still requires the default loopback transport. WebView2 does not raise `WebResourceRequested` for virtual-host requests, so the native host cannot implement route fallback itself. Set `Transport = EmbeddedAssetTransport.Auto` to use the native host on Windows and the loopback server on other platforms. macOS and Linux native scheme handlers are not implemented: WKURLSchemeHandler must be registered before the WKWebView is created, but Avalonia only exposes the existing web view; Linux native scheme serving needs additional GIO interop. Both platforms continue to use loopback HTTP. Set `EmbeddedAssetsTransport="NativeHost"` (or `Auto`) on the control to test the native transport.

## React to Avalonia messages

The repository includes an optional TypeScript helper in [`client`](client/README.md). The NuGet package itself needs no npm dependency: React can call Avalonia's built-in JavaScript bridge directly.

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

`PostMessageAsync` completes when the script runs, but messages sent before the page loads are queued and delivered after a successful navigation. Use `LoadState` and `BridgeReady` to decide when the UI is ready instead of relying on navigation alone:

```csharp
webView.LoadStateChanged += (_, args) =>
{
    if (args.State == ReactLoadState.Failed) ShowError(args.Error);
};

webView.BridgeReady += (_, _) => HideStartupOverlay();
```

## Package layout

- `src/Avalonia.React`: the NuGet control and C# bridge.
- `src/Avalonia.React.Vite`: optional Debug-time Vite server startup target.
- `src/Avalonia.React.SourceGenerator`: C# interface generation from `.d.ts` declarations.
- `client`: TypeScript helpers for sending and subscribing to bridge messages.
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

```sh
# Windows (WebView2)
dotnet run --project samples/ReactHost -f net10.0-windows

# macOS/Linux (system WebKit runtime required)
dotnet run --project samples/ReactHost -f net10.0
```

The sample uses a conventional Vite entry: `index.html` only declares the root element, `main.tsx` renders `<App />` directly, and the app calls `signalReady()` from `@avalonia-react/bridge` after React mounts. The sample window uses `ReactHostView` with `SourceMode="Auto"`, `EnableWindowsOffscreenComposition="True"`, and `WaitForBridgeReady="True"`, so the built-in themed overlay replaces the previous hand-written loading/error UI; the code-behind only registers the generated API and handles the demo messages. The service table is a separate dynamic module loaded only when requested. The workbench then calls the generated .NET API for its greeting, and the "发送消息到 .NET" button demonstrates React-to-host messaging plus a host acknowledgment (`react:ack`). Vite hot reload continues to work inside the native WebView. The Avalonia host uses Semi.Avalonia so the built-in overlay follows the same light/dark theme as the React UI.

Build all three NuGet packages with `dotnet pack src/Avalonia.React/Avalonia.React.csproj -c Release`, `dotnet pack src/Avalonia.React.Vite/Avalonia.React.Vite.csproj -c Release`, and `dotnet pack src/Avalonia.React.SourceGenerator/Avalonia.React.SourceGenerator.csproj -c Release`.

## Build and test

```sh
dotnet build Avalonia.React.sln -c Release -p:AvaloniaReactBuildOnBuild=false -p:AvaloniaReactAutoStart=false
dotnet test tests/Avalonia.React.Tests/Avalonia.React.Tests.csproj -c Release
dotnet test tests/Avalonia.React.SourceGenerator.Tests/Avalonia.React.SourceGenerator.Tests.csproj -c Release
npm --prefix client run build
npm --prefix client test
```

The `AvaloniaReactBuildOnBuild` and `AvaloniaReactAutoStart` overrides skip npm and Vite during a .NET-only build. The control tests cover the bridge protocol, source-mode resolution, origin policy, message queue, API argument binding, JavaScript error parsing, and the loopback asset server (including keep-alive and conditional requests). The generator tests compile generated interfaces and assert the diagnostics. The client tests run on Node's built-in test runner and exercise messaging, the typed proxy, timeouts, aborts, page unload, and error forwarding. GitHub Actions runs all three suites on Windows and Linux and packs all three packages.

## Release

Set `AvaloniaReactVersion` in `Directory.Build.props`, commit, and push a matching `vX.Y.Z` tag. The release workflow validates the tag, runs all tests, packs the three packages, publishes them to NuGet when the `NUGET_API_KEY` repository secret is configured, and creates a GitHub release with the `.nupkg` files attached.

## Platform notes

- Windows uses the installed Microsoft Edge WebView2 runtime.
- macOS uses the system WKWebView.
- Linux needs WebKitGTK or WPE WebKit runtime libraries.
- The sample builds for `net10.0-windows` and `net10.0`; the cross-platform target uses loopback HTTP for embedded assets.
- The embedded asset pipeline in this repository targets desktop host applications; mobile and browser hosting are not covered by the sample or the MSBuild targets.

See the [official Avalonia WebView documentation](https://docs.avaloniaui.net/docs/app-development/embedding-web-content) for platform setup details.
