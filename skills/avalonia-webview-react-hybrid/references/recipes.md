# Recipes

These examples target AvaloniaWebview.React `0.17.x` and .NET 10.

## Minimal Windows host with a typed API

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaReactProjectDirectory>$(MSBuildProjectDirectory)\..\react-app</AvaloniaReactProjectDirectory>
    <AvaloniaReactEmbeddedAssets>true</AvaloniaReactEmbeddedAssets>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia.Desktop" Version="12.1.0" />
    <PackageReference Include="AvaloniaWebview.React" Version="0.18.0" />
    <PackageReference Include="AvaloniaWebview.React.SourceGenerator" Version="0.18.0" />
    <PackageReference Include="AvaloniaWebview.React.Vite" Version="0.18.0" />
    <AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />
  </ItemGroup>
</Project>
```

The source-generator package automatically adds `.d.ts` files under the project; the explicit `AdditionalFiles` entry is for contracts outside the .NET project directory. `AvaloniaReactEmbeddedAssets=true` makes the build warn if `wwwroot/index.html` is missing in Release.

### `app.manifest`

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

Without this Windows 10/11 compatibility declaration, WebView2 may create no visible child window.

### Window

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:react="using:AvaloniaWebview.React"
        x:Class="MyApp.MainWindow"
        Width="1100" Height="760">
  <react:ReactHostView x:Name="ReactHost"
                       SourceMode="Auto"
                       WaitForBridgeReady="True"
                       BridgeReadyTimeout="0:0:10" />
</Window>
```

### Contract

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
}
```

### .NET implementation and registration

```csharp
using Avalonia.Controls;
using MyApp.Frontend;

namespace MyApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        HostApiBridge.Register(ReactHost.WebView, new HostApiImplementation());
    }
}

public sealed class HostApiImplementation : IHostApi
{
    public Task<string> GetGreetingAsync(string name) =>
        Task.FromResult($"Hello {name}");
}
```

The generated helper lives in `MyApp.Frontend` and is named after the TypeScript interface (`HostApi` → `HostApiBridge`). Use `ReactHost.WebView`, not `ReactHost`, because the helper registers against the raw WebView.

### React entry and proxy

```tsx
import { createHostApiProxy, signalReady } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");

export function App() {
  return <button onClick={() => void hostApi.getGreeting("Ada")}>Greet</button>;
}

// After subscriptions are registered and the tree is mounted:
requestAnimationFrame(() => signalReady("react"));
```

`signalReady` accepts any JSON payload; the `"react"` argument can be omitted or replaced with app metadata.

### React hook variant

```tsx
import {
  useAvaloniaMessage,
  useHostApi,
  useHostApiInvoke,
  useHostTheme,
  useSignalReady,
} from "@avalonia-react/bridge/react";
import type { HostApi } from "./FrontendApi";

export function HostPanel() {
  const hostApi = useHostApi<HostApi>("HostApi");
  const greeting = useHostApiInvoke<string>("HostApi", "getGreeting");
  const theme = useHostTheme();

  useAvaloniaMessage<{ at: string }>("react:ack", payload => console.log(payload.at));
  useSignalReady({ mounted: true });

  return (
    <button data-theme={theme} onClick={() => void greeting.invoke("Ada")}>
      {greeting.data ?? "Greet"}
    </button>
  );
}
```

`react` is an optional peer dependency; non-React pages can keep importing the root package only.

### Host-to-React request/response

```tsx
import { registerReactHandler } from "@avalonia-react/bridge";

const unregister = registerReactHandler<{ title: string }>(
  "dialog:confirm",
  async (payload, { signal }) => await showConfirm(payload.title, signal),
);
```

```csharp
var confirmed = await ReactHost.InvokeReactAsync<bool>(
    "dialog:confirm",
    new { title = "Delete this item?" },
    TimeSpan.FromSeconds(10));
