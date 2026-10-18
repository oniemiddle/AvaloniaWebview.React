# @avalonia-react/bridge

TypeScript bridge used by the React frontend hosted in `AvaloniaReactWebView`. The package is ESM-only and ships TypeScript declarations.

```sh
npm install @avalonia-react/bridge
```

When working inside the AvaloniaWebview.React repository, point the example app at the local package instead:

```sh
npm install ../../client
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

When a call times out, is aborted, or the page unloads, the client also sends the reserved `avalonia:invoke:cancel` message so the host can cancel the implementation's `CancellationToken`. Calls rejected because the host page unloads reject with an `Error` instead of staying pending forever. `isBridgeAvailable()` tells whether the page runs inside Avalonia's WebView, and `bridgeMessageTypes` lists the reserved bridge message names.

### React hooks

React users can import hooks from the `/react` subpath. React is an optional peer dependency; the core bridge remains usable without it.

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
  const { invoke, data, error, loading, cancel } = useHostApiInvoke<string>("HostApi", "getGreeting");
  const theme = useHostTheme();

  useAvaloniaMessage<{ at: string }>("react:ack", payload => console.log(payload.at));
  useSignalReady({ mounted: true });

  return (
    <section data-theme={theme}>
      <button onClick={() => void invoke("Ada")}>Call host</button>
      {loading && <span>Loading…</span>}
      {error && <span>{error.message}</span>}
      {data && <strong>{data}</strong>}
      {loading && <button onClick={cancel}>Cancel</button>}
    </section>
  );
}
```

- `useHostApi(api)` returns a stable typed proxy.
- `useAvaloniaMessage(type, handler)` subscribes/unsubscribes automatically and keeps the latest handler.
- `useHostTheme()` requests the current theme on mount and returns `"light"`/`"dark"`/`null`.
- `useSignalReady(payload?)` calls `signalReady()` after mount.
- `useHostApiInvoke(api, method, options?)` exposes `data`, `error`, `loading`, `invoke`, and `cancel`; a new call cancels the previous one and cleanup aborts pending work.
- `useBridgeAvailable()` reports whether the page is inside Avalonia's WebView.

The client also forwards `window` errors and unhandled promise rejections as the reserved `avalonia:error` message, which raises `AvaloniaReactWebView.JavaScriptError` on the host. Duplicate errors are reported at most once, and reporting is disabled when the bridge is unavailable.

The message format is `{ "type": string, "payload": any JSON value }`. API calls use a request ID and return either a result or an error through the same message bridge.

The module can be imported in Node/SSR environments without a `window`; `isBridgeAvailable()` then returns `false`, and `subscribe()` returns a no-op unsubscribe function. The package is ESM-only and includes `dist/index.d.ts`/`dist/react.d.ts` alongside the JavaScript builds.

Maintainers can validate the publishable tarball with `npm run verify:package`; see [RELEASING.md](https://github.com/oniemiddle/AvaloniaWebview.React/blob/main/RELEASING.md) for the full release checklist.
