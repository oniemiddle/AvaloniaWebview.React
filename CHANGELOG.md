# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Rearranged the `samples/react-app` UI into a responsive Semi Design dashboard: a sidebar and top bar,
  live runtime metric cards driven by the new `getRuntimeMetrics()` contract, a bridge playground with
  an event stream, and an about view. The page still renders labelled browser values when it runs
  outside the Avalonia host.
- `samples/ReactHost` now exposes a reflection-free `SampleHostApi`, uses slimmer native chrome that
  matches the React dashboard, and keeps the window title stable while reporting `react:clicked`
  receipts in the status line.

## [0.19.0] - 2026-10-18

### Added

- `samples/ReactHost` publishes as a self-contained Native AOT `win-x64` desktop app through
  `samples/ReactHost/Properties/PublishProfiles/win-x64-aot.pubxml` and `eng/publish-aot.ps1`.
  The publish profile keeps `PublishAot` project-local so it cannot flow into the
  `netstandard2.0` source generator, and the sample's host message payload is the generated
  `ReactAck` DTO so no reflection-based JSON metadata is needed at run time.
- `eng/verify-published-app.ps1` launches a published `ReactHost` build from an isolated copy and
  writes a JSON + PNG report (window title, WebView2 process tree, window capture, React
  readiness note).
  `eng/publish-aot.ps1 -CertificateThumbprint` optionally Authenticode-signs the published
  executable with `signtool` and verifies the signature.

### Fixed

- The sample's Vite build dedupes `react`/`react-dom`. `@avalonia-react/bridge` is a `file:`
  dependency (a junction to `client`) that carries its own React dev dependencies, so after
  `npm ci` in `client` the production bundle contained a second React copy and the page failed at
  startup with `Cannot read properties of null (reading 'useMemo')`.

## [0.18.0] - 2026-10-18

### Added

- React integration hooks under `@avalonia-react/bridge/react`: `useHostApi`,
  `useHostApiInvoke`, `useAvaloniaMessage`, `useHostTheme`, `useSignalReady`, and
  `useBridgeAvailable`. React is an optional peer dependency; the core bridge remains usable without it.
- Host-to-React request/response calls: `registerReactHandler`/`useReactHandler` on the client and
  `InvokeReactAsync`/`InvokeReactAsync<T>` on the host, with timeout, cancellation, errors, and the
  `ReactInvokeTimeout` setting.
- Zero-config trimmed/AOT JSON support: the source generator emits converters and a type-info
  resolver for generated DTOs, enums, lists, dictionaries, and nullable values, and installs it
  through `ReactJsonSerializerDefaults` at assembly load.
- Runtime availability preflight: `ReactWebViewAvailability.Check()` and `EnsureAvailable()` report
  the platform adapter, engine, runtime version, and install guidance without creating a WebView.
- Multi-window support: identical embedded-asset configurations share one reference-counted
  loopback server, and `ReactWebViewSession.Shared` keeps cookies/localStorage consistent across
  windows. `CreateEphemeral()` provides isolated sessions.
- Resource cleanup APIs: `ClearBrowserDataAsync`/`ClearCookiesAsync` for cookies, localStorage,
  sessionStorage, CacheStorage, IndexedDB, and service workers, plus
  `ReactWebViewResources.ClearExtractedAssetsAsync()` for assembly-embedded frontend temp files.
- Fixed embedded-asset origin support: `EmbeddedAssetOptions.Port` and
  `EmbeddedAssetsPort` on the controls keep the loopback origin stable across restarts for
  origin-scoped browser storage and fixed callback URLs.
- Native WebView E2E smoke harness covering ready, host APIs, host-to-React calls, fixed ports, and
  localStorage cleanup, plus a Windows CI job.

## [0.17.0] - 2026-10-18

### Changed

