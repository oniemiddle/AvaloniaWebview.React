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
  invokeCancel: "avalonia:invoke:cancel",
  reactInvoke: "avalonia:react:invoke",
  reactResult: "avalonia:react:result",
  reactCancel: "avalonia:react:cancel",
  theme: "theme",
  themeRequest: "theme:request",
  error: "avalonia:error",
} as const;

export interface ReactHandlerContext {
  /** Aborted when the host times out, cancels, navigates, or shuts down. */
  signal: AbortSignal;
}

export type ReactHandler<T> = (
  payload: T,
  context: ReactHandlerContext,
) => unknown | Promise<unknown>;

interface ReactInvocationRequest<T = unknown> {
  requestId: string;
  handler: string;
  payload: T;
}

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
  if (typeof window === "undefined") {
    return () => {};
  }

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

function cancelHostApiCall(requestId: string): void {
  if (!isBridgeAvailable()) {
    return;
  }

  try {
    postMessage(bridgeMessageTypes.invokeCancel, { requestId });
  } catch {
    // The host may already be disposing the page; cancellation is best-effort.
  }
}

subscribe<ApiInvocationResult>(bridgeMessageTypes.invokeResult, ({ requestId, result, error }) => {
  const pending = pendingApiCalls.get(requestId);
  if (!pending) return;
  if (error) pending.reject(new Error(error));
  else pending.resolve(result);
});

const reportedErrors = new Set<string>();

function reportJavaScriptError(
  kind: "error" | "unhandledrejection",
  error: unknown,
  source?: string,
  line?: number,
  column?: number,
): void {
  if (!isBridgeAvailable()) {
    return;
  }

  const record = typeof error === "object" && error !== null
    ? (error as { message?: unknown; stack?: unknown })
    : undefined;
  const message = typeof error === "string"
    ? error
    : typeof record?.message === "string"
      ? record.message
      : "Unhandled JavaScript error";
  const stack = typeof record?.stack === "string" ? record.stack : undefined;
  const key = `${kind}:${message}:${source ?? ""}:${line ?? ""}:${column ?? ""}`;
  if (reportedErrors.has(key) || reportedErrors.size >= 50) {
    return;
  }

  reportedErrors.add(key);
  try {
    postMessage(bridgeMessageTypes.error, { kind, message, stack, source, line, column });
  } catch {
    // The bridge can disappear while the page is unloading; error reporting is best-effort.
  }
}

if (typeof window !== "undefined") {
  window.addEventListener("pagehide", () => {
    const error = new Error("The page unloaded before the host API call completed.");
    for (const requestId of [...pendingApiCalls.keys()]) {
      cancelHostApiCall(requestId);
    }
    for (const pending of [...pendingApiCalls.values()]) {
      pending.reject(error);
    }
    pendingApiCalls.clear();
  });

  window.addEventListener("error", (event) => {
    const errorEvent = event as ErrorEvent;
    reportJavaScriptError(
      "error",
      errorEvent.error ?? errorEvent.message,
      errorEvent.filename,
      errorEvent.lineno,
      errorEvent.colno,
    );
  });

  window.addEventListener("unhandledrejection", (event) => {
    reportJavaScriptError("unhandledrejection", (event as PromiseRejectionEvent).reason);
  });
}

/** Calls an API implemented by the .NET host and generated from a TypeScript interface. */
export function invokeHostApi<T>(api: string, method: string, ...args: unknown[]): Promise<T> {
  return invokeHostApiWithOptions<T>(api, method, args);
}

/**
 * Creates a typed proxy for a host API. The TypeScript contract is the single source of truth:
 * property access becomes `invokeHostApi(api, property, ...args)`, so the API and method names are
 * checked by the compiler.
 *
 * ```ts
 * import type { HostApi } from "./FrontendApi";
 * const hostApi = createHostApiProxy<HostApi>("HostApi");
 * const greeting = await hostApi.getGreeting("Ada");
 * ```
 */
export function createHostApiProxy<T extends object>(api: string): T {
  return new Proxy({} as T, {
    get(_target, property) {
      if (typeof property !== "string") {
        return undefined;
      }

      return (...args: unknown[]) => invokeHostApi(api, property, ...args);
    },
  });
}

const reactHandlers = new Map<string, ReactHandler<unknown>>();
const pendingReactInvocations = new Map<string, AbortController>();

/**
 * Registers a handler the .NET host can call with InvokeReactAsync.
 * Returns an unsubscribe function. Registering the same name replaces the handler.
 */
export function registerReactHandler<T>(
  handlerName: string,
  handler: ReactHandler<T>,
): () => void {
  if (!handlerName.trim()) {
    throw new Error("React handler name must not be empty.");
  }

  const entry = handler as ReactHandler<unknown>;
  reactHandlers.set(handlerName, entry);
  return () => {
    if (reactHandlers.get(handlerName) === entry) {
      reactHandlers.delete(handlerName);
    }
  };
}

function postReactResult(payload: unknown): void {
  try {
    postMessage(bridgeMessageTypes.reactResult, payload);
  } catch {
    // The bridge can disappear while the page unloads; result delivery is best-effort.
  }
}

async function handleReactInvocation(request: ReactInvocationRequest | null | undefined): Promise<void> {
  const requestId = request?.requestId;
  const handlerName = request?.handler;
  if (typeof requestId !== "string" || !requestId || typeof handlerName !== "string" || !handlerName) {
    return;
  }

  const handler = reactHandlers.get(handlerName);
  if (!handler) {
    postReactResult({
      requestId,
      error: `No React handler registered for '${handlerName}'.`,
    });
    return;
  }

  const controller = new AbortController();
  pendingReactInvocations.set(requestId, controller);
  try {
    const result = await handler(request.payload, { signal: controller.signal });
    if (!controller.signal.aborted) {
      postReactResult({ requestId, result });
    }
  } catch (reason) {
    if (!controller.signal.aborted) {
      postReactResult({
        requestId,
        error: reason instanceof Error ? reason.message : String(reason),
      });
    }
  } finally {
    if (pendingReactInvocations.get(requestId) === controller) {
      pendingReactInvocations.delete(requestId);
    }
  }
}

subscribe<ReactInvocationRequest>(bridgeMessageTypes.reactInvoke, request => {
  void handleReactInvocation(request);
});

subscribe<{ requestId?: string }>(bridgeMessageTypes.reactCancel, payload => {
  if (payload?.requestId) {
    pendingReactInvocations.get(payload.requestId)?.abort();
  }
});

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

    const handleAbort = () => {
      cancelHostApiCall(requestId);
      fail(new Error(`The host API call '${api}.${method}' was aborted.`));
    };

    if (signal?.aborted) {
      fail(new Error(`The host API call '${api}.${method}' was aborted.`));
      return;
    }

    if (timeoutMs !== undefined && timeoutMs > 0) {
      timer = setTimeout(() => {
        cancelHostApiCall(requestId);
        fail(new Error(`The host API call '${api}.${method}' timed out after ${timeoutMs} ms.`));
      }, timeoutMs);
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
