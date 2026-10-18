---
name: avalonia-webview-react-hybrid
description: Build, debug, and extend desktop apps that host a React frontend inside Avalonia with AvaloniaWebview.React (the Avalonia + NativeWebView hybrid toolkit; Avalonia + React 混合桌面应用). Use this skill whenever a task mentions Avalonia or .NET hosting React, AvaloniaWebview.React, NativeWebView, an Avalonia React bridge, a generated IHostApi, createHostApiProxy, signalReady, SourceMode, embedded React assets, host WebView trimming/AOT, or hybrid desktop navigation/security — even if the user does not name the package. Also use it when migrating an existing Avalonia + React app or diagnosing blank pages, missing WebView runtimes, blocked host APIs, or bridge errors.
---

# AvaloniaWebview.React hybrid apps

AvaloniaWebview.React hosts a React single-page app in Avalonia 12's native `NativeWebView` and connects the two sides with a JSON bridge and a TypeScript-to-C# contract generator.

The toolkit has four parts:

| Part | Purpose |
| --- | --- |
| `AvaloniaWebview.React` | The control, bridge, asset server, diagnostics, security, and lifecycle. |
| `AvaloniaWebview.React.Vite` | Optional Debug-time Vite server startup for desktop hosts. |
| `AvaloniaWebview.React.SourceGenerator` | Generates typed C# APIs, DTOs, enums, and registration helpers from `.d.ts` files. |
| `@avalonia-react/bridge` (npm) | The React-side typed client: messages, host API calls, theme, errors, cancellation. |

Current package line: `0.17.0`. The namespace, assemblies, project folders, and NuGet IDs are `AvaloniaWebview.React`; the public WebView control type is still named `AvaloniaReactWebView`. The npm scope remains `@avalonia-react/bridge`. Treat those names as intentional rather than typos.

## Choose the layer first

Most work starts in one of these places:

- **.NET host** — XAML, window lifecycle, source selection, registration, diagnostics.
- **React frontend** — `signalReady`, messages, typed host API proxy, theme.
- **Contract** — a shared `.d.ts` that generates C# interfaces/DTOs and drives the TypeScript proxy.
- **Build/deploy** — Vite in Debug, `wwwroot` or embedded assets in Release, trimming/AOT.

Read the relevant reference file after you understand the layer:

- `references/api-reference.md` — public C#/TypeScript surfaces and exact names.
- `references/recipes.md` — complete minimal host, contract, trimming, and multi-window examples.
- `references/troubleshooting.md` — symptom-driven diagnosis.

## Five-minute baseline

### Packages

```xml
<ItemGroup>
  <PackageReference Include="AvaloniaWebview.React" Version="0.17.0" />
  <PackageReference Include="AvaloniaWebview.React.SourceGenerator" Version="0.17.0" />
  <!-- Optional: Debug-time Vite startup -->
  <PackageReference Include="AvaloniaWebview.React.Vite" Version="0.17.0" />
</ItemGroup>
```

```sh
npm install @avalonia-react/bridge
```

The frontend also needs React and its bundler. Windows hosts must embed an application manifest declaring Windows 10/11 compatibility before `NativeWebView` can create its native child window; the sample uses `app.manifest` with `<ApplicationManifest>`.

### Host view

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:react="using:AvaloniaWebview.React">
  <react:ReactHostView x:Name="ReactHost"
                       SourceMode="Auto"
                       WaitForBridgeReady="True"
                       BridgeReadyTimeout="0:0:10" />
</Window>
```

`ReactHostView` supplies a themed loading/error overlay, forwards the common WebView API, and sets the Windows composition needed for the overlay. Use `ReactHost.WebView` when you need the raw `AvaloniaReactWebView`.

### React entry

```tsx
import { signalReady } from "@avalonia-react/bridge";

// After the React tree mounts and subscriptions are in place.
signalReady({ frontend: "React" });
```

Call `signalReady()` once per page. It returns `false` outside Avalonia, so the same app can run in a normal browser during development.

### Minimal contract

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
}
```

```csharp
using AvaloniaWebview.React;
using MyApp.Frontend;

public sealed class HostApi : IHostApi
{
    public Task<string> GetGreetingAsync(string name) =>
        Task.FromResult($"Hello {name}");
}

// Preferred: reflection-free, trimming-friendly registration.
HostApiBridge.Register(ReactHost.WebView, new HostApi());
```

```ts
import { createHostApiProxy } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
```

The generated helper name is `{TypeScriptName}Bridge`; for `HostApi` it is `HostApiBridge`. It lives in the contract's `@namespace`. Pass the raw WebView (`ReactHost.WebView`), not the `ReactHostView`.

## Mental model

```text
React  --JSON {type,payload}--> invokeCSharpAction --+
                                                      |
                                        AvaloniaReactWebView
                                                      |
                                  registered API handlers (delegates)
                                                      |
                                            .NET implementation

React <-- CustomEvent "avalonia-react:message" -- PostMessageAsync
```

