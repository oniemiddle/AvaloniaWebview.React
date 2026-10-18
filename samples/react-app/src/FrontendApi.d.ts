// @namespace ReactHost.Frontend
export enum ServiceHealth {
  Healthy = "healthy",
  Degraded = "degraded",
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
