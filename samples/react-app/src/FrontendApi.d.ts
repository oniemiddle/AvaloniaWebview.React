// @namespace ReactHost.Frontend

/** Host -> React message payload; generated as an AOT-safe DTO. */
export interface ReactAck {
  at: string;
}

export enum ServiceHealth {
  Healthy = "healthy",
  Degraded = "degraded",
}

/** Snapshot used by the dashboard's bridge status card. */
export interface RuntimeSnapshot {
  host: string;
  webView: string;
  health: ServiceHealth;
  tags: string[];
}

/** Live .NET runtime metrics sampled by the host every few seconds. */
export interface RuntimeMetrics {
  os: string;
  runtime: string;
  avalonia: string;
  webView: string;
  machine: string;
  processorCount: number;
  totalMemoryBytes: number;
  workingSetBytes: number;
  gcHeapBytes: number;
  threadCount: number;
  uptimeSeconds: number;
  capturedAt: string;
}

export interface HostApi {
  getGreeting(name: string): Promise<string>;
  getRuntimeSnapshot(): Promise<RuntimeSnapshot>;
  getRuntimeMetrics(): Promise<RuntimeMetrics>;
}