- Reserved messages include `avalonia:ready`, `avalonia:invoke`, `avalonia:invoke:result`, `avalonia:invoke:cancel`, `theme`, `theme:request`, and `avalonia:error`. Application messages must use different names.
- The client uses request IDs for host API calls, resolves results through `avalonia:invoke:result`, and sends cancellation when a call times out, is aborted, or the page unloads.
- The host never serializes a method name from a C# identifier directly; generated `[ReactApiContract]` / `[ReactApiMethod]` metadata preserves the TypeScript names.

## Source selection

`ReactHostView` defaults to `SourceMode="Manual"`, so set `SourceMode="Auto"` in XAML for normal hybrid apps.

| Mode | What happens |
| --- | --- |
| `Auto` | Uses build metadata: Debug → Vite dev server, Release → embedded assets; an explicit `DevServerUrl` wins. |
| `DevServer` | Navigates to `DevServerUrl` (default `http://127.0.0.1:5173/`). |
| `EmbeddedAssets` | Serves `wwwroot` from a loopback HTTP server. |
| `AssemblyEmbeddedAssets` | Extracts frontend files embedded in the host assembly, then serves them on loopback. |
| `Manual` | The application assigns `Source` itself. |

Release builds should use embedded assets rather than depending on Vite. The main package copies the frontend build output into `wwwroot`; `AvaloniaReactEmbedAssetsInAssembly=true` embeds it into the assembly instead. The Vite package is optional and only handles Debug auto-start.

If the app shows `127.0.0.1:5173` connection-refused in Release, the source mode likely resolved to `DevServer` or the embedded assets are missing. Check `SourceMode`, `AvaloniaReactDefaultSourceMode`, `AvaloniaReactEmbeddedAssets`, and whether `wwwroot/index.html` exists in the published output.

## Contract-first host APIs

1. Put the contract in a `.d.ts` file shared with the React project.
2. Install the source-generator package. It automatically picks up `.d.ts` files under the project; for a contract outside the project, add it explicitly:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />
   </ItemGroup>
   ```

3. Implement the generated interface. Methods with `Promise<T>` become `Task<T>` (or `Task` for `Promise<void>`); `number` becomes `double`; the generator appends `Async` to method names when needed.
4. Register with `HostApiBridge.Register(webView, implementation)`. This avoids reflection and includes inherited interface methods.
5. Call it from React with `createHostApiProxy<HostApi>("HostApi")` or `invokeHostApi<T>("HostApi", "getGreeting", ...)`.

Keep the contract as the single source of truth. If the TypeScript API name and the C# constant drift, React receives an “API method is not registered” error. The generated `[ReactApiContract]` name is the authority.

The reflection-based `RegisterApi<TApi>(implementation)` overload also exists, but it reflects over the interface and implementation and is annotated for trimming/AOT. Prefer the generated helper.

### DTOs, enums, and aliases

- Property-only interfaces and object `export type` aliases generate partial C# DTO classes. Properties carry `[JsonPropertyName]`; optional properties become nullable.
- `export enum` generates a C# enum. String enums use `JsonStringEnumConverter<T>` and `JsonStringEnumMemberName`; numeric enums keep explicit values.
- DTOs and enums declared in other `.d.ts` files are resolved by unique name across namespaces. Ambiguous names fall back to `JsonElement` (`AVR0002`).
- Interfaces with only properties become DTOs; interfaces with methods become API contracts. Do not mix methods and properties in one interface — properties on an API contract are ignored with `AVR0001`.
- Generic interfaces are skipped (`AVR0003`); unsupported enums are skipped (`AVR0004`); unsupported members report `AVR0001`.

For larger shapes, see `references/recipes.md`.

## Messages and lifecycle

```ts
import { postMessage, requestTheme, subscribe } from "@avalonia-react/bridge";

const unsubscribe = subscribe<{ at: string }>("account:changed", payload => {
  console.log(payload.at);
});

