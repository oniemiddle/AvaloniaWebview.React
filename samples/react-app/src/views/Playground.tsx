import type { ReactNode } from "react";
import { Button, Card, Col, Divider, Empty, Row, Space, Tag, Typography } from "@douyinfe/semi-ui";
import type { HostApiInvocation } from "@avalonia-react/bridge/react";
import type { RuntimeSnapshot } from "../FrontendApi";
import type { BridgeEvent, BridgeEventKind } from "../types";
import { Notice } from "../components/Notice";

const { Text, Title, Paragraph } = Typography;

interface PlaygroundProps {
  bridgeAvailable: boolean;
  themeLabel: string;
  greeting: HostApiInvocation<string>;
  snapshot: HostApiInvocation<RuntimeSnapshot>;
  events: BridgeEvent[];
  proxyGreeting: string;
  sharedCount: number;
  onGreeting: () => void;
  onProxyCall: () => void;
  onSendMessage: () => void;
  onRefreshSnapshot: () => void;
  onIncrementShared: () => void;
  onClearEvents: () => void;
}

const HEALTH_LABELS: Record<string, string> = {
  healthy: "运行正常",
  degraded: "降级运行",
};

const EVENT_COLORS: Record<BridgeEventKind, "green" | "blue" | "orange" | "grey"> = {
  ack: "green",
  call: "blue",
  host: "orange",
  note: "grey",
};

function DemoBlock({
  method,
  title,
  description,
  result,
  action,
}: {
  method: string;
  title: string;
  description: string;
  result?: string;
  action?: ReactNode;
}) {
  return (
    <div className="demo-block">
      <Space wrap spacing={8}>
        <Text className="mono" strong>
          {method}
        </Text>
        <Text strong>{title}</Text>
      </Space>
      <Text type="tertiary" size="small">
        {description}
      </Text>
      {result ? (
        <Text className="demo-result" size="small">
          {result}
        </Text>
      ) : null}
      {action}
    </div>
  );
}

