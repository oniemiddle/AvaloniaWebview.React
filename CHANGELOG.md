# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