postMessage("react:clicked", { count: 1 });
requestTheme(); // ask the host to send the current theme immediately
unsubscribe();
```

- `signalReady()` tells the host the page is mounted. With `WaitForBridgeReady="True"`, the host overlay waits for this (bounded by `BridgeReadyTimeout`).
- `subscribe` returns an unsubscribe function and is safe to import in Node/SSR.
- Host → React messages use `PostMessageAsync(type, payload)`; messages sent before navigation completes are queued (`MaxQueuedBridgeMessages`, default 1024).
- Host-side `MessageReceived` only sees application messages, not reserved bridge traffic. Use `GetPayload<T>()` for typed payloads.
- `BridgeReady`, `LoadStateChanged`, `BridgeError`, and `JavaScriptError` are the main host observability events.
- `BridgeError` reports exceptions thrown by application `MessageReceived` handlers instead of escaping the WebView event loop.

## Security boundaries

Host APIs are origin-gated by default:

- The initial application HTTP origin becomes trusted automatically.
- Add additional trusted page origins to `TrustedApiOrigins`.
- `AllowedOrigins` grants **navigation**, not host API access. Do not use it as an API allowlist.
- Calls from other origins are rejected with an error to React and a `SecurityViolation` event.
- Main-frame navigation to `javascript:`, `data:`, `file:`, and other non-HTTP schemes is always cancelled, even with `ExternalNavigationPolicy="Allow"`. HTTP(S) and `about:blank` are allowed.
- `MaxIncomingMessageCharacters` (default 4,000,000) drops oversized bridge messages and reports `SecurityViolation`.
- `ExternalNavigationPolicy` defaults to `OpenInSystemBrowser`; `Block` ignores external navigation; `Allow` navigates the WebView but does not grant API access.

Do not weaken these defaults to make a quick integration work. If an external auth or checkout origin needs to call host APIs, add it explicitly to `TrustedApiOrigins` and explain the trust decision.

## Diagnostics and recovery

`ReactHostView` shows a runtime error in its overlay when the adapter cannot initialize:

- `AdapterStartupTimeout` (10 seconds) waits for the native adapter before showing a runtime-availability error.
- `AdapterDiagnostics` returns `DetailedWebViewAdapterInfo` when the platform exposes it.
- `IsWebViewRuntimeInstalled` is `true`/`false`/`null` (unknown).
- `WebViewUnavailableReason` gives the platform reason.
- `LoadState`, `LoadError`, `IsPageLoaded`, and `IsBridgeReady` describe the page lifecycle.

Typical runtime causes: missing WebView2 on Windows, missing WKWebView/system configuration on macOS, or missing WebKitGTK/WPE libraries on Linux. The browser runtime is provided by the OS; AvaloniaWebview.React does not bundle Chromium.

## Multi-window and reparenting

`ReactHostView` keeps the native WebView alive when the control is detached:

- `PreserveWebViewOnDetach` is enabled by default on `ReactHostView` (disabled on a raw `AvaloniaReactWebView`).
- Reattaching reparents the native control and preserves page/history/cookies/bridge state.
- If it is not reattached within `ReparentingTimeout` (10 seconds), the native control is released.
- `BeginReparenting()` / `BeginReparentingAsync()` are available for manual detach/attach sequences.

The host window's asset-server subscription follows the control. Do not create a second WebView just to move a tab between windows unless the product actually needs two independent sessions.

## Trimming and Native AOT

Use the generated `HostApiBridge.Register` helper, not the reflection overload.

Primitive and `JsonElement` arguments/results and host messages are written without reflection. DTOs, enums, collections, and custom message payloads need source-generated JSON metadata. For trimmed/AOT apps, define a context covering every root payload type and install it before the WebView is created:

```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(UserProfile))]
[JsonSerializable(typeof(IReadOnlyList<UserProfile>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;
// Or per view:
// reactHost.BridgeJsonSerializerOptions = AppJsonContext.Default.Options;
```

Symptoms of missing metadata after trimming: reflection-based serialization is disabled, or an API call fails while the same call works untrimmed. Add the missing root type to the context; do not switch back to reflection.

## Debugging playbook

Start with `references/troubleshooting.md` for commands and exact checks. Quick triage:

| Symptom | First checks |
| --- | --- |
| Blank/loading overlay forever | Is the adapter initialized? `AdapterDiagnostics`/`IsWebViewRuntimeInstalled`; `AdapterStartupTimeout`. |
| `127.0.0.1:5173` connection refused | Debug Vite server not running, or Release resolved to `DevServer` without assets. |
| Page loads but React never becomes ready | Did the entry module call `signalReady()` after mount? Is `WaitForBridgeReady` enabled intentionally? |
| “API method is not registered” | Contract namespace/API name, helper registration, generated assembly name, and React proxy string. |
| Host API rejected with origin error | `TrustedApiOrigins`; `AllowedOrigins` only covers navigation. |
| XAML cannot find `ReactHostView` | Use `xmlns:react="using:AvaloniaWebview.React"` and `react:ReactHostView`. |
| Windows host window is blank | Missing Windows 10/11 compatibility manifest. |
| Trimmed app cannot call a DTO API | Missing `JsonSerializerContext` for the DTO/collection. |
| Navigation leaves the app | `ExternalNavigationPolicy`, `AllowedOrigins`, and the non-HTTP navigation rule. |
| Page state lost after moving a tab | Use a `ReactHostView` in that window; it preserves the WebView by default. |

## Do not

- Do not use `AllowedOrigins` as a host API trust list; it is for navigation.
- Do not call host APIs from an untrusted external origin or disable origin checks to “make it work”.
- Do not set `Source` manually while also expecting `SourceMode="Auto"`; pick `Manual` or `Auto`.
- Do not use the reflection `RegisterApi<TApi>` path in a trimmed/AOT app unless you have preserved the implementation and JSON metadata.
- Do not reuse reserved `avalonia:*` names for application messages.
- Do not assume the old namespace/assembly names: this project renamed them to `AvaloniaWebview.React`; update `using`, XAML namespace mappings, project references, and package IDs accordingly.
- Do not forget the Windows manifest; a WebView that never appears is often a manifest problem, not a React problem.

## Reference files

- `references/api-reference.md` — public C# and TypeScript API surface, events, options, bridge protocol.
- `references/recipes.md` — complete code for a minimal app, typed API with DTOs, trimming, multi-window, and manual asset modes.
- `references/troubleshooting.md` — symptom-by-symptom diagnostics and common mistakes.