- Completed the project rename:
  - Source and test folders are now `src/AvaloniaWebview.React`, `src/AvaloniaWebview.React.Vite`,
    `src/AvaloniaWebview.React.SourceGenerator`, `tests/AvaloniaWebview.React.Tests`, and
    `tests/AvaloniaWebview.React.SourceGenerator.Tests`.
  - All project files, assembly names, root namespaces, `InternalsVisibleTo`, build-transitive
    targets/props paths, and the default embedded asset prefix use `AvaloniaWebview.React`.
  - The solution, CI/release workflows, package verification, documentation paths, sample imports,
    and XAML namespace mappings were updated to the new names.
- Consumers should update `using` directives, XAML `xmlns` mappings, project references, and assembly
  references to the new namespace/assembly name. NuGet package IDs were already renamed in 0.16.1.
- Package version is `0.17.0`; client version is `0.17.0`.

### Verification

- `dotnet build AvaloniaWebview.React.slnx -c Release` and all control/generator/client tests pass.
- No remaining project folders, files, assemblies, or namespaces use the previous identifier.

## [0.16.2] - 2026-10-18

### Changed

- Renamed the GitHub repository and project to `AvaloniaWebview.React`: the solution file is now
  `AvaloniaWebview.React.slnx`, and repository URLs, package metadata, and documentation use the new
  name. Until 0.17.0, C# namespaces and assemblies still used the previous name; the NuGet package
  IDs were renamed in 0.16.1.
- Updated the nuget.org Trusted Publishing policy for the renamed repository; this is the first
  publish under the new policy, which activates it permanently.
- Package version is `0.16.2`; client version is `0.16.2`.

## [0.16.1] - 2026-10-18

### Changed

- Renamed the NuGet package IDs to `AvaloniaWebview.React`, `AvaloniaWebview.React.Vite`, and
  `AvaloniaWebview.React.SourceGenerator`. The previous `Avalonia.*` IDs are blocked by nuget.org's
  reserved package ID prefix for AvaloniaUI, so 0.16.0 was never published.
- Package version is `0.16.1`; client version is `0.16.1`.

## [0.16.0] - 2026-10-18

### Added

- `@avalonia-react/bridge` is now publishable: version-aligned with the .NET packages, public npm
  metadata, `files`/`exports`/`publishConfig`, a packaged LICENSE, and a `verify:package` script that
  inspects the npm tarball.
- The bridge module is safe to import without a browser `window` (SSR/Node), and a test covers the
  package import.
- `RELEASING.md` documents version sync, required secrets, local verification, and package contents.
- GitHub Actions now validates all package versions, verifies NuGet/npm package contents, publishes
  the npm package when `NPM_TOKEN` is set, and uploads `.nupkg`, `.snupkg`, and `.tgz` artifacts.

### Changed

- NuGet package quality: deterministic CI builds, `.snupkg` symbols for `AvaloniaWebview.React`, release
  notes metadata, and root-level LICENSE files in all three packages. Analyzer/targets-only packages
  no longer attempt to produce empty symbol packages.
- NuGet publishing now uses Trusted Publishing (`NuGet/login@v1` with GitHub OIDC and the
  `NUGET_USER` secret) instead of a long-lived `NUGET_API_KEY`.
- `README.md` and the client README document installation from NuGet/npm and local repository usage.
- Package version is `0.16.0`; client version is `0.16.0`.

### Verification

- `npm --prefix client test`: 12 tests.
- Package content checks: `npm --prefix client run verify:package` and
  `eng/verify-nuget-packages.ps1`.
- A packed npm tarball was installed in a clean consumer, imported from Node, and type-checked.

## [0.15.0] - 2026-10-18

### Added

- Runtime availability watchdog: `ReactHostView.AdapterStartupTimeout` shows a clear error when the
  native WebView adapter does not initialize, instead of leaving the loading overlay visible forever.
- Host API origin gating: the application origin is trusted automatically, additional origins can be
  added to `TrustedApiOrigins`, and rejected calls raise `SecurityViolation` and return an error to
  React. `AllowedOrigins` remains navigation-only.
