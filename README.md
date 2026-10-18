# Avalonia.React

A small .NET 10 toolkit for hosting a React frontend in Avalonia 12 with Avalonia's official `NativeWebView` and a JSON message bridge.

## Install

```sh
dotnet add package Avalonia.React
```

The package references `Avalonia.Controls.WebView` 12.1.0. Windows uses WebView2, macOS/iOS use WKWebView, and Linux needs the system WebKit runtime. The browser runtime is provided by the operating system; Chromium is not bundled.

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
}
```

This generates `MyApp.Frontend.IHostApi` with `Task<string> GetGreetingAsync(string name)` and `Task SaveCountAsync(double value)`. The first version supports interface method signatures and the TypeScript primitive types `string`, `number`, `boolean`, `void`, `object`, `unknown`, `any`, and arrays; other types map to `JsonElement`.

Implement the generated interface and register it on the WebView with `webView.RegisterApi<IHostApi>(implementation)`. The React helper exposes the methods through `invokeHostApi<T>("HostApi", "getGreeting", "Ada")`.

## Host a React development server

Add the control to an Avalonia view. Point it to the Vite development server while developing. The included host uses Vite in Debug and embedded assets in Release; set `AVALONIA_REACT_URL` to the deployed frontend URL to override both.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:react="using:Avalonia.React">
  <react:AvaloniaReactWebView Source="http://localhost:5173/" />
</Window>
```

Start the frontend with `npm run dev -- --host 0.0.0.0`. Keep the Vite port in sync with the `Source` URI.

### Serve published assets

Release builds copy the Vite output to `wwwroot` next to the host executable. Call `UseEmbeddedAssets()` instead of assigning a dev-server URL:

```csharp
reactView.UseEmbeddedAssets();
```

This starts a loopback HTTP server on an ephemeral port and serves `wwwroot` with:

- SPA fallback to `index.html` for extensionless routes;
- immutable cache headers for files under `assets/` and `no-cache` for `index.html`;
- `ETag`, `HEAD`, and single-range requests;
- path traversal protection and a strict `Host` header check.

Configure the root directory, fallback file, or caching through `EmbeddedAssetOptions`. The server binds only to `127.0.0.1` and stops when the host window closes. Set `AVALONIA_REACT_EMBEDDED=1` in a Debug build to test the published path without switching to Release. On macOS and iOS, plain HTTP to localhost may require an App Transport Security exception (`NSAllowsLocalNetworking`). Set `AvaloniaReactEmbeddedAssets=true` in the project file to make the build warn when `wwwroot/index.html` is missing.

On Windows, `EmbeddedAssetTransport.NativeHost` maps the asset folder to a WebView2 virtual host instead of opening a loopback port. It requires a Windows target framework such as `net10.0-windows`; the cross-platform `net10.0` build keeps using loopback:

```csharp
reactView.UseEmbeddedAssets(options => options.Transport = EmbeddedAssetTransport.NativeHost);
```

The host defaults to `https://avalonia-react.local/` and can be changed with `NativeHostName`. It serves static files only; SPA route fallback still requires the default loopback transport. WebView2 does not raise `WebResourceRequested` for virtual-host requests, so the native host cannot implement route fallback itself. Set `Transport = EmbeddedAssetTransport.Auto` to use the native host on Windows and the loopback server on other platforms. macOS and Linux native scheme handlers are not implemented: WKURLSchemeHandler must be registered before the WKWebView is created, but Avalonia only exposes the existing web view; Linux native scheme serving needs additional GIO interop. Both platforms continue to use loopback HTTP. The sample enables the native transport when `AVALONIA_REACT_NATIVE=1` is set.

## React to Avalonia messages

The repository includes an optional TypeScript helper in [`client`](client/README.md). The NuGet package itself needs no npm dependency: React can call Avalonia's built-in JavaScript bridge directly.

```tsx
import { postMessage, subscribe } from "@avalonia-react/bridge";

postMessage("ready", { version: 1 });
const unsubscribe = subscribe<{ theme: "light" | "dark" }>("theme", ({ theme }) => {
  document.documentElement.dataset.theme = theme;
});
postMessage("theme:request", {});
```

The Avalonia WebView exposes JavaScript's `invokeCSharpAction` helper. The client uses it to send JSON to .NET. Subscribe after your React app mounts so frontend handlers are ready before the host sends messages. For the initial theme, subscribe in the entry module before rendering.

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

Then start the Avalonia host in Debug configuration. The Vite extension starts the frontend automatically; Vite hot reload continues inside the official native WebView.

```sh
dotnet run --project samples/ReactHost
```

The sample uses a conventional Vite entry: `index.html` only declares the root element, `main.tsx` renders `<App />` directly, and the app notifies the Avalonia host after React mounts. The service table is a separate dynamic module loaded only when requested. The workbench then calls the generated .NET API for its greeting. Vite hot reload continues to work inside the native WebView. The Avalonia host uses Semi.Avalonia so native loading and error states follow the same light/dark theme as the React UI.

Build all three NuGet packages with `dotnet pack src/Avalonia.React/Avalonia.React.csproj -c Release`, `dotnet pack src/Avalonia.React.Vite/Avalonia.React.Vite.csproj -c Release`, and `dotnet pack src/Avalonia.React.SourceGenerator/Avalonia.React.SourceGenerator.csproj -c Release`.

## Platform notes

- Windows uses the installed Microsoft Edge WebView2 runtime.
- macOS/iOS use the system WKWebView.
- Linux needs WebKitGTK or WPE WebKit runtime libraries.
- Avalonia's WebView does not run on the Browser (WebAssembly) target.

See the [official Avalonia WebView documentation](https://docs.avaloniaui.net/docs/app-development/embedding-web-content) for platform setup details.
