# Building and testing

Build the solution, run the test suites, exercise the native WebView smoke test, and release new packages.

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

### Native WebView smoke test

Unit tests do not execute a real WebView. The E2E harness starts Avalonia, navigates a native WebView to an asset-server page with a known fixed port, and verifies the full bridge path: ready handshake, host API call, host-to-React call, and localStorage cleanup:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/run-webview-e2e.ps1 -Configuration Release -TargetFramework net10.0-windows
```

It writes `artifacts/webview-e2e-result.json` and exits non-zero if any check fails. The Windows CI job runs this smoke test and uploads the result artifact. The project is [`tests/AvaloniaWebview.React.WebViewE2E`](../tests/AvaloniaWebview.React.WebViewE2E/AvaloniaWebview.React.WebViewE2E.csproj).

## Release

Set `AvaloniaReactVersion` in `Directory.Build.props`, update `client/package.json` and `client/package-lock.json`, commit, and push a matching `vX.Y.Z` tag. The workflow validates all versions, runs tests, verifies package contents, publishes the NuGet packages (including `.snupkg` symbols) using NuGet Trusted Publishing when `NUGET_USER` and the nuget.org policy are configured, publishes `@avalonia-react/bridge` when `NPM_TOKEN` is configured, and creates a GitHub release with the `.nupkg`, `.snupkg`, and `.tgz` files. See [RELEASING.md](../RELEASING.md) for details.