- Non-HTTP main-frame navigation (`javascript:`, `data:`, `file:`, and similar) is always blocked,
  including when `ExternalNavigationPolicy` is `Allow`; only HTTP(S) and `about:blank` are allowed.
- `MaxIncomingMessageCharacters` limits bridge message size and reports oversized messages through
  `SecurityViolation`.
- `ReactHostView` forwards `TrustedApiOrigins`, `MaxIncomingMessageCharacters`, and
  `SecurityViolation`; `ReactSecurityViolationEventArgs` carries the origin, API, and method.

### Changed

- Package version is `0.15.0`.

### Documentation

- Documented the origin boundary, navigation-scheme restrictions, message-size limit, and runtime
  availability watchdog.

## [0.14.0] - 2026-10-18

### Added

- Reflection-free host API registration: every generated API contract now emits a
  `{TypeScriptName}Bridge` helper with typed delegates, including inherited interface methods and
  cancellation-token forwarding. The sample uses it for its `HostApi`.
- Primitive and `JsonElement` host API arguments/results and host messages are read/written without
  reflection; string, number, boolean, `DateTimeOffset`, `Guid`, and `byte[]` values are handled
  directly.
- `ReactJsonSerializerDefaults` and `BridgeJsonSerializerOptions` let trimmed/AOT applications
  install a source-generated `JsonSerializerContext` for DTO/enum/collection payloads.
- `AvaloniaWebview.React` is marked `IsTrimmable` and `IsAotCompatible`; trim/AOT analyzers build clean,
  and a trimmed sample publish was verified to invoke the generated primitive API successfully.
- Source-generator CSProj treats `PublishTrimmed`/`SelfContained` as local so analyzer projects do
  not inherit application trimming settings.
- Generator tests now cover generated registration helpers, inheritance, cancellation, and direct
  primitive argument reads (27 tests).

### Changed

- Package version is `0.14.0`.

### Documentation

- Documented generated registration, the trimming/AOT JSON context requirement, and the manually
  configurable serializer defaults.

## [0.13.0] - 2026-10-18

### Added

- Multi-window and tab support through native reparenting:
  - `AvaloniaReactWebView.PreserveWebViewOnDetach` and `ReparentingTimeout` preserve the native
    WebView while the control is detached, then reparent it on reattach or release it after the
    timeout.
  - `ReactHostView` enables preservation by default and exposes `PreserveWebViewOnDetach`,
    `ReparentingTimeout`, `BeginReparenting`, and `BeginReparentingAsync`.
  - The embedded asset server's window subscription follows the control between windows.
- `BridgeJsonSerializerOptions` on `AvaloniaReactWebView` and `ReactHostView` lets applications
  supply source-generated JSON options for trimmed/AOT deployments.
- Host-to-React envelopes, theme messages, and API result envelopes are now constructed without
  reflection-based anonymous type serialization. `ReactBridgeProtocol` exposes the script builder
  and JSON string encoder for testing.
- New envelope/protocol tests (96 control tests total).

### Changed

- Package version is `0.13.0`.

### Documentation

- Documented multi-window/reparenting behavior and the remaining trimming/AOT constraints.

## [0.12.0] - 2026-10-18

### Added

- `ReactHostView` now forwards native WebView capabilities: `UserAgent`, `CanGoBack`/`CanGoForward`,
  `GoBack`/`GoForward`, `Stop`, `Navigate`, `ShowPrintUI`, `PrintToPdfStreamAsync` (both overloads),
  `TryGetCookieManager`, `TryGetCommandManager`, and `AdapterInfo`. The same capabilities were always
  available directly on `AvaloniaReactWebView` through its `NativeWebView` base class.
- `AvaloniaReactWebView.GetAdapterDiagnostics()` and `ReactHostView.AdapterDiagnostics` /
  `IsWebViewRuntimeInstalled` / `WebViewUnavailableReason` expose platform runtime diagnostics.
- `ReactHostView` detects an installed-but-unsupported or missing WebView runtime when the adapter is
  created and shows the reason in its error overlay, instead of leaving the loading state visible.
