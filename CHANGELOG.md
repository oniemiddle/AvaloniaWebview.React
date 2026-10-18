# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.7.0] - 2026-10-18

### Added

- Host API cancellation. Setting `AvaloniaReactGenerateCancellationToken=true` appends a trailing
  `CancellationToken` to every generated interface method; the runtime passes a token that is
  canceled when React aborts or times out a call, when the page navigates or fails to load, and on
  shutdown. The option is off by default.
- The bridge client sends the reserved `avalonia:invoke:cancel` message when a call times out, is
  aborted, or the page unloads; the host cancels the matching invocation.
- The runtime tracks pending invocations per request ID and cancels them when navigation starts or
  fails and when the WebView shuts down. API timeouts now also cancel the implementation token.

### Changed

- Package version is `0.7.0`.
- The generator test project includes analyzer option plumbing (14 tests); control tests cover token
  injection and now total 82.

### Documentation

- Documented the cancellation option, the reserved cancel message, and the generated signature.

## [0.6.0] - 2026-10-18

### Added

- `AvaloniaReactWebView.Shutdown()` and `ReactHostView.Shutdown()`/`Dispose()`. Shutdown is
  idempotent, stops the WebView, disposes the embedded asset transport, fails queued messages, and
  clears registered APIs; bridge operations then throw `ObjectDisposedException`. The host window
  closing path now uses the same shutdown routine.
- The sample multi-targets `net10.0-windows` and `net10.0`, so it builds and runs on macOS/Linux
  (with the platform WebKit runtime) as well as Windows. CI builds the cross-platform target on
  Linux.
- A tag-driven release workflow (`.github/workflows/release.yml`) that validates the tag against
  `AvaloniaReactVersion`, runs all test suites, packs the three packages, publishes to NuGet when
  `NUGET_API_KEY` is configured, and creates a GitHub release.
- Repository/project URL metadata for all packages, LICENSE in the Vite package, and npm metadata
  for the bridge client (description, license, repository, engines, keywords).

### Changed

- Package version is `0.6.0`.
- README documents explicit target frameworks for running the multi-targeted sample.

### Fixed

- The frontend copy/verify MSBuild targets no longer run without a `TargetDir`, which previously
  copied `dist` into the project root when a multi-targeted host project was built.

## [0.5.0] - 2026-10-18

### Added

- `ReactHostView`, a Grid-based host that composes `AvaloniaReactWebView` with a themed
  loading/error overlay. It forwards the common configuration properties, events, and methods
  (`SourceMode`, `RegisterApi`, `PostMessageAsync`, `Reload`, `MessageReceived`, `BridgeReady`,
  `JavaScriptError`, and others) and exposes `WebView` for advanced use.
- `WaitForBridgeReady` and `BridgeReadyTimeout` control when the overlay hides; the default
  behavior hides on navigation completion, so applications without `signalReady()` are unaffected.
- Customizable overlay text (`LoadingText`, `RetryText`, `ErrorTextFormat`) and
  `OverlayBackground`; the default overlay background follows the light/dark theme. The built-in
  retry button reloads the page.
- The sample now uses `ReactHostView`; its window XAML and code-behind only register the generated
  API and handle demo messages.

### Changed

- Package version is `0.5.0`.
- The sample no longer contains any overlay state machine, retry handler, or WebView environment
  configuration.

### Tests

- Added overlay policy tests (8 cases); the control test suite now has 81 tests.

## [0.4.0] - 2026-10-18

### Added

- The loopback asset server now supports HTTP/1.1 keep-alive connections, bounded concurrent
  connections, and a configurable idle timeout (`EnableKeepAlive`, `IdleConnectionTimeout`,
  `MaxRequestsPerConnection`, `MaxConnections`).
- Conditional requests: `Last-Modified`/`If-Modified-Since`, `If-Range`, `If-None-Match` lists,
  and wildcard ETags, in addition to the existing `ETag` support.
- SPA fallback now covers dotted client-side routes when the browser sends
  `Accept: text/html`, while missing asset requests still return 404.
- Additional MIME types for `.webmanifest`, `.pdf`, `.zip`, `.csv`, and `.md`.
- Host-side `ApiInvokeTimeout` (30 seconds by default) so a slow API implementation returns a
  timeout error to React instead of leaving the promise pending.
- `JavaScriptError` event and the reserved `avalonia:error` message. The bridge client forwards
  window errors and unhandled promise rejections with stack, source, line, and column, deduplicated
  and capped at 50 reports.
- A Node-based test suite for `@avalonia-react/bridge` (11 tests) covering messaging, the typed
  proxy, timeout/abort behavior, page unload, and error forwarding.

### Changed

- Package version is `0.4.0`.
- CI now builds and tests the bridge client in addition to the two .NET test projects.

### Fixed

- The JavaScript error parser no longer throws on mistyped non-numeric fields.

