# Sample application

Two projects show the hybrid setup end to end: `samples/ReactHost` is the Avalonia desktop host and `samples/react-app` is the React + Vite frontend.

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

The sample is a small desktop "hybrid workbench" that exercises the 0.19 feature set end to end. It has three React views (运行概览, 桥接实验, 关于示例) arranged in a Semi Design sidebar layout, and the same page still renders labelled browser values when it runs outside Avalonia.

- **Live host metrics** — `FrontendApi.d.ts` declares `RuntimeMetrics` and `HostApi.getRuntimeMetrics()`. The dashboard polls it every two seconds and renders working set, GC heap, thread count, uptime, and the platform details card; `SampleHostApi` collects them with BCL APIs only.
- **Generated DTO/enum/list JSON** — `RuntimeSnapshot`, `ServiceHealth`, `RuntimeMetrics`, and `ReactAck` are generated from the same contract, so both the typed host API and the React-to-host acknowledgment stay AOT-safe without a handwritten `JsonSerializerContext`.
- **React hooks** — `App.tsx` uses `useSignalReady`, `useAvaloniaMessage`, `useHostTheme`, `useBridgeAvailable`, `useHostApi`, `useHostApiInvoke`, and `useReactHandler`; `main.tsx` only handles the pre-render theme so the initial paint matches the host. The dashboard adds one polling hook of its own on top of the generated proxy.
- **Host → React** — the native toolbar's "宿主 → React" button calls `InvokeReactAsync<string>("host:greet", ...)`; the React side handles it with `useReactHandler` and returns a string to the host status bar.
- **Bridge playground** — 桥接实验 combines the Hook call, the stable `useHostApi` proxy, a `postMessage` round trip with `react:ack`, and an event stream that records each path.
- **Multi-window** — the native toolbar's shared window creates another `MainWindow` using `ReactWebViewSession.Shared` and the shared loopback asset server; the localStorage counter is shared between those windows. The isolated window uses `ReactWebViewSession.CreateEphemeral()` and starts with its own storage.
- **Runtime preflight** — the host status line shows `ReactWebViewAvailability.Check()` output (engine, version, adapter), and the generated snapshot reports the same runtime information.
- **Resource cleanup** — "清理存储" calls `ClearBrowserDataAsync(ReactWebViewDataKind.BrowserStorage)` and reports cleared/failed groups.
- **Reparenting** — `ReactHostView` still preserves the native WebView across detach/attach by default.
- **Lazy module and component library** — 本地开发服务 is a `React.lazy` chunk that is downloaded only when the details are expanded; it renders a Semi Design table inside the dashboard.
- **Browser preview fallback** — when `window.invokeCSharpAction` is unavailable the dashboard falls back to browser-derived values, labels itself 浏览器预览, and switches to real .NET metrics as soon as the Avalonia host connects.

Vite hot reload continues to work inside the native WebView. The Avalonia host uses Semi.Avalonia so the native chrome, overlay, and React UI follow the same light/dark theme.
### Publish the sample with Native AOT

The Windows sample also publishes as a self-contained Native AOT executable. Besides the .NET 10 SDK, Native AOT needs the MSVC linker from Visual Studio's "Desktop development with C++" workload and the Windows 10/11 SDK libraries (`kernel32.lib` and friends):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/publish-aot.ps1
```

Output lands in `artifacts/publish/ReactHost-win-x64-aot` — the native `ReactHost.exe`, its native Skia/HarfBuzz/ANGLE dependencies, and the Vite bundle in `wwwroot/` (about 39 MB in total). The script asserts that the output really is Native AOT (no managed `ReactHost.dll`), then launches it through [`eng/verify-published-app.ps1`](../eng/verify-published-app.ps1), which writes a screenshot and a JSON report to `artifacts/aot-verify`; pass `-SkipRuntimeCheck` to stop after the build. The configuration lives in `samples/ReactHost/Properties/PublishProfiles/win-x64-aot.pubxml`, so the equivalent direct command is:

```sh
dotnet publish samples/ReactHost/ReactHost.csproj -p:PublishProfile=win-x64-aot -f net10.0-windows
```

`PublishAot` is set in the publish profile instead of being passed as a global property: global properties flow into project references, and the `netstandard2.0` source generator would then fail with `NETSDK1207`. Publishing also runs the Vite production build; pass `-p:AvaloniaReactBuildOnBuild=false` (or `-SkipFrontendBuild`) to reuse an existing `samples/react-app/dist`.

The publish is **unsigned** by default, so the first launch shows Windows' "unknown publisher" security warning (打开文件 - 安全警告); that is expected for a locally built executable, and the publish script prints its SHA-256 for comparison. If the file was downloaded or unpacked from an archive, clear the mark-of-the-web first with `Unblock-File .\ReactHost.exe`. For distribution, sign with an Authenticode code-signing certificate:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/publish-aot.ps1 -CertificateThumbprint <thumbprint>
```

The script finds `signtool.exe` in the Windows 10/11 SDK, signs with SHA-256 and an RFC 3161 timestamp (`-TimestampServer`; pass `none` to skip), and verifies the signature. A self-signed certificate only suppresses the warning on machines that trust it (Trusted Root + Trusted Publishers); public distribution needs a CA-issued certificate (for example Azure Trusted Signing) so the publisher name can be verified.

Payloads stay AOT-safe because the sample uses its generated contract: the host's `react:ack` message serializes a generated `ReactAck` DTO instead of an anonymous type, and the two annotated `PostMessageAsync`/`InvokeReactAsync` call sites suppress `IL2026`/`IL3050` because their payloads are primitives or generated DTOs that the generated JSON resolver covers. Adding an anonymous object or an unmapped custom payload to those calls would fail at run time once reflection-based serialization is trimmed away.

Build all three NuGet packages with:

```sh
dotnet pack src/AvaloniaWebview.React/AvaloniaWebview.React.csproj -c Release
dotnet pack src/AvaloniaWebview.React.Vite/AvaloniaWebview.React.Vite.csproj -c Release
dotnet pack src/AvaloniaWebview.React.SourceGenerator/AvaloniaWebview.React.SourceGenerator.csproj -c Release
```

Build the npm tarball with `cd client && npm pack --pack-destination ../artifacts/packages`. See [RELEASING.md](../RELEASING.md) for the complete release checklist.

## Screenshots

Overview in light and dark mode (browser preview values), plus the bridge playground:

| Light | Dark |
| --- | --- |
| ![Overview, light](images/sample-overview-light.png) | ![Overview, dark](images/sample-overview-dark.png) |

![Bridge playground](images/sample-playground-light.png)

