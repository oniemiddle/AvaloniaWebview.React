export interface ServiceInfo {
  key: string;
  service: string;
  role: string;
  status: "online" | "degraded";
  latency: string;
  detail: string;
}

/** Shared by the lazy-loaded table and the collapsed preview on the dashboard. */
export const services: ServiceInfo[] = [
  {
    key: "1",
    service: "Avalonia Host Bridge",
    role: "JSON-RPC · 宿主 API",
    status: "online",
    latency: "12 ms",
    detail: "127.0.0.1 · 回环",
  },
  {
    key: "2",
    service: "React Renderer",
    role: "React 19 · 并发渲染",
    status: "online",
    latency: "8 ms",
    detail: "主线程 · 增量更新",
  },
  {
    key: "3",
    service: "Vite Dev Server",
    role: "内嵌资源 / HMR",
    status: "online",
    latency: "24 ms",
    detail: "开发模式下自动启动",
  },
];
