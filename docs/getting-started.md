# Getting started

Install the NuGet packages, add the React client, and generate a typed host API from a TypeScript contract.

## Install

```sh
dotnet add package AvaloniaWebview.React
```

The package references `Avalonia.Controls.WebView` 12.1.0. Windows uses WebView2, macOS uses the system WKWebView, and Linux needs the WebKitGTK or WPE WebKit runtime. The browser runtime is provided by the operating system; Chromium is not bundled.

On Windows, the host executable must embed an application manifest that declares Windows 10/11 compatibility before `NativeWebView` can create its native child window. The sample does this with `samples/ReactHost/app.manifest` and the `ApplicationManifest` project property; add the same compatibility declaration to your host application's manifest.

The main package also imports an MSBuild frontend build target. When the consuming project contains a `ClientApp/package.json`, it installs dependencies with `npm ci` when needed, runs `npm run build` before the .NET build, and copies `dist` to the app's `wwwroot` output. Override `AvaloniaReactProjectDirectory`, `AvaloniaReactBuildCommand`, `AvaloniaReactInstallCommand`, or set `AvaloniaReactBuildOnBuild=false` in the project file to customize it.

For Vite development, install the optional extension:

```sh
dotnet add package AvaloniaWebview.React.Vite
```

In Debug builds it checks the configured Vite server before the .NET build, starts it when needed, and waits for the server to accept connections before continuing. Visual Studio's fast up-to-date check is disabled for Debug when this auto-start target applies, so each F5 verifies/restarts Vite if its process exited after the previous debug session. This adds a no-op MSBuild check to each Debug launch. Set `AvaloniaReactDevServerDirectory` to the React project directory; `AvaloniaReactDevServerUrl`, `AvaloniaReactDevServerCommand`, and `AvaloniaReactAutoStart` can override the defaults. Set `AvaloniaReactDevServerWarmupPath` to a frequently used client entry (for example, `src/main.tsx`) to wait until Vite has transformed it before launching the host. Node.js and npm must be installed, and frontend dependencies must be available.

To generate C# interfaces from TypeScript API contracts, install:

```sh
dotnet add package AvaloniaWebview.React.SourceGenerator
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

This generates `MyApp.Frontend.IHostApi` with `Task<string> GetGreetingAsync(string name)`, `Task SaveCountAsync(double value)`, and `Task<IReadOnlyList<string>> FindUsersAsync(double limit, string? filter = default)`. Generated types include `string`, `number`, `boolean`, `void`, `object`, `unknown`, `any`, arrays, `Record<string, T>`, `Date`, nullable unions, and literal unions; any type that cannot be represented is mapped to `JsonElement` with an `AVR0002` diagnostic. Unsupported members produce `AVR0001`, generic interfaces are skipped with `AVR0003`, and unsupported enums are skipped with `AVR0004`.

Interfaces without methods and object `type` aliases generate DTO classes instead of API contracts:

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
}

export interface HostApi {
  save(profile: UserProfile): Promise<UserProfile>;
}
```

This emits a `UserProfile` class with `[JsonPropertyName]` properties, a `Status` enum using `JsonStringEnumMemberName` values, and an `IHostApi.SaveAsync(UserProfile)` method. Generated DTOs and enums declared in other `.d.ts` files are resolved when their names are unique; otherwise they fall back to `JsonElement`.

Generated interfaces are `partial` and carry `[ReactApiContract]`/`[ReactApiMethod]`, so the bridge uses the exact TypeScript API and method names. Keep the contract in the React project and reference it from the .NET project with `<AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />` to use one file for both sides.

Implement the generated interface and register it with the generated reflection-free helper `HostApiBridge.Register(webView, implementation)` (or the reflection-based `webView.RegisterApi<IHostApi>(implementation)`). On the React side, `createHostApiProxy<T>("HostApi")` returns a compiler-checked proxy, so method names and arguments no longer have to be written as strings:

```ts
import { createHostApiProxy } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
```

The lower-level `invokeHostApi<T>("HostApi", "getGreeting", "Ada")` form remains available.

Host API methods can observe cancellation. Set `<AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>` in the host project; generated interfaces then append `CancellationToken cancellationToken = default` to every method. The bridge passes a token that is canceled when React aborts or times out a call, when the page navigates or fails to load, and on shutdown. The bridge client sends `avalonia:invoke:cancel` automatically in those cases. The option defaults to `false`, so existing contracts and implementations are unchanged.

## React client package

The frontend bridge is published as `@avalonia-react/bridge`:

```sh
npm install @avalonia-react/bridge
```

It is ESM-only, includes TypeScript declarations, and forwards the reserved `avalonia:*` messages, host API calls, theme requests, JavaScript errors, cancellation, and timeouts. React projects can use `@avalonia-react/bridge/react` for `useHostApi`, `useHostApiInvoke`, `useAvaloniaMessage`, `useHostTheme`, `useSignalReady`, `useBridgeAvailable`, and `useReactHandler`. Inside this repository the sample uses the local package through `file:../../client`; run `npm --prefix client run build` before installing it.

## Agent skill

[`skills/avalonia-webview-react-hybrid`](../skills/avalonia-webview-react-hybrid/SKILL.md) is an installable Agent Skill for teams using the library. It covers the hybrid architecture, contract-first host APIs, source modes, security boundaries, runtime diagnostics, multi-window reparenting, and trimming/AOT. See [`skills/README.md`](../skills/README.md) for DSH, Claude Code, and Codex installation.

