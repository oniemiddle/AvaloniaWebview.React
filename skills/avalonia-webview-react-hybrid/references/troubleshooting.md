# Troubleshooting

Use this order: establish whether the page loaded, whether the bridge is ready, then whether the specific message/API was accepted.

## 1. Capture state

```csharp
Console.WriteLine($"load={ReactHost.LoadState} error={ReactHost.LoadError} " +
                  $"page={ReactHost.IsPageLoaded} bridge={ReactHost.IsBridgeReady} " +
                  $"overlay={ReactHost.IsOverlayVisible}");

if (ReactHost.AdapterDiagnostics is { } adapter)
{
    Console.WriteLine($"adapter installed={adapter.IsInstalled} supported={adapter.IsSupported} " +
                      $"reason={adapter.UnavailableReason}");
}
```

Subscribe to all four observability events while diagnosing:

```csharp
ReactHost.LoadStateChanged += (_, e) => Console.WriteLine($"state: {e.State} {ReactHost.LoadError}");
ReactHost.BridgeReady += (_, e) => Console.WriteLine($"ready: {e.Payload}");
ReactHost.BridgeError += (_, e) => Console.WriteLine($"bridge error: {e.Exception}");
ReactHost.JavaScriptError += (_, e) => Console.WriteLine($"js error: {e.Message}");
ReactHost.SecurityViolation += (_, e) => Console.WriteLine($"security: {e.Reason} origin={e.Origin}");
```

## 2. Blank page or overlay never disappears

Check in this order:

1. **Is the native runtime available?** `IsWebViewRuntimeInstalled`, `AdapterDiagnostics`, and `WebViewUnavailableReason`. `ReactHostView` sets `AdapterStartupTimeout` (10 seconds); if the adapter never initializes, the overlay should show the runtime reason.
2. **Is the page actually navigating?** `LoadState` should move to `Loading`, then `Loaded` or `Failed`; `LoadError` should explain a failed navigation.
3. **Did React call `signalReady()`?** With `WaitForBridgeReady="True"`, the overlay waits for `avalonia:ready`. Without it, the overlay hides after navigation.
4. **Is the source correct?** In Debug, `Auto` expects the Vite server at `http://127.0.0.1:5173/`. In Release, `Auto` expects `wwwroot` or assembly-embedded assets. A connection-refused page showing `127.0.0.1:5173` means the app selected the dev server.
5. **Windows host blank child window?** Add the Windows 10/11 compatibility manifest (`supportedOS` GUID). This is a common “WebView never appears” cause.

Useful checks:

```sh
# Is Vite listening?
curl http://127.0.0.1:5173/

# Does the published host contain assets?
ls artifacts/publish/wwwroot/index.html
```

If missing, run `npm run build` (or `npm --prefix react-app run build`), then rebuild/publish. For assembly embedding, verify `AvaloniaReactEmbedAssetsInAssembly=true` and `AssemblyEmbeddedAssets` mode.

## 3. Page loaded, but host APIs fail

### “API method is not registered”

- Did you call the generated helper with the **raw WebView**?

  ```csharp
  HostApiBridge.Register(ReactHost.WebView, new HostApiImplementation());
  ```

  `HostApiBridge.Register(ReactHost, ...)` is not valid; the helper takes `AvaloniaReactWebView`.
- Is the helper in the contract's `@namespace`? `HostApiBridge` is generated next to `IHostApi`.
- Does the React proxy use the TypeScript API name from `[ReactApiContract]`? For `export interface HostApi`, use `"HostApi"`.
- Did registration happen before React invokes it? Register in the window constructor/startup, before the page loads.
- If using the reflection overload, confirm `TApi` is the generated interface and the implementation actually implements every method.

### “Host API calls are not allowed from this page origin”

This is the security boundary, not a bridge bug.

- The app origin is trusted after the initial HTTP navigation.
- Add the additional origin explicitly:

  ```csharp
  ReactHost.TrustedApiOrigins.Add(new Uri("https://auth.example.com"));
  ```

- `AllowedOrigins` is only for navigation. Adding an origin there does not grant API access.
- Check the current page: `Source`/navigation may have moved to an external origin. If that origin should have API access, add it to `TrustedApiOrigins`; otherwise keep it untrusted.

### API times out or rejects

- `ApiInvokeTimeout` defaults to 30 seconds. A long implementation can set it higher or `TimeSpan.Zero` to disable.
- React calls time out/abort through `invokeHostApiWithOptions({ timeoutMs, signal })`; the client sends cancellation.
- Implement a trailing `CancellationToken` parameter to observe cancellation; enable generated tokens with:

  ```xml
  <AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>
  ```

### Host-to-React call hangs or fails