```

The React handler receives an `AbortSignal`; .NET sends `avalonia:react:cancel` when `ReactInvokeTimeout` expires or the caller cancels. A missing handler returns an error rather than hanging. Register handlers after the module loads; use `useReactHandler` for a React component version.

## DTOs and enums

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
  tags: string[];
}

export interface HostApi {
  save(profile: UserProfile): Promise<UserProfile>;
  find(id: number): Promise<UserProfile[]>;
}
```

```csharp
using MyApp.Frontend;

public sealed class HostApiImplementation : IHostApi
{
    public Task<UserProfile> SaveAsync(UserProfile profile) =>
        Task.FromResult(profile);

    public Task<IReadOnlyList<UserProfile>> FindAsync(double id) =>
        Task.FromResult<IReadOnlyList<UserProfile>>(Array.Empty<UserProfile>());
}

HostApiBridge.Register(ReactHost.WebView, new HostApiImplementation());
```

Generated `UserProfile` has `[JsonPropertyName]` properties and nullable optional properties. `Status` is a string enum with `JsonStringEnumMemberName` values. The generated API method uses `global::MyApp.Frontend.UserProfile` directly instead of `JsonElement`.

If an interface mixes methods and properties, it is treated as an API and its properties are reported as unsupported (`AVR0001`). Split the shape into an API interface and a DTO interface.

## Resource cleanup

```csharp
using AvaloniaWebview.React;

var result = await ReactHost.ClearBrowserDataAsync(
    ReactWebViewDataKind.LocalStorage |
    ReactWebViewDataKind.SessionStorage |
    ReactWebViewDataKind.CacheStorage |
    ReactWebViewDataKind.IndexedDb |
    ReactWebViewDataKind.ServiceWorkers |
    ReactWebViewDataKind.Cookies);

if (!result.IsSuccess)
{
    Console.Error.WriteLine(string.Join("; ", result.Warnings));
}

// After all assembly-embedded-asset WebViews are shut down:
await ReactWebViewResources.ClearExtractedAssetsAsync();
```

`ClearBrowserDataAsync` needs a loaded page for script-visible storage; cookie cleanup still works otherwise. The browser HTTP disk cache is platform-owned and not cleared by this API; delete the configured session/profile directory after shutting WebViews down for a full profile purge.

## Runtime availability preflight

```csharp
using AvaloniaWebview.React;

var availability = ReactWebViewAvailability.Check();
if (!availability.IsAvailable)
{
    // availability.Message explains what is missing.
    // availability.Guidance contains install commands/links.
    ShowInstallGuidance(availability.Message, availability.Guidance);
    return;
}
```

Use `ReactWebViewAvailability.EnsureAvailable()` when throwing is preferable to showing a UI. The probe runs before a WebView exists and works on Windows, macOS, and Linux.

## Trimming / Native AOT

Generated registration avoids method reflection. Primitive API arguments/results and host messages are reflection-free. Generated DTOs, enums, arrays, dictionaries, and nullable values are covered by a generated type-info resolver installed automatically. For custom payload types outside generated contracts, add a source-generated JSON context:

```csharp
using System.Text.Json.Serialization;
using AvaloniaWebview.React;
using MyApp.Frontend;

[JsonSerializable(typeof(MyMessagePayload))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
```

Install it before the WebView serializes anything:

```csharp
// In App.OnFrameworkInitializationCompleted or App constructor, before creating the window:
ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;

// Or scoped to one control:
ReactHost.BridgeJsonSerializerOptions = AppJsonContext.Default.Options;
```

Prefer `HostApiBridge.Register(...)` over `RegisterApi<TApi>(...)`. The reflection overload remains available for non-trimmed apps but is annotated with `RequiresUnreferencedCode`/`RequiresDynamicCode`.

If an API works in Debug but fails after `PublishTrimmed=true` with a reflection-disabled message, the first check is whether the payload type is a generated `.d.ts` contract; those are covered automatically. For custom message/React-call payload types, add every root type the app sends or receives to the context; nested types are discovered from roots.

## Multi-window and tab moves

`ReactHostView` preserves the native WebView across detach/attach by default:

