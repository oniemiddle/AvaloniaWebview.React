import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import "@douyinfe/semi-ui/react19-adapter";
import { Avatar, Layout, Nav, Space, Tag, Typography } from "@douyinfe/semi-ui";
import { postMessage } from "@avalonia-react/bridge";
import {
  useAvaloniaMessage,
  useBridgeAvailable,
  useHostApi,
  useHostApiInvoke,
  useHostTheme,
  useReactHandler,
  useSignalReady,
} from "@avalonia-react/bridge/react";
import type { HostApi, ReactAck, RuntimeSnapshot } from "./FrontendApi";
import type { BridgeEvent, BridgeEventKind, ViewKey } from "./types";
import { VIEW_META } from "./types";
import { useRuntimeMetrics } from "./useRuntimeMetrics";
import { OverviewView } from "./views/Overview";
import { PlaygroundView } from "./views/Playground";
import { AboutView } from "./views/About";
import { GridIcon, InfoIcon, TerminalIcon } from "./components/Icons";

const { Sider, Header, Content } = Layout;
const { Text } = Typography;

const SHARED_COUNTER_KEY = "sample:shared-counter";
const APP_VERSION = "0.19.0";

function readSharedCounter(): number {
  try {
    return Number(window.localStorage.getItem(SHARED_COUNTER_KEY) ?? "0") || 0;
  } catch {
    return 0;
  }
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

const NAV_ITEMS: Array<{ itemKey: ViewKey; text: string; icon: ReactNode }> = [
  { itemKey: "overview", text: "运行概览", icon: <GridIcon size={16} /> },
  { itemKey: "playground", text: "桥接实验", icon: <TerminalIcon size={16} /> },
  { itemKey: "about", text: "关于示例", icon: <InfoIcon size={16} /> },
];

export default function App() {
  const hostApi = useHostApi<HostApi>("HostApi");
  const bridgeAvailable = useBridgeAvailable();
  const theme = useHostTheme();
  const greeting = useHostApiInvoke<string>("HostApi", "getGreeting");
  const snapshot = useHostApiInvoke<RuntimeSnapshot>("HostApi", "getRuntimeSnapshot");
  const runtimeMetrics = useRuntimeMetrics(hostApi, bridgeAvailable);

  const [view, setView] = useState<ViewKey>("overview");
  const [systemTheme, setSystemTheme] = useState<"light" | "dark" | null>(null);
  const [events, setEvents] = useState<BridgeEvent[]>([]);
  const [proxyGreeting, setProxyGreeting] = useState("");
  const [sharedCount, setSharedCount] = useState(readSharedCounter);
  const eventId = useRef(0);

  const pushEvent = useCallback((kind: BridgeEventKind, text: string) => {
    eventId.current += 1;
    const at = new Date().toLocaleTimeString("zh-CN", { hour12: false });
    setEvents(previous => [...previous.slice(-23), { id: eventId.current, at, kind, text }]);
  }, []);

  useSignalReady({ frontend: "React 19", toolkit: APP_VERSION });

  useAvaloniaMessage<ReactAck>("react:ack", ({ at }) => {
    pushEvent("ack", "宿主回执 " + at + " · 原生状态栏已更新");
  });

  useReactHandler<string>("host:greet", from => {
    const response = "React 已响应「" + from + "」，当前主题：" + (theme ?? "unknown");
    pushEvent("host", "宿主调用 host:greet → " + response);
    return response;
  });

  useEffect(() => {
    if (!bridgeAvailable) {
      return;
    }
    void greeting.invoke("AvaloniaWebview.React").catch(() => undefined);
    void snapshot.invoke().catch(() => undefined);
  }, [bridgeAvailable]);

  useEffect(() => {
    const applied = document.documentElement.dataset.theme;
    if (applied === "light" || applied === "dark") {
      setSystemTheme(applied);
    }
  }, []);

  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key === SHARED_COUNTER_KEY) {
        setSharedCount(Number(event.newValue ?? "0") || 0);
      }
    };

    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  const navigate = useCallback((next: ViewKey) => setView(next), []);

  const refreshAll = useCallback(() => {
    runtimeMetrics.refresh();
    if (bridgeAvailable) {
      void snapshot.invoke().catch(() => undefined);
    }
  }, [runtimeMetrics, snapshot, bridgeAvailable]);

  const invokeGreeting = useCallback(() => {
    if (!bridgeAvailable) {
      pushEvent("note", "浏览器预览：启动 Avalonia 宿主后才能调用 C# HostApi。");
      return;
    }
    void greeting
      .invoke("AvaloniaWebview.React")
      .then(result => pushEvent("call", "useHostApiInvoke.getGreeting → " + result))
      .catch(reason => pushEvent("note", "问候调用失败：" + errorMessage(reason)));
  }, [bridgeAvailable, greeting, pushEvent]);

  const callProxy = useCallback(async () => {
    if (!bridgeAvailable) {
      pushEvent("note", "浏览器预览：启动 Avalonia 宿主后才能调用 C# HostApi。");
      return;
    }
    try {
      const result = await hostApi.getGreeting("useHostApi");
      setProxyGreeting(result);
      pushEvent("call", "useHostApi.getGreeting → " + result);
    } catch (reason) {
      const message = errorMessage(reason);
      setProxyGreeting(message);
      pushEvent("note", "代理调用失败：" + message);
    }
  }, [bridgeAvailable, hostApi, pushEvent]);

  const sendMessageToHost = useCallback(() => {
    if (!bridgeAvailable) {
      pushEvent("note", "浏览器预览：消息未发送，启动宿主后即可看到往返。");
      return;
    }
    try {
      postMessage("react:clicked", { at: new Date().toISOString() });
      pushEvent("call", "React → .NET · react:clicked");
    } catch (reason) {
      pushEvent("note", "消息发送失败：" + errorMessage(reason));
    }
  }, [bridgeAvailable, pushEvent]);

  const incrementShared = useCallback(() => {
    setSharedCount(current => {
      const next = current + 1;
      try {
        window.localStorage.setItem(SHARED_COUNTER_KEY, String(next));
      } catch {
        // Storage can be disabled in private or ephemeral windows.
      }
      return next;
    });
  }, []);

  const clearEvents = useCallback(() => setEvents([]), []);

  const effectiveTheme = theme ?? systemTheme;
  const themeLabel =
    effectiveTheme === "dark" ? "深色" : effectiveTheme === "light" ? "浅色" : bridgeAvailable ? "同步中" : "跟随系统";

  return (
    <Layout className="app-shell">
      <Sider className="app-sider">
        <div className="brand">
          <Avatar color="blue" size="small">
            A
          </Avatar>
          <div className="brand-copy">
            <Text strong>Avalonia · React</Text>
            <Text className="brand-sub" type="tertiary" size="small">
              Hybrid Workbench
            </Text>
          </div>
        </div>

        <Nav
          className="app-nav"
          selectedKeys={[view]}
          items={NAV_ITEMS}
          onSelect={({ itemKey }) => setView(itemKey as ViewKey)}
        />

        <div className="sider-footer">
          <Text strong type="tertiary" size="small">
            连接状态
          </Text>
          <div className="sider-row">
            <Text type="tertiary" size="small">
              桥接
            </Text>
            <Tag size="small" color={bridgeAvailable ? "green" : "grey"}>
              {bridgeAvailable ? "已连接" : "浏览器预览"}
            </Tag>
          </div>
          <div className="sider-row">
            <Text type="tertiary" size="small">
              主题
            </Text>
            <Text size="small">{themeLabel}</Text>
          </div>
          <div className="sider-row">
            <Text type="tertiary" size="small">
              采样
            </Text>
            <Text size="small">{runtimeMetrics.source === "host" ? "2s · 宿主" : "2s · 预览"}</Text>
          </div>
          <Text type="tertiary" size="small">
            AvaloniaWebview.React {APP_VERSION}
          </Text>
        </div>
      </Sider>

      <Layout>
        <Header className="app-header">
          <div className="breadcrumb">
            <Text type="tertiary" size="small">
              工作台
            </Text>
            <Text type="tertiary" size="small">
              /
            </Text>
            <Text strong size="small">
              {VIEW_META[view].title}
            </Text>
          </div>
          <Space align="center">
            <Tag size="small" color={bridgeAvailable ? "green" : "grey"}>
              {bridgeAvailable ? "Avalonia WebView" : "浏览器预览"}
            </Tag>
            <Text type="tertiary" size="small">
              {themeLabel}
            </Text>
            <Avatar size="small" color="orange">
              AR
            </Avatar>
          </Space>
        </Header>

        <Content className="app-content">
          <div className="page">
            {view === "overview" ? (
              <OverviewView
                metrics={runtimeMetrics.metrics}
                source={runtimeMetrics.source}
                metricsError={runtimeMetrics.error}
                greeting={greeting}
                snapshot={snapshot}
                bridgeAvailable={bridgeAvailable}
                proxyGreeting={proxyGreeting}
                ackText={events.find(event => event.kind === "ack")?.text ?? null}
                onRefresh={refreshAll}
                onGreeting={invokeGreeting}
                onProxyCall={() => void callProxy()}
                onSendMessage={sendMessageToHost}
                onNavigate={navigate}
              />
            ) : null}

            {view === "playground" ? (
              <PlaygroundView
                bridgeAvailable={bridgeAvailable}
                themeLabel={themeLabel}
                greeting={greeting}
                snapshot={snapshot}
                events={events}
                proxyGreeting={proxyGreeting}
                sharedCount={sharedCount}
                onGreeting={invokeGreeting}
                onProxyCall={() => void callProxy()}
                onSendMessage={sendMessageToHost}
                onRefreshSnapshot={() => void snapshot.invoke().catch(() => undefined)}
                onIncrementShared={incrementShared}
                onClearEvents={clearEvents}
              />
            ) : null}

            {view === "about" ? <AboutView /> : null}

            <footer className="page-footer">
              <Text type="tertiary" size="small">
                AvaloniaWebview.React {APP_VERSION} · 数据来自 IHostApi · 每 2 秒采样
              </Text>
              <Text type="tertiary" size="small">
                {bridgeAvailable ? "运行于 Avalonia NativeWebView" : "浏览器预览 · 启动宿主获取真实指标"}
              </Text>
            </footer>
          </div>
        </Content>
      </Layout>
    </Layout>
  );
}
