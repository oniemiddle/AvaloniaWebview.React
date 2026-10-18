# React bridge client

This directory contains the TypeScript package used by the React frontend hosted in `AvaloniaReactWebView`.

```sh
npm install ../client
```

Use `postMessage(type, payload)` to send a JSON message to .NET. Use `subscribe(type, listener)` to receive host messages and call the returned function to unsubscribe.

Call `signalReady({ ... })` after React mounts. It sends the reserved `avalonia:ready` message, which lets the host hide a loading state and treat the bridge as ready. It returns `false` when the page is not hosted by Avalonia, so the same code can run in a plain browser during Vite development. `requestTheme()` asks the host to resend the current theme immediately.

Use `invokeHostApi<T>(api, method, ...args)` to call a method exposed by a generated TypeScript API contract. For example, the `HostApi.getGreeting(name)` method resolves to its .NET `Task<string>` result:

```ts
const greeting = await invokeHostApi<string>("HostApi", "getGreeting", "Semi Design");
```

Keep the TypeScript contract as the single source of truth with `createHostApiProxy`, which maps property access to `invokeHostApi` and lets the compiler check API and method names:

```ts
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
```

Use `invokeHostApiWithOptions` when a call needs a timeout or cancellation:

```ts
const controller = new AbortController();
const value = await invokeHostApiWithOptions<string>(
  "HostApi",
  "getGreeting",
  ["Ada"],
  { timeoutMs: 5000, signal: controller.signal },
);
```

Calls rejected because the host page unloads reject with an `Error` instead of staying pending forever. `isBridgeAvailable()` tells whether the page runs inside Avalonia's WebView, and `bridgeMessageTypes` lists the reserved bridge message names.

The client also forwards `window` errors and unhandled promise rejections as the reserved `avalonia:error` message, which raises `AvaloniaReactWebView.JavaScriptError` on the host. Duplicate errors are reported at most once, and reporting is disabled when the bridge is unavailable.

The message format is `{ "type": string, "payload": any JSON value }`. API calls use a request ID and return either a result or an error through the same message bridge.