## [0.3.0] - 2026-10-18

### Added

- The source generator now parses multi-line method signatures, members separated only by newlines,
  interface inheritance (`extends`),
  string/number/boolean literal unions, nullable and optional parameters, `Record<string, T>`,
  `Date`, `ArrayBuffer`/`Uint8Array`, and nested array/generic forms.
- Generated interfaces carry `[ReactApiContract]` and `[ReactApiMethod]` attributes, so the bridge
  uses the exact TypeScript API and method names instead of deriving them from C# identifiers.
- Generated interfaces are `partial`, and generated hint names include the source file and an index,
  so multiple contracts with the same interface name no longer collide.
- New source-generator diagnostics: `AVR0001` for unsupported members, `AVR0002` for types that fall
  back to `JsonElement`, and `AVR0003` for generic interfaces.
- The runtime now binds missing trailing optional arguments to their generated defaults and supports
  methods whose TypeScript name does not match the C# name.
- The bridge client adds `createHostApiProxy<T>()`, which turns the TypeScript contract into a typed
  proxy and removes hand-written API/method strings from React code.
- New `Avalonia.React.SourceGenerator.Tests` project (12 tests) covering parsing, diagnostics,
  generated source shape, and compilation of the generated code.
- The sample contract is now a single file at `samples/react-app/src/FrontendApi.d.ts`, shared by the
  source generator and the React app's typed proxy.

### Changed

- Package version is `0.3.0`.
- The sample uses `hostApi.getGreeting(...)` through `createHostApiProxy` instead of passing method
  names as strings.
- CI runs both test projects.

### Documentation

- Documented the expanded TypeScript surface, diagnostics, generated attributes, typed proxy, and
  the single-source-of-truth contract workflow.

## [0.2.0] - 2026-10-18

### Added

- `ReactLoadState` (`Idle`, `Loading`, `Loaded`, `Failed`), `LoadStateChanged`, `LoadError`, and
  `Reload()` on `AvaloniaReactWebView`, so host applications no longer have to track navigation
  events themselves.
- A host-to-React message queue: `PostMessageAsync` calls made before the page loads are delivered
  after a successful navigation, and fail with an exception when navigation fails. The queue is
  bounded by `MaxQueuedBridgeMessages`.
- The reserved `avalonia:ready` handshake. The bridge client exposes `signalReady()` and
  `requestTheme()`, and the control raises `BridgeReady` and exposes `IsBridgeReady`.
- `SourceMode="Auto"` and `DevServerUrl`/`EmbeddedAssetsRoot`/`EmbeddedAssetsTransport` properties.
  The MSBuild targets now emit `AvaloniaReact.SourceMode`, `AvaloniaReact.DevServerUrl`, and
  `AvaloniaReact.EnableDevTools` assembly metadata for the host executable, removing Debug/Release
  `#if` blocks and environment-variable boilerplate from applications.
- `EnableWindowsOffscreenComposition` and `EnableDevTools` properties. Dev tools default to enabled
  in Debug builds through build metadata.
- External navigation handling: the initial origin is tracked, `ExternalNavigationPolicy`
  (default `OpenInSystemBrowser`) opens external targets in the browser, and `NewWindowRequested`
  navigates same-origin requests in the current view instead of opening a native popup.
- `AllowedOrigins`, `UnregisterApi`, duplicate API registration detection, `BridgeError` for
  exceptions thrown by application message handlers, and explicit-interface API implementations.
- Client bridge: `invokeHostApiWithOptions` with timeout and `AbortSignal` support, page-unload
  rejection for pending calls, `isBridgeAvailable()`, and shared reserved message constants.
- 43 new tests covering bridge parsing, source mode resolution, origin policy, and the message
  queue (55 tests total).
- GitHub Actions CI workflow for build, test, and pack on Windows and Linux.

### Changed

- Package versions are centralized in `Directory.Build.props` (`AvaloniaReactVersion`).
- The main package now includes the LICENSE file and repository/project URLs.
- The sample uses `SourceMode="Auto"`, `signalReady()`, and the new load-state API. Its code-behind
  no longer contains Debug/Release source selection or WebView2 environment plumbing.
- The sample demonstrates a host acknowledgment message (`react:ack`) for React-to-Avalonia
  messages.
- The test suite creates asset fixtures under the test output directory instead of the system temp
  directory, avoiding sandboxed temp folders.

### Fixed

- `UseEmbeddedAssets` now leaves the control in a retryable state when starting the asset transport
  throws.
- API invocation resolves explicit interface implementations and validates duplicate registrations.
- Message handler exceptions no longer escape the WebView event loop unobserved.

### Documentation

- Documented `SourceMode`, load state, message queueing, reserved bridge messages, external
  navigation policy, dev tools, and offscreen composition.
- Corrected platform notes and removed stale environment-variable guidance from the sample.
