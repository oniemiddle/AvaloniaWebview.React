export interface AvaloniaReactMessage<T = unknown> {
  type: string;
  payload: T;
}

interface ApiInvocationResult<T = unknown> {
  requestId: string;
  result?: T;
  error?: string | null;
}

declare global {
  interface Window {
    invokeCSharpAction?: (body: string) => void;
  }
}

/** Sends a typed JSON message from React to the Avalonia host. */
export function postMessage<T>(type: string, payload: T): void {
  if (!type.trim()) {
    throw new Error("Message type must not be empty.");
  }

  if (typeof window.invokeCSharpAction !== "function") {
    throw new Error("Avalonia's WebView bridge is not available on this page.");
  }

  window.invokeCSharpAction(JSON.stringify({ type, payload }));
}

/** Subscribes to messages sent from AvaloniaReactWebView.PostMessageAsync. */
export function subscribe<T>(type: string, listener: (payload: T) => void): () => void {
  const handler = (event: Event) => {
    const messageEvent = event as CustomEvent<AvaloniaReactMessage<T>>;
    if (messageEvent.detail?.type === type) {
      listener(messageEvent.detail.payload);
    }
  };

  window.addEventListener("avalonia-react:message", handler);
  return () => window.removeEventListener("avalonia-react:message", handler);
}

const pendingApiCalls = new Map<string, { resolve: (value: unknown) => void; reject: (reason: Error) => void }>();
let nextRequestId = 0;

subscribe<ApiInvocationResult>("avalonia:invoke:result", ({ requestId, result, error }) => {
  const pending = pendingApiCalls.get(requestId);
  if (!pending) return;
  pendingApiCalls.delete(requestId);
  if (error) pending.reject(new Error(error));
  else pending.resolve(result);
});

/** Calls an API implemented by the .NET host and generated from a TypeScript interface. */
export function invokeHostApi<T>(api: string, method: string, ...args: unknown[]): Promise<T> {
  const requestId = `${Date.now()}-${++nextRequestId}`;
  return new Promise<T>((resolve, reject) => {
    pendingApiCalls.set(requestId, {
      resolve: value => resolve(value as T),
      reject,
    });
    try {
      postMessage("avalonia:invoke", { requestId, api, method, args });
    } catch (error) {
      pendingApiCalls.delete(requestId);
      reject(error instanceof Error ? error : new Error(String(error)));
    }
  });
}