export function PlaygroundView({
  bridgeAvailable,
  themeLabel,
  greeting,
  snapshot,
  events,
  proxyGreeting,
  sharedCount,
  onGreeting,
  onProxyCall,
  onSendMessage,
  onRefreshSnapshot,
  onIncrementShared,
  onClearEvents,
}: PlaygroundProps) {
  const healthKey = String(snapshot.data?.health ?? "unknown");
  const healthLabel = HEALTH_LABELS[healthKey] ?? (bridgeAvailable ? "检查中" : "浏览器预览");
  const healthColor = healthKey === "healthy" ? "green" : healthKey === "degraded" ? "orange" : "grey";
  const tags = snapshot.data?.tags?.length ? snapshot.data.tags : ["shared-session", "aot-json", "host-api"];
  const lastAck = [...events].reverse().find(event => event.kind === "ack");

  const greetingResult = greeting.loading
    ? "调用中…"
    : greeting.error
      ? greeting.error.message
      : greeting.data ?? "尚未调用";

  return (
    <>
      <div className="page-hero">
        <div>
          <Text type="tertiary" size="small" strong>
            交互演示 · BRIDGE LAB
          </Text>
          <Title heading={3} className="page-title">
            桥接实验
          </Title>
          <Paragraph className="page-lead" type="tertiary">
            在这里体验 Hook 调用、类型化代理、React 到 .NET 的消息，以及 .NET 调用 React 的回调；每条路径都会记录到事件流。
          </Paragraph>
        </div>
      </div>

      <Row type="flex" gutter={[16, 16]}>
        <Col xs={24} xl={14}>
          <Card
            title="调用与消息"
            headerExtraContent={
              <Text type="tertiary" size="small">
                {events.length} 条事件
              </Text>
            }
            style={{ height: "100%" }}
          >
            <Space vertical align="start" spacing={16} style={{ width: "100%" }}>
              <DemoBlock
                method="useHostApiInvoke"
                title="Hook 托管调用"
                description="Hook 管理 data / error / loading，并自动取消上一次未完成的调用。"
                result={greetingResult}
                action={
                  <Button size="small" onClick={onGreeting} disabled={!bridgeAvailable}>
                    重新问候
                  </Button>
                }
              />
              <DemoBlock
                method="useHostApi"
                title="稳定代理对象"
                description="代理按契约生成，可直接 await 调用；失败通过 Promise 抛出，便于自行处理。"
                result={proxyGreeting || "点击按钮发起一次直接调用。"}
                action={
                  <Button size="small" onClick={onProxyCall} disabled={!bridgeAvailable}>
                    代理直调
                  </Button>
                }
              />
              <DemoBlock
                method="postMessage"
                title="React → 宿主消息"
                description="发送 react:clicked，宿主收到后回发 react:ack，并在原生状态栏显示回执。"
                result={lastAck ? lastAck.text : "尚未收到宿主回执。"}
                action={
                  <Button size="small" theme="solid" type="primary" onClick={onSendMessage}>
                    发送消息
                  </Button>
                }
              />
              <DemoBlock
                method="useReactHandler"
                title="宿主 → React 回调"
                description="点击原生窗口顶部的「宿主 → React」，.NET 会调用已注册的处理器，并把返回值写到原生状态栏。"
              />
            </Space>

            <Divider margin={16} />

            <div className="sider-row" style={{ marginBottom: 10 }}>
              <Text strong>事件流</Text>
              <Button size="small" theme="borderless" onClick={onClearEvents} disabled={events.length === 0}>
                清空
              </Button>
            </div>
            {events.length === 0 ? (
              <Empty title="还没有消息" description="点击上方按钮，观察 React 与 .NET 之间的往返。" />
            ) : (
              <ul className="event-list">
                {[...events].reverse().map(event => (
                  <li className="event-item" key={event.id}>
                    <Tag size="small" color={EVENT_COLORS[event.kind]}>
                      {event.kind}
                    </Tag>
                    <span>{event.text}</span>
                    <span className="event-time">{event.at}</span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </Col>

        <Col xs={24} xl={10}>
          <Space vertical align="start" spacing={16} style={{ width: "100%" }}>
            <Card
              title="共享浏览器存储"
              headerExtraContent={<Tag size="small">localStorage</Tag>}
              style={{ width: "100%" }}
            >
              <Space vertical align="start" spacing={10}>
                <Title heading={2} style={{ margin: 0 }}>
                  {sharedCount}
                </Title>
                <Text type="tertiary" size="small">
                  默认共享会话的窗口读取同一份 localStorage；以隔离会话打开的窗口互不影响，刷新页面后计数仍然保留。
                </Text>
                <Button theme="solid" type="primary" onClick={onIncrementShared}>
                  计数 +1
                </Button>
              </Space>
            </Card>

            <Card
              title="运行时快照"
              headerExtraContent={<Tag color={healthColor}>{healthLabel}</Tag>}
              style={{ width: "100%" }}
            >
              <Space vertical align="start" spacing={8} style={{ width: "100%" }}>
                <div className="sider-row" style={{ width: "100%" }}>
                  <Text type="tertiary" size="small">
                    宿主
                  </Text>
                  <Text size="small">{snapshot.data?.host ?? "—"}</Text>
                </div>
                <div className="sider-row" style={{ width: "100%" }}>
                  <Text type="tertiary" size="small">
                    WebView
                  </Text>
                  <Text size="small">{snapshot.data?.webView ?? "—"}</Text>
                </div>
                <div className="sider-row" style={{ width: "100%" }}>
                  <Text type="tertiary" size="small">
                    主题
                  </Text>
                  <Text size="small">{themeLabel}</Text>
                </div>
                <Space wrap spacing={6}>
                  {tags.map(tag => (
                    <Tag key={tag} size="small" color={snapshot.data?.tags?.length ? "blue" : "grey"}>
                      {tag}
                    </Tag>
                  ))}
                </Space>
                <Button size="small" onClick={onRefreshSnapshot} disabled={!bridgeAvailable}>
                  刷新快照
                </Button>
              </Space>
            </Card>
          </Space>
        </Col>
      </Row>

      <Notice tone="warning" title="安全边界：宿主 API 只信任受控来源">
        宿主 API 默认只接受启动应用后自动信任的页面来源。TrustedApiOrigins 追加允许调用宿主 API
        的来源，AllowedOrigins 只授予导航能力；主框架跳转到 file:、data: 或 javascript: 等非 HTTP(S) 地址时始终会被取消。
      </Notice>
    </>
  );
}