```csharp
var host = new ReactHostView
{
    SourceMode = ReactSourceMode.Auto,
    PreserveWebViewOnDetach = true,
    ReparentingTimeout = TimeSpan.FromSeconds(30),
};

// Move the same control between containers/windows:
windowA.Content = null;
windowB.Content = host;
```

Reattach within `ReparentingTimeout` to keep page, history, cookies, and bridge state. For explicit control of the detach sequence:

```csharp
using (host.BeginReparenting())
{
    windowA.Content = null;
    windowB.Content = host;
}
```

Each WebView's asset-server subscription follows the control. Views with identical `EmbeddedAssetOptions` share one reference-counted loopback server, so their origin and browser storage match; the last view release stops it. Do not attach one control to two parents, and do not create a new WebView just to move a tab.

### Shared browser session

```csharp
// Configure before creating the windows.
ReactWebViewSession.Shared.ProfileName = "MyAppProfile";
ReactWebViewSession.Shared.UserDataFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "WebView");

var first = new MainWindow();
var second = new MainWindow();
first.Show();
second.Show();
```

`ProfileName`/`UserDataFolder` control WebView2 sharing on Windows; macOS uses the default WKWebView data store, and Linux uses `BaseDataDirectory`/`BaseCacheDirectory`. Use a fresh session for an isolated window:

```csharp
var isolated = new ReactHostView
{
    Session = ReactWebViewSession.CreateEphemeral(),
};
```

Cache/share browser data across windows, but register host APIs on each `ReactWebView` that needs them.

## Manual source selection

Use `SourceMode="Manual"` only when the app assigns the source itself.

### Embedded `wwwroot`

```csharp
using AvaloniaWebview.React;
using AvaloniaWebview.React.EmbeddedAssets;

var host = new ReactHostView { SourceMode = ReactSourceMode.Manual };
host.UseEmbeddedAssets(options =>
{
    options.RootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    options.SpaFallback = "index.html";
});
```

### Assembly-embedded assets

```xml
<PropertyGroup>
  <AvaloniaReactEmbedAssetsInAssembly>true</AvaloniaReactEmbedAssetsInAssembly>
  <AvaloniaReactAssemblyAssetPrefix>AvaloniaWebview.React.Assets/</AvaloniaReactAssemblyAssetPrefix>
</PropertyGroup>
```

```csharp
using AvaloniaWebview.React;

var host = new ReactHostView { SourceMode = ReactSourceMode.Manual };
host.UseAssemblyEmbeddedAssets();
```

The main package's build targets copy the frontend to `wwwroot` by default; assembly embedding embeds it as manifest resources and extracts a versioned copy at startup.

## Trusted external API origin

If an auth or embedded checkout page must call host APIs:

```csharp
ReactHost.TrustedApiOrigins.Add(new Uri("https://auth.example.com"));
```

Also allow navigation to that origin if needed:

```csharp
ReactHost.AllowedOrigins.Add(new Uri("https://auth.example.com"));
```

These are separate boundaries. `AllowedOrigins` permits navigation; `TrustedApiOrigins` permits host API calls. Adding only the former still produces a security rejection for API calls.

## Diagnostics at startup

```csharp
using var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
timer.Tick += (_, _) =>
{
    if (ReactHost.AdapterDiagnostics is { } diagnostics)
    {
        Console.WriteLine($"installed={diagnostics.IsInstalled} supported={diagnostics.IsSupported} reason={diagnostics.UnavailableReason}");
    }
};
timer.Start();
```

`ReactHostView` already shows the unavailable reason in its overlay and sets `AdapterStartupTimeout` (10 seconds by default). Use the properties when you need to log or telemetry the result yourself.

## Build and run checklist

```sh
# Frontend
npm install
npm run dev        # Debug; Vite at http://127.0.0.1:5173
npm run build      # Release static assets in dist/wwwroot

# .NET
dotnet build
dotnet run
```

For CI or a .NET-only build, set `-p:AvaloniaReactBuildOnBuild=false -p:AvaloniaReactAutoStart=false` to skip npm/Vite. For Release, verify the published output contains `wwwroot/index.html` or that assembly embedding is enabled.
