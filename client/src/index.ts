export interface AvaloniaReactMessage<T = unknown> {
  type: string;
  payload: T;
}

interface ApiInvocationResult<T = unknown> {
  requestId: string;
  result?: T;
  error?: string | null;
}

interface PendingApiCall {
  resolve(value: unknown): void;
  reject(reason: Error): void;
}

export interface HostApiCallOptions {
  /** Rejects the call when the host does not answer within this many milliseconds. Defaults to no timeout. */
  timeoutMs?: number;
  /** Cancels the call when aborted. */
  signal?: AbortSignal;
}

declare global {
  interface Window {
    invokeCSharpAction?: (body: string) => void;
  }
}

/** Reserved message types understood by AvaloniaReactWebView. Application messages must not use them. */
export const bridgeMessageTypes = {
  ready: "avalonia:ready",
  invoke: "avalonia:invoke",
  invokeResult: "avalonia:invoke:result",
  theme: "theme",
  themeRequest: "theme:request",
} as const;

/** DOM event name used by the host to deliver messages to JavaScript. */
export const bridgeDomainEvent = "avalonia-react:message";

/** Returns whether the page is running inside Avalonia's native WebView bridge. */
export function isBridgeAvailable(): boolean {
  return typeof window !== "undefined" && typeof window.invokeCSharpAction === "function";
}

/** Sends a typed JSON message from React to the Avalonia host. */
export function postMessage<T>(type: string, payload: T): void {
  if (!type.trim()) {
    throw new Error("Message type must not be empty.");
  }

  if (!isBridgeAvailable()) {
    throw new Error("Avalonia's WebView bridge is not available on this page.");
  }

  window.invokeCSharpAction!(JSON.stringify({ type, payload }));
}

/**
 * Tells the host that the React application mounted and can receive host messages.
 * Returns false when the page is not hosted by Avalonia (for example, when opened in a
 * browser during development).
 */
export function signalReady<T = Record<string, never>>(payload?: T): boolean {
  if (!isBridgeAvailable()) {
    return false;
  }

  postMessage(bridgeMessageTypes.ready, payload ?? ({} as T));
  return true;
}

/** Requests the current host theme immediately. */
export function requestTheme(): boolean {
  if (!isBridgeAvailable()) {
    return false;
  }

  postMessage(bridgeMessageTypes.themeRequest, {});
  return true;
}

/** Subscribes to messages sent from AvaloniaReactWebView.PostMessageAsync. */
export function subscribe<T>(type: string, listener: (payload: T) => void): () => void {
  const handler = (event: Event) => {
    const messageEvent = event as CustomEvent<AvaloniaReactMessage<T>>;
    if (messageEvent.detail?.type === type) {
      listener(messageEvent.detail.payload);
    }
  };

  window.addEventListener(bridgeDomainEvent, handler);
  return () => window.removeEventListener(bridgeDomainEvent, handler);
}

const pendingApiCalls = new Map<string, PendingApiCall>();
let nextRequestId = 0;

subscribe<ApiInvocationResult>(bridgeMessageTypes.invokeResult, ({ requestId, result, error }) => {
  const pending = pendingApiCalls.get(requestId);
  if (!pending) return;
  if (error) pending.reject(new Error(error));
  else pending.resolve(result);
});

if (typeof window !== "undefined") {
  window.addEventListener("pagehide", () => {
    const error = new Error("The page unloaded before the host API call completed.");
    for (const pending of [...pendingApiCalls.values()]) {
      pending.reject(error);
    }
    pendingApiCalls.clear();
  });
}

/** Calls an API implemented by the .NET host and generated from a TypeScript interface. */
export function invokeHostApi<T>(api: string, method: string, ...args: unknown[]): Promise<T> {
  return invokeHostApiWithOptions<T>(api, method, args);
}

/**
 * Like {@link invokeHostApi}, with timeout and cancellation support.
 */
export function invokeHostApiWithOptions<T>(
  api: string,
  method: string,
  args: readonly unknown[],
  options: HostApiCallOptions = {},
): Promise<T> {
  const requestId = `${Date.now()}-${++nextRequestId}`;
  const { timeoutMs, signal } = options;

  return new Promise<T>((resolve, reject) => {
    let settled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const cleanup = () => {
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
      signal?.removeEventListener("abort", handleAbort);
    };

    const fail = (error: Error) => {
      if (settled) return;
      settled = true;
      pendingApiCalls.delete(requestId);
      cleanup();
      reject(error);
    };

    const succeed = (value: unknown) => {
      if (settled) return;
      settled = true;
      pendingApiCalls.delete(requestId);
      cleanup();
      resolve(value as T);
    };

    const handleAbort = () => fail(new Error(`The host API call '${api}.${method}' was aborted.`));

    if (signal?.aborted) {
      fail(new Error(`The host API call '${api}.${method}' was aborted.`));
      return;
    }

    if (timeoutMs !== undefined && timeoutMs > 0) {
      timer = setTimeout(
        () => fail(new Error(`The host API call '${api}.${method}' timed out after ${timeoutMs} ms.`)),
        timeoutMs,
      );
    }

    signal?.addEventListener("abort", handleAbort, { once: true });
    pendingApiCalls.set(requestId, { resolve: succeed, reject: fail });

    try {
      postMessage(bridgeMessageTypes.invoke, { requestId, api, method, args });
    } catch (error) {
      fail(error instanceof Error ? error : new Error(String(error)));
    }
  });
}
