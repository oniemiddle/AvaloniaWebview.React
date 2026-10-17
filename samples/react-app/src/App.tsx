import { lazy, Suspense, useEffect, useState } from "react";
import "@douyinfe/semi-ui/react19-adapter";
import { Avatar, Button, Card, Col, Layout, Nav, Row, Space, Tag, Typography } from "@douyinfe/semi-ui";
import { invokeHostApi, postMessage } from "@avalonia-react/bridge";

const ServiceTable = lazy(() => import("./ServiceTable"));

const { Header, Sider, Content } = Layout;
const { Title, Text } = Typography;

export default function App() {
  const [greeting, setGreeting] = useState("等待 Avalonia 宿主连接…");
  const [clicks, setClicks] = useState(0);
  const [apiResult, setApiResult] = useState("");
  const [showServiceDetails, setShowServiceDetails] = useState(false);

  useEffect(() => {
    let disposed = false;
    void invokeHostApi<string>("HostApi", "getGreeting", "Semi Design").then(
      message => { if (!disposed) setGreeting(message); },
      () => { if (!disposed) setGreeting("Avalonia 宿主已连接"); },
    );
    return () => { disposed = true; };
  }, []);

  useEffect(() => {
    if (typeof window.invokeCSharpAction !== "function") return;

    let canceled = false;
    let frame = window.requestAnimationFrame(() => {
      frame = window.requestAnimationFrame(() => {
        if (!canceled) {
          postMessage("react:shell-ready", { frontend: "React 19" });
        }
      });
    });

    return () => {
      canceled = true;
      window.cancelAnimationFrame(frame);
    };
  }, []);

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
          <Avatar color="orange" size="small">AR</Avatar>
        </Header>
        <Content className="app-content">
          <div className="page-heading">
            <div>
              <Text type="tertiary">DEVELOPER TOOLKIT</Text>
              <Title heading={3}>联合开发控制台</Title>
              <Text type="tertiary">{greeting}</Text>
            </div>
            <Space>
              <Button onClick={() => setClicks((value) => value + 1)}>React 点击：{clicks}</Button>
              <Button onClick={async () => setApiResult(await invokeHostApi<string>("HostApi", "getGreeting", "Semi Design"))}>
                调用生成的 .NET API
              </Button>
              <Button theme="solid" type="primary" onClick={() => postMessage("react:clicked", { at: new Date().toISOString() })}>
                发送消息到 .NET
              </Button>
            </Space>
          </div>
          <Row gutter={16} className="stats-row">
            <Col span={8}><Card><Text type="tertiary">宿主框架</Text><Title heading={4}>Avalonia 12</Title></Card></Col>
            <Col span={8}><Card><Text type="tertiary">前端框架</Text><Title heading={4}>React 19</Title></Card></Col>
            <Col span={8}><Card><Text type="tertiary">UI 组件库</Text><Title heading={4}>Semi Design</Title></Card></Col>
          </Row>
          <Card title="本地开发服务" className="service-card" headerExtraContent={<Tag color="green">全部正常</Tag>}>
            {showServiceDetails
              ? <Suspense fallback={null}><ServiceTable /></Suspense>
              : <Button theme="borderless" onClick={() => setShowServiceDetails(true)}>查看服务明细</Button>}
          </Card>
          <Card title="原生消息桥" className="bridge-card">
            <Space vertical align="start">
              <Text>React 与 Avalonia 通过 JSON 消息进行双向通信。</Text>
              <Text type="tertiary">Vite 热更新直接显示在原生 WebView 中。</Text>
              {apiResult && <Text>{apiResult}</Text>}
            </Space>
          </Card>
        </Content>
      </Layout>
    </Layout>
  );
}
