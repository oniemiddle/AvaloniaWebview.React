import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  bridgeMessageTypes,
  createHostApiProxy,
  invokeHostApiWithOptions,
  isBridgeAvailable,
  requestTheme,
  signalReady,
  subscribe,
  type HostApiCallOptions,
} from "./index.js";

export type HostTheme = "light" | "dark";

/** Returns a stable typed proxy for a generated host API contract. */
export function useHostApi<T extends object>(api: string): T {
  return useMemo(() => createHostApiProxy<T>(api), [api]);
}

/** Subscribes to a host message and keeps the latest handler without resubscribing. */
export function useAvaloniaMessage<T>(type: string, handler: (payload: T) => void): void {
  const handlerRef = useRef(handler);
  useEffect(() => {
    handlerRef.current = handler;
  }, [handler]);

  useEffect(() => {
    return subscribe<T>(type, payload => handlerRef.current(payload));
  }, [type]);
}

/** True when the page is running inside Avalonia's native WebView bridge. */
export function useBridgeAvailable(): boolean {
  const [available, setAvailable] = useState(() => isBridgeAvailable());
  useEffect(() => {
    setAvailable(isBridgeAvailable());
  }, []);
  return available;
}

/**
 * Subscribes to the host theme and asks for the current theme on mount.
 * Returns null until the host answers or when no theme has been received.
 */
export function useHostTheme(): HostTheme | null {
  const [theme, setTheme] = useState<HostTheme | null>(null);

  useEffect(() => {
    const unsubscribe = subscribe<{ theme?: string }>(bridgeMessageTypes.theme, payload => {
      if (payload?.theme === "light" || payload?.theme === "dark") {
        setTheme(payload.theme);
      }
    });
    requestTheme();
    return unsubscribe;
  }, []);

  return theme;
}

/**
 * Calls signalReady() after mount. Returns false when the caller is not hosted
 * by Avalonia or when the host is not ready to receive the handshake.
 */
export function useSignalReady<T = Record<string, never>>(payload?: T): boolean {
  const [sent, setSent] = useState(false);

  useEffect(() => {
    setSent(signalReady(payload));
  }, []);

  return sent;
}

export interface HostApiInvocation<T> {
  data: T | null;
  error: Error | null;
  loading: boolean;
  invoke: (...args: unknown[]) => Promise<T>;
  cancel: () => void;
}

/**
 * Invokes one host API method with React state, automatic cancellation of the
 * previous call, and an AbortController-backed cancel function.
 */
export function useHostApiInvoke<T>(
  api: string,
  method: string,
  options: HostApiCallOptions = {},
): HostApiInvocation<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(false);
  const controllerRef = useRef<AbortController | null>(null);

  const cancel = useCallback(() => {
    controllerRef.current?.abort();
  }, []);

  const invoke = useCallback(
    async (...args: unknown[]): Promise<T> => {
      controllerRef.current?.abort();
      const controller = new AbortController();
      controllerRef.current = controller;
      setData(null);
      setError(null);
      setLoading(true);

      try {
        const result = await invokeHostApiWithOptions<T>(api, method, args, {
          ...options,
          signal: controller.signal,
        });
        if (!controller.signal.aborted) {
          setData(result);
          setLoading(false);
        }
        return result;
      } catch (reason) {
        const failure = reason instanceof Error ? reason : new Error(String(reason));
        if (!controller.signal.aborted) {
          setError(failure);
          setLoading(false);
        }
        throw failure;
      }
    },
    [api, method, options.signal, options.timeoutMs],
  );

  useEffect(() => {
    return () => controllerRef.current?.abort();
  }, []);

  return { data, error, loading, invoke, cancel };
}
