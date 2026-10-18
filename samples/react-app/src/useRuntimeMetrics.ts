import { useCallback, useEffect, useRef, useState } from "react";
import type { HostApi, RuntimeMetrics } from "./FrontendApi";

const POLL_INTERVAL_MS = 2000;
const HISTORY_LIMIT = 24;
const MEGABYTE = 1024 * 1024;
const GIGABYTE = 1024 * 1024 * 1024;
const FALLBACK_TOTAL_MEMORY = 8 * GIGABYTE;

export type MetricsSource = "host" | "preview";

export interface RuntimeMetricsState {
  metrics: RuntimeMetrics | null;
  samples: RuntimeMetrics[];
  source: MetricsSource;
  error: string | null;
  refresh: () => void;
}

type MemoryPerformance = Performance & {
  memory?: { usedJSHeapSize: number; totalJSHeapSize: number; jsHeapSizeLimit: number };
};

type BrowserNavigator = Navigator & {
  deviceMemory?: number;
  userAgentData?: { platform?: string };
};

function detectPlatform(navigatorValue: BrowserNavigator): string {
  if (navigatorValue.userAgentData?.platform) {
    return navigatorValue.userAgentData.platform;
  }

  const agent = navigatorValue.userAgent;
  if (/Mac/i.test(agent)) {
    return "macOS";
  }
  if (/Win/i.test(agent)) {
    return "Windows";
  }
  if (/Android/i.test(agent)) {
    return "Android";
  }
  if (/Linux/i.test(agent)) {
    return "Linux";
  }
  if (/iPhone|iPad/i.test(agent)) {
    return "iOS";
  }
  return "浏览器";
}

function detectEngine(navigatorValue: BrowserNavigator): string {
  const agent = navigatorValue.userAgent;
  if (/Edg\//.test(agent)) {
    return "Microsoft Edge (Chromium)";
  }
  if (/Chrome\//.test(agent)) {
    return "Chromium";
  }
  if (/Firefox\//.test(agent)) {
    return "Gecko · Firefox";
  }
  if (/Safari\//.test(agent)) {
    return "WebKit · Safari";
  }
  return "浏览器引擎";
}

/**
 * Reads representative values from the browser so the dashboard stays legible
 * when the page runs outside the Avalonia host (for example in Vite or a test).
 */
export function readPreviewMetrics(): RuntimeMetrics {
  const navigatorValue = navigator as BrowserNavigator;
  const memory = (performance as MemoryPerformance).memory;
  const totalMemoryBytes = navigatorValue.deviceMemory
    ? navigatorValue.deviceMemory * GIGABYTE
    : FALLBACK_TOTAL_MEMORY;

  return {
    os: detectPlatform(navigatorValue) + " · 浏览器预览",
    runtime: "JavaScript · 浏览器运行时",
    avalonia: "预览模式（未连接宿主）",
    webView: detectEngine(navigatorValue),
    machine: "浏览器标签页",
    processorCount: navigatorValue.hardwareConcurrency ?? 4,
    totalMemoryBytes,
    workingSetBytes: memory?.usedJSHeapSize ?? 64 * MEGABYTE,
    gcHeapBytes: memory?.totalJSHeapSize ?? 96 * MEGABYTE,
    threadCount: navigatorValue.hardwareConcurrency ?? 4,
    uptimeSeconds: performance.now() / 1000,
    capturedAt: new Date().toISOString(),
  };
}

/**
 * Polls IHostApi.getRuntimeMetrics() every two seconds and keeps a short
 * sample history for the sparklines. Outside Avalonia it uses browser values
 * so the sample still renders in a plain Vite preview.
 */
export function useRuntimeMetrics(hostApi: HostApi, bridgeAvailable: boolean): RuntimeMetricsState {
  const [metrics, setMetrics] = useState<RuntimeMetrics | null>(null);
  const [samples, setSamples] = useState<RuntimeMetrics[]>([]);
  const [source, setSource] = useState<MetricsSource>("preview");
  const [error, setError] = useState<string | null>(null);
  const hasHostData = useRef(false);
  const tickRef = useRef<() => void>(() => undefined);

  const publish = useCallback((next: RuntimeMetrics, nextSource: MetricsSource) => {
    setMetrics(next);
    setSource(nextSource);
    setSamples(previous => [...previous, next].slice(-HISTORY_LIMIT));
  }, []);

  useEffect(() => {
    let cancelled = false;

    const tick = async () => {
      if (!bridgeAvailable) {
        if (!cancelled) {
          publish(readPreviewMetrics(), "preview");
          setError(null);
        }
        return;
      }

      try {
        const next = await hostApi.getRuntimeMetrics();
        if (cancelled) {
          return;
        }
        hasHostData.current = true;
        setError(null);
        publish(next, "host");
      } catch (reason) {
        if (cancelled) {
          return;
        }
        setError(reason instanceof Error ? reason.message : String(reason));
        if (!hasHostData.current) {
          publish(readPreviewMetrics(), "preview");
        }
      }
    };

    tickRef.current = () => {
      void tick();
    };

    void tick();
    const timer = window.setInterval(() => {
      void tick();
    }, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [bridgeAvailable, hostApi, publish]);

  const refresh = useCallback(() => {
    tickRef.current();
  }, []);

  return { metrics, samples, source, error, refresh };
}
