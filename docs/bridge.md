# Bridge protocol

The JSON message contract between React and the Avalonia host, including host-to-React request/response calls.

## React to Avalonia messages

The TypeScript helper is published as [`@avalonia-react/bridge`](../client/README.md):

```sh
npm install @avalonia-react/bridge
```

The NuGet package itself needs no npm dependency: React can call Avalonia's built-in JavaScript bridge directly.

```tsx
import { postMessage, requestTheme, signalReady, subscribe } from "@avalonia-react/bridge";

// Call after React mounts: tells the host the bridge is ready.
signalReady({ version: 1 });

const unsubscribe = subscribe<{ theme: "light" | "dark" }>("theme", ({ theme }) => {
  document.documentElement.dataset.theme = theme;
});
requestTheme();

postMessage("account:changed", { id: 42 });
```

The Avalonia WebView exposes JavaScript's `invokeCSharpAction` helper. The client uses it to send JSON to .NET. Subscribe after your React app mounts so frontend handlers are ready before the host sends messages. For the initial theme, subscribe in the entry module before rendering.

`avalonia:ready`, `avalonia:invoke`, `avalonia:invoke:result`, `avalonia:invoke:cancel`, `avalonia:error`, `theme`, and `theme:request` are reserved bridge messages handled by the control; application messages must use different names. `MessageReceived` only receives application messages. The first `avalonia:ready` from a page raises `BridgeReady`; use it to dismiss startup UI once React has mounted.

`AvaloniaReactWebView` sends a `theme` message when the WebView's actual theme variant changes and once after every successful navigation. The payload is `{ theme: "light" | "dark" }`: `ThemeVariant.Default` follows the system theme, and custom variants resolve through their inherited variant or fall back to the system theme, so React always receives a semantic value. Send `{ type: "theme:request", payload: {} }` after subscribing to receive the current theme immediately, which is the reliable way to capture the initial value before the first navigation message arrives.

If your React project only consumes the NuGet package, use the same message contract directly:

```ts
declare global {
  interface Window {
    invokeCSharpAction: (body: string) => void;
  }
}

export function postToAvalonia<T>(type: string, payload: T): void {
  window.invokeCSharpAction(JSON.stringify({ type, payload }));
}

export function onAvaloniaMessage<T>(type: string, listener: (payload: T) => void): () => void {
  const handler = (event: Event) => {
    const message = (event as CustomEvent<{ type: string; payload: T }>).detail;
    if (message?.type === type) listener(message.payload);
  };
  window.addEventListener("avalonia-react:message", handler);
  return () => window.removeEventListener("avalonia-react:message", handler);
}
```

## Avalonia to React messages

```csharp
webView.MessageReceived += (_, message) =>
{
    if (message.Type == "ready")
    {
        var version = message.GetPayload<ReadyPayload>();
        // Handle the message in the host application.
    }
};

await webView.PostMessageAsync("theme", new { theme = "dark" });

public sealed record ReadyPayload(int Version);
```

### Host-to-React calls

For request/response flows initiated by .NET, register a React handler and call it from the host:

```tsx
import { registerReactHandler } from "@avalonia-react/bridge";

const unregister = registerReactHandler<{ title: string }>(
  "dialog:confirm",
  async (payload, { signal }) => {
    // Use signal to dismiss the dialog when the host times out or cancels.
    return await showConfirm(payload.title, signal);
  },
);
```

```csharp
var confirmed = await ReactHost.InvokeReactAsync<bool>(
    "dialog:confirm",
    new { title = "Delete this item?" },
    TimeSpan.FromSeconds(10));
```

The host waits for `avalonia:react:result`, throws when the handler rejects, and times out after `ReactInvokeTimeout` (10 seconds by default) or the explicit timeout. A timeout or cancellation sends `avalonia:react:cancel`, which aborts the handler's `AbortSignal`. Use `useReactHandler` from `@avalonia-react/bridge/react` to keep a React handler current without re-registering on every render. Unknown handlers return an error to .NET instead of hanging.

`PostMessageAsync` completes when the script runs, but messages sent before the page loads are queued and delivered after a successful navigation. Use `LoadState` and `BridgeReady` to decide when the UI is ready instead of relying on navigation alone:

```csharp
webView.LoadStateChanged += (_, args) =>
{
    if (args.State == ReactLoadState.Failed) ShowError(args.Error);
};

webView.BridgeReady += (_, _) => HideStartupOverlay();
```