- Confirm React called `registerReactHandler("name", ...)` (or `useReactHandler`) before `InvokeReactAsync` runs.
- A missing handler returns an error containing `No React handler registered`.
- `InvokeReactAsync` times out after `ReactInvokeTimeout` (10s default); pass an explicit timeout or increase the property.
- A handler error propagates as `InvalidOperationException` with the React error message.
- On timeout/cancellation, .NET sends `avalonia:react:cancel`; honor the handler's `AbortSignal` to stop work.

### API works untrimmed, fails trimmed

- Use `HostApiBridge.Register(...)`, not the reflection `RegisterApi<TApi>` overload.
- DTOs/enums/collections need JSON metadata. Add a source-generated context and install it before WebView creation:

  ```csharp
  [JsonSerializable(typeof(UserProfile))]
  [JsonSerializable(typeof(IReadOnlyList<UserProfile>))]
  internal partial class AppJsonContext : JsonSerializerContext { }

  ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;
  ```

- Add every root payload type used by host APIs or `PostMessageAsync`; nested types are discovered from roots.
- Do not suppress the trimming warning by ignoring it; the call can still fail at runtime.

## 4. Messages or theme do not work

- React must subscribe before the host sends: use `subscribe(type, listener)` and keep the returned unsubscribe for cleanup.
- `postMessage(type, payload)` throws outside Avalonia; guard with `isBridgeAvailable()` if the same component runs in a browser/SSR.
- Application messages must not reuse `avalonia:ready`, `avalonia:invoke`, `avalonia:invoke:result`, `avalonia:invoke:cancel`, `theme`, `theme:request`, or `avalonia:error`.
- The host only raises `MessageReceived` for application messages; reserved protocol traffic is handled internally.
- `requestTheme()` is the reliable way to get the initial theme before the first navigation message. The host also sends `theme` after each successful navigation and when the actual theme changes.
- Host messages sent before navigation completes are queued (`MaxQueuedBridgeMessages`, default 1024).

## 5. Navigation, popups, and security

- `ExternalNavigationPolicy` defaults to `OpenInSystemBrowser`; external HTTP navigations are cancelled and opened externally.
- `Block` cancels them silently; `Allow` navigates the WebView but does **not** grant host API access.
- Add extra navigation origins to `AllowedOrigins`; add API-trust origins separately to `TrustedApiOrigins`.
- Non-HTTP main-frame navigation (`javascript:`, `data:`, `file:`, etc.) is always rejected, even with `Allow`.
- `NewWindowRequested` follows the external navigation policy; same-origin requests navigate the current view.
- Oversized incoming messages exceed `MaxIncomingMessageCharacters` (default 4,000,000) and raise `SecurityViolation`.

## 6. State is lost after moving a tab between windows

- Keep the same `ReactHostView` control and reattach it; do not create a new WebView.
- `ReactHostView.PreserveWebViewOnDetach` is already `true`; raw `AvaloniaReactWebView` defaults to `false`.
- Reattach within `ReparentingTimeout` (default 10 seconds).
- For a manual sequence, wrap the move in `using (host.BeginReparenting()) { ... }`.
- A control can have only one parent. Detach from the old container before attaching to the new one.
- The asset server's window subscription follows the control, so the loopback server continues across the move.

## 7. Generator and XAML errors

- XAML namespace: `xmlns:react="using:AvaloniaWebview.React"`.
- The WebView type remains `AvaloniaReactWebView`; the host type is `ReactHostView`.
- The project renamed its namespace/assemblies to `AvaloniaWebview.React`; update old `using` directives, XAML mappings, project references, and package IDs. NuGet package IDs are `AvaloniaWebview.React`, `AvaloniaWebview.React.Vite`, and `AvaloniaWebview.React.SourceGenerator`.
- `AVR0001` — unsupported member. Split mixed method/property interfaces.
- `AVR0002` — a type mapped to `JsonElement`. Use a supported shape or handle `JsonElement` explicitly.
- `AVR0003` — generic interface skipped. Use a concrete contract.
- `AVR0004` — enum skipped. Use all-string or all-numeric values and explicit integer literals.
- If the generated interface is not visible, confirm the `.d.ts` file is under the project or listed in `<AdditionalFiles>` and that the generator package is installed.

## 8. Version and rename checks

The current namespace/assembly is `AvaloniaWebview.React`; NuGet versions are `0.17.x`. The public WebView type is still `AvaloniaReactWebView`, and the npm client is still `@avalonia-react/bridge`.

If a snippet, article, or generated file references a different namespace/assembly, update it to `AvaloniaWebview.React`. If a package restore fails for an old package ID, switch to the `AvaloniaWebview.React*` package IDs.