- Adapter diagnostic tests (5); the control suite now has 93 tests.

### Changed

- Package version is `0.12.0`.

### Documentation

- Documented native WebView passthrough, cookie/print/history examples, and runtime diagnostics.

## [0.11.0] - 2026-10-18

### Added

- The source generator now emits DTO classes for property-only interfaces and object
  `export type` aliases, with `[JsonPropertyName]` properties, nullable optional properties,
  and class inheritance for `extends`.
- Exported enums generate C# enums. String enums use `JsonStringEnumConverter<T>` with
  `JsonStringEnumMemberName` values; numeric enums keep explicit values. Unsupported enums report
  `AVR0004`.
- Referenced DTOs and enums are resolved across `.d.ts` files by unique type name (including across
  namespaces) instead of falling back to `JsonElement`; ambiguous names still report `AVR0002`.

### Changed

- Package version is `0.11.0`.
- The generator test suite now covers DTOs, enums, aliases, and cross-file resolution (24 tests).

## [0.10.0] - 2026-10-18

### Changed

- **Breaking:** removed the Windows-only WebView2 virtual-host asset transport and unified every
  platform on the loopback HTTP server. `EmbeddedAssetTransport`, `EmbeddedAssetOptions.Transport`,
  `EmbeddedAssetOptions.NativeHostName`, `AvaloniaReactWebView.EmbeddedAssetsTransport`, and
  `ReactHostView.EmbeddedAssetsTransport` no longer exist.
- `AvaloniaWebview.React` now targets only `net10.0`; the `net10.0-windows` target, the Windows-specific
  `NativeAssetHost`, and `AllowUnsafeBlocks` were removed. Windows applications continue to consume
  the `net10.0` library.
- `ReactHostView` enables Windows WebView2 offscreen composition automatically so its overlay
  renders above the WebView; its `EnableWindowsOffscreenComposition` property was removed. Direct
  `AvaloniaReactWebView` users can still configure the low-level property.
- Package version is `0.10.0`.

### Fixed

- The sample no longer contains platform-specific composition configuration.

### Documentation

- Replaced the native-transport documentation with a single cross-platform asset-serving
  description.

## [0.9.0] - 2026-10-18

### Added

- Trim/AOT annotations on the reflection-based API surface (`RegisterApi`, `PostMessageAsync`,
  `GetPayload`, and the `ReactHostView` forwarders) using `RequiresUnreferencedCode` and
  `RequiresDynamicCode`, so trim/AOT analyzers surface unsafe call sites.
- README guidance for trimming, Native AOT, and preserving generated interfaces and payload types.

### Changed

- Package version is `0.9.0`.

## [0.8.0] - 2026-10-18

### Added

- Assembly-embedded frontend assets for single-file and installer deployments. Set
  `AvaloniaReactEmbedAssetsInAssembly=true` and the MSBuild target embeds the built `dist` folder as
  manifest resources with the `AvaloniaWebview.React.Assets/` prefix before compilation.
- `UseAssemblyEmbeddedAssets` plus `AssemblyEmbeddedAssetOptions` (`Assembly`, `ResourcePrefix`,
  `ExtractionRoot`, `ForceExtraction`). Files are extracted once per assembly build to a versioned
  directory and served by the loopback server; `ReactHostView` forwards the method.
- `ReactSourceMode.AssemblyEmbeddedAssets`. In Release, `SourceMode="Auto"` chooses it when the
  embedding property is enabled, so no application code is needed.
- Extraction is thread-safe and cached; custom extraction roots and resource prefixes are supported.

### Changed

- Package version is `0.8.0`.
- Control tests now include extractor and source-mode coverage (87 tests).

### Documentation

- Documented the embedding property, extraction behavior, and the single-file deployment workflow.

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
  the registry credentials are configured, and creates a GitHub release.
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
- New `AvaloniaWebview.React.SourceGenerator.Tests` project (12 tests) covering parsing, diagnostics,
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