// @namespace ReactHost.Frontend
export enum ServiceHealth {
  Healthy = "healthy",
  Degraded = "degraded",
}

/** Host -> React message payload; generated as an AOT-safe DTO. */
export interface ReactAck {
  at: string;
}

export interface RuntimeSnapshot {
  host: string;
  webView: string;
  health: ServiceHealth;
  tags: string[];
}

export interface HostApi {
  getGreeting(name: string): Promise<string>;
  getRuntimeSnapshot(): Promise<RuntimeSnapshot>;
}
