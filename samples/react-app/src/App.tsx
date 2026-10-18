import { lazy, Suspense, useEffect, useState } from "react";
import "@douyinfe/semi-ui/react19-adapter";
import { Avatar, Button, Card, Col, Layout, Nav, Row, Space, Tag, Typography } from "@douyinfe/semi-ui";
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
import type { HostApi, RuntimeSnapshot } from "./FrontendApi";

const ServiceTable = lazy(() => import("./ServiceTable"));

const { Header, Sider, Content } = Layout;
const { Title, Text } = Typography;

const SHARED_COUNTER_KEY = "sample:shared-counter";

function readSharedCounter(): number {
  try {
    return Number(window.localStorage.getItem(SHARED_COUNTER_KEY) ?? "0") || 0;
  } catch {
    return 0;
  }
}

export default function App() {
  const hostApi = useHostApi<HostApi>("HostApi");
  const greeting = useHostApiInvoke<string>("HostApi", "getGreeting");
  const snapshot = useHostApiInvoke<RuntimeSnapshot>("HostApi", "getRuntimeSnapshot");
  const theme = useHostTheme();
  const bridgeAvailable = useBridgeAvailable();

  const [clicks, setClicks] = useState(0);
  const [directGreeting, setDirectGreeting] = useState("");
  const [ack, setAck] = useState("");
  const [showServiceDetails, setShowServiceDetails] = useState(false);
  const [sharedCount, setSharedCount] = useState(readSharedCounter);

  // Sends avalonia:ready once after mount so the host can hide its loading overlay.
  useSignalReady({ frontend: "React 19", toolkit: "0.18" });

  // Hook-based subscriptions replace the manual subscribe/signalReady effects.
  useAvaloniaMessage<{ at: string }>("react:ack", ({ at }) => setAck(at));

  // The native toolbar's "宿主 → React" button invokes this handler.
  useReactHandler<string>(
    "host:greet",
    from => `React 已响应「${from}」，当前主题：${theme ?? "unknown"}`,
  );

  useEffect(() => {
    void greeting.invoke("Semi Design");
    void snapshot.invoke();
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

  const incrementSharedCounter = () => {
    setSharedCount(current => {
      const next = current + 1;
      try {
        window.localStorage.setItem(SHARED_COUNTER_KEY, String(next));
      } catch {
        // Storage can be disabled in private/ephemeral windows.
      }
      return next;
    });
  };

  const callProxy = async () => {
    try {
      setDirectGreeting(await hostApi.getGreeting("useHostApi"));
    } catch (error) {
      setDirectGreeting(error instanceof Error ? error.message : String(error));
    }
  };

  const snapshotHealth = snapshot.data?.health;
  const snapshotTagColor = snapshotHealth === "healthy" ? "green" : "orange";

  return (
    <Layout className="app-shell">
      <Sider className="app-sider">
        <div className="brand"><Avatar color="blue" size="small">A</Avatar><span>Avalonia React</span></div>
        <Nav defaultSelectedKeys={["overview"]} items={[
          { itemKey: "overview", text: "工作台" },
          { itemKey: "services", text: "服务状态" },
          { itemKey: "settings", text: "设置" },
        ]} />
      </Sider>
      <Layout>
        <Header className="app-header">
          <Text type="tertiary">桌面应用 / 工作台</Text>
          <Space>
            <Tag color={bridgeAvailable ? "green" : "grey"}>
              {bridgeAvailable ? "Avalonia WebView" : "浏览器预览"}
            </Tag>
            <Tag color="blue">主题：{theme ?? "unknown"}</Tag>
            <Avatar color="orange" size="small">AR</Avatar>
          </Space>
        </Header>
        <Content className="app-content">
          <div className="page-heading">
            <div>
              <Text type="tertiary">DEVELOPER TOOLKIT · 0.18</Text>
              <Title heading={3}>联合开发控制台</Title>
              <Text type="tertiary">
                {greeting.loading
                  ? "正在连接 Avalonia 宿主…"
                  : greeting.error
                    ? `宿主 API 错误：${greeting.error.message}`
                    : greeting.data ?? "等待 Avalonia 宿主连接…"}
              </Text>
            </div>
            <Space>
              <Button onClick={() => setClicks(value => value + 1)}>React 点击：{clicks}</Button>
              <Button onClick={() => void greeting.invoke("Semi Design")}>useHostApiInvoke</Button>
              <Button theme="solid" type="primary" onClick={() => postMessage("react:clicked", { at: new Date().toISOString() })}>
                发送消息到 .NET
              </Button>
            </Space>
          </div>

          <Row gutter={16} className="stats-row">
            <Col span={8}><Card><Text type="tertiary">宿主框架</Text><Title heading={4}>Avalonia 12</Title></Card></Col>
            <Col span={8}><Card><Text type="tertiary">前端框架</Text><Title heading={4}>React 19 Hooks</Title></Card></Col>
            <Col span={8}><Card><Text type="tertiary">UI 组件库</Text><Title heading={4}>Semi Design</Title></Card></Col>
          </Row>

          <Card
            title="运行时快照（DTO / 枚举 / 列表）"
            className="service-card"
            headerExtraContent={snapshotHealth ? <Tag color={snapshotTagColor}>{snapshotHealth}</Tag> : undefined}
          >
            {snapshot.loading && <Text type="tertiary">正在读取 .NET 运行时快照…</Text>}
            {snapshot.error && <Text type="danger">{snapshot.error.message}</Text>}
            {snapshot.data && (
              <Space vertical align="start">
                <Text>宿主：{snapshot.data.host}</Text>
                <Text>WebView：{snapshot.data.webView}</Text>
                <Space>
                  {snapshot.data.tags.map(tag => <Tag key={tag}>{tag}</Tag>)}
                </Space>
                <Button size="small" onClick={() => void snapshot.invoke()}>刷新快照</Button>
              </Space>
            )}
          </Card>

          <Card title="本地开发服务" className="service-card" headerExtraContent={<Tag color="green">全部正常</Tag>}>
            {showServiceDetails
              ? <Suspense fallback={null}><ServiceTable /></Suspense>
              : <Button theme="borderless" onClick={() => setShowServiceDetails(true)}>查看服务明细</Button>}
          </Card>

          <Row gutter={16} className="bridge-card">
            <Col span={14}>
              <Card title="原生桥与 Hooks">
                <Space vertical align="start">
                  <Text>React 与 Avalonia 通过 JSON 消息双向通信。</Text>
                  <Text type="tertiary">
                    useHostApiInvoke 由 React hook 管状态；useHostApi 返回稳定的代理对象。
                  </Text>
                  {greeting.data && <Text>Hook 问候：{greeting.data}</Text>}
                  {greeting.error && <Text type="danger">Hook 错误：{greeting.error.message}</Text>}
                  <Space>
                    <Button size="small" onClick={() => void callProxy()}>useHostApi 代理调用</Button>
                    {directGreeting && <Text type="tertiary">{directGreeting}</Text>}
                  </Space>
                  {ack && <Text type="tertiary">宿主回执：{ack}</Text>}
                  <Text type="tertiary">
                    点击原生工具栏的“宿主 → React”，.NET 会调用 useReactHandler 注册的处理器。
                  </Text>
                </Space>
              </Card>
            </Col>
            <Col span={10}>
              <Card title="共享浏览器存储">
                <Space vertical align="start">
                  <Text>
                    此计数保存在 localStorage。默认共享 session 的窗口读取同一份数据；
                    隔离窗口使用 ephemeral session。
                  </Text>
                  <Title heading={4}>{sharedCount}</Title>
                  <Button onClick={incrementSharedCounter}>共享计数 +1</Button>
                </Space>
              </Card>
            </Col>
          </Row>
        </Content>
      </Layout>
    </Layout>
  );
}
