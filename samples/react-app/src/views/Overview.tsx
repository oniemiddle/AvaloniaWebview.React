import { lazy, Suspense, useState, type ReactNode } from "react";
import { Button, Card, Col, Progress, Row, Space, Tag, Typography } from "@douyinfe/semi-ui";
import type { HostApiInvocation } from "@avalonia-react/bridge/react";
import type { RuntimeMetrics, RuntimeSnapshot } from "../FrontendApi";
import type { MetricsSource } from "../useRuntimeMetrics";
import type { ViewKey } from "../types";
import {
  ClockIcon,
  CpuIcon,
  LayersIcon,
  MemoryIcon,
  RefreshIcon,
  SendIcon,
} from "../components/Icons";
import { formatBytes, formatClock, formatPercent, formatUptime, splitBytes } from "../format";
import { services } from "../services";
import { Notice } from "../components/Notice";

const ServiceTable = lazy(() => import("../ServiceTable"));

const { Text, Title, Paragraph } = Typography;

interface OverviewProps {
  metrics: RuntimeMetrics | null;
  source: MetricsSource;
  metricsError: string | null;
  greeting: HostApiInvocation<string>;
  snapshot: HostApiInvocation<RuntimeSnapshot>;
  bridgeAvailable: boolean;
  proxyGreeting: string;
  ackText: string | null;
  onRefresh: () => void;
  onGreeting: () => void;
  onProxyCall: () => void;
  onSendMessage: () => void;
  onNavigate: (view: ViewKey) => void;
}

const HEALTH_LABELS: Record<string, string> = {
  healthy: "运行正常",
  degraded: "降级运行",
};

function StatCard({
  icon,
  tone,
  label,
  value,
  unit,
  meta,
  progress,
}: {
  icon: ReactNode;
  tone?: "violet" | "teal" | "amber";
  label: string;
  value: string;
  unit?: string;
  meta: string;
  progress?: number;
}) {
  return (
    <Card className="stat-card">
      <div className="stat-head">
        <span className={tone ? "stat-icon tone-" + tone : "stat-icon"}>{icon}</span>
        <Text type="tertiary" size="small">
          {label}
        </Text>
      </div>
      <Title heading={3} className="stat-value">
        {value}
        {unit ? <span className="stat-unit">{unit}</span> : null}
      </Title>
      <Text type="tertiary" size="small" className="stat-meta">
        {meta}
      </Text>
      {progress !== undefined ? <Progress percent={Math.max(2, progress)} showInfo={false} size="small" /> : null}
    </Card>
  );
}

export function OverviewView({
  metrics,
  source,
  metricsError,
  greeting,
  snapshot,
  bridgeAvailable,
  proxyGreeting,
  ackText,
  onRefresh,
  onGreeting,
  onProxyCall,
  onSendMessage,
  onNavigate,
}: OverviewProps) {
  const [servicesExpanded, setServicesExpanded] = useState(false);

  const healthKey = String(snapshot.data?.health ?? "unknown");
  const healthLabel = HEALTH_LABELS[healthKey] ?? (bridgeAvailable ? "检查中" : "浏览器预览");
  const healthColor = healthKey === "healthy" ? "green" : healthKey === "degraded" ? "orange" : "grey";

  const workingSet = splitBytes(metrics?.workingSetBytes ?? 0);
  const gcHeap = splitBytes(metrics?.gcHeapBytes ?? 0);
  const uptime = formatUptime(metrics?.uptimeSeconds ?? 0);
  const memoryPercent = metrics
    ? Number(formatPercent(metrics.workingSetBytes, metrics.totalMemoryBytes).replace("%", ""))
    : 0;
  const capturedAt = metrics?.capturedAt ?? null;
  const startedAt =
    capturedAt !== null
      ? new Date(new Date(capturedAt).getTime() - (metrics?.uptimeSeconds ?? 0) * 1000).toISOString()
      : null;

  const tags = snapshot.data?.tags?.length ? snapshot.data.tags : ["shared-session", "aot-json", "host-api"];

  const tiles = [
    { label: "操作系统", value: metrics?.os ?? "读取中…" },
    { label: ".NET 运行时", value: metrics?.runtime ?? "读取中…" },
    { label: "Avalonia", value: metrics?.avalonia ?? "读取中…" },
    { label: "WebView 引擎", value: metrics?.webView ?? "读取中…" },
    { label: "设备", value: metrics ? metrics.machine + " · " + metrics.processorCount + " 核" : "读取中…" },
    { label: "内存上限", value: metrics ? formatBytes(metrics.totalMemoryBytes) : "读取中…" },
    { label: "采样时间", value: formatClock(metrics?.capturedAt) },
    { label: "数据来源", value: source === "host" ? "C# HostApi" : "浏览器预览" },
  ];

  const greetingText = !bridgeAvailable
    ? "浏览器预览：启动 Avalonia 宿主后即可调用 C# HostApi。"
    : greeting.loading
      ? "正在向 C# 请求问候…"
      : greeting.error
        ? "宿主调用失败：" + greeting.error.message
        : greeting.data ?? "等待宿主连接…";

  return (
    <>
      <div className="page-hero">
        <div>
          <Text type="tertiary" size="small" strong>
            DEVELOPER TOOLKIT · 0.19
          </Text>
          <Title heading={3} className="page-title">
            运行概览
          </Title>
          <Paragraph className="page-lead" type="tertiary">
            宿主与 .NET 运行时的实时指标，经 JSON-RPC 桥每 2 秒同步到 React。
          </Paragraph>
        </div>
        <Space>
          <Button icon={<RefreshIcon size={14} />} onClick={onRefresh}>
            刷新指标
          </Button>
          <Button theme="solid" type="primary" onClick={() => onNavigate("playground")}>
            桥接实验
          </Button>
        </Space>
      </div>

      {source === "preview" ? (
        <Notice title="浏览器预览模式">
          当前页面未连接 Avalonia 宿主，卡片中的数值来自浏览器运行时；启动宿主后会自动切换为真实的 .NET 指标。
        </Notice>
      ) : null}

      {metricsError ? (
        <Notice tone="warning" title="宿主指标读取失败">
          {metricsError}
        </Notice>
      ) : null}

      <Row type="flex" gutter={[16, 16]}>
        <Col xs={24} sm={12} xl={6}>
          <StatCard
            icon={<MemoryIcon size={17} />}
            label="工作集 · Working Set"
            value={workingSet.value}
            unit={workingSet.unit}
            meta={"占内存上限 " + formatPercent(metrics?.workingSetBytes ?? 0, metrics?.totalMemoryBytes ?? 0)}
            progress={memoryPercent}
          />
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatCard
            icon={<LayersIcon size={17} />}
            tone="violet"
            label="GC 堆 · Managed Heap"
            value={gcHeap.value}
            unit={gcHeap.unit}
            meta="托管堆 · GC.GetTotalMemory"
          />
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatCard
            icon={<CpuIcon size={17} />}
            tone="teal"
            label="托管线程 · Threads"
            value={String(metrics?.threadCount ?? "—")}
            unit="个"
            meta={metrics ? metrics.processorCount + " 个逻辑核心" : "读取中…"}
          />
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatCard
            icon={<ClockIcon size={17} />}
            tone="amber"
            label="运行时长 · Uptime"
            value={uptime}
            meta={startedAt ? "启动于 " + formatClock(startedAt) : "读取中…"}
          />
        </Col>
      </Row>

      <Row type="flex" gutter={[16, 16]}>
        <Col xs={24} xl={14}>
          <Card
            title="宿主与运行时"
            headerExtraContent={<Tag color={healthColor}>{healthLabel}</Tag>}
            style={{ height: "100%" }}
          >
            <div className="tile-grid">
              {tiles.map(tile => (
                <div className="tile" key={tile.label}>
                  <span className="tile-label">{tile.label}</span>
                  <span className="tile-value">{tile.value}</span>
                </div>
              ))}
            </div>
            <div className="card-foot">
              <Text type="tertiary" size="small">
                宿主标签
              </Text>
              <Space wrap spacing={6}>
                {tags.map(tag => (
                  <Tag key={tag} size="small" color={snapshot.data?.tags?.length ? "blue" : "grey"}>
                    {tag}
                  </Tag>
                ))}
              </Space>
            </div>
          </Card>
        </Col>

        <Col xs={24} xl={10}>
          <Card
            title="桥接演练"
            headerExtraContent={<Tag color="blue">2s 轮询</Tag>}
            style={{ height: "100%" }}
            bodyStyle={{ display: "flex", flexDirection: "column", gap: 16 }}
          >
            <Space vertical align="start" spacing={12} style={{ width: "100%" }}>
              <div className="bridge-line">
                <span className="bridge-node">React 19</span>
                <span className="bridge-link" />
                <span className="bridge-node">JSON-RPC</span>
                <span className="bridge-link" />
                <span className="bridge-node">.NET</span>
              </div>
              <Text>{greetingText}</Text>
              <Space wrap>
                <Button size="small" onClick={onGreeting} disabled={!bridgeAvailable}>
                  重新问候
                </Button>
                <Button size="small" onClick={onProxyCall} disabled={!bridgeAvailable}>
                  代理直调
                </Button>
                <Button size="small" theme="solid" type="primary" icon={<SendIcon size={13} />} onClick={onSendMessage}>
                  发送消息到 .NET
                </Button>
              </Space>
              {proxyGreeting ? (
                <Text className="demo-result" size="small">
                  useHostApi 代理：{proxyGreeting}
                </Text>
              ) : null}
              {ackText ? (
                <Text className="demo-result" size="small">
                  {ackText}
                </Text>
              ) : null}
              <Text type="tertiary" size="small">
                点击原生窗口顶部的「宿主 → React」，.NET 会调用 useReactHandler("host:greet") 注册的处理器，并把结果写回原生状态栏。
              </Text>
            </Space>
            <div className="card-foot" style={{ marginTop: "auto" }}>
              <Text type="tertiary" size="small">
                传输
              </Text>
              <Text size="small">JSON-RPC</Text>
              <Text type="tertiary" size="small">
                数据源
              </Text>
              <Text size="small">{source === "host" ? "C# HostApi" : "浏览器预览"}</Text>
            </div>
          </Card>
        </Col>
      </Row>

      <Card
        title="本地开发服务"
        headerExtraContent={
          <Space>
            <Tag color="green">全部正常</Tag>
            <Button size="small" theme="borderless" onClick={() => setServicesExpanded(value => !value)}>
              {servicesExpanded ? "收起明细" : "查看明细"}
            </Button>
          </Space>
        }
      >
        {servicesExpanded ? (
          <Suspense fallback={<Text type="tertiary">正在加载表格模块…</Text>}>
            <ServiceTable />
          </Suspense>
        ) : (
          <Space wrap spacing={8}>
            {services.map(service => (
              <Tag key={service.key} color="green" size="small">
                {service.service} · {service.latency}
              </Tag>
            ))}
            <Text type="tertiary" size="small">
              展开明细时才会下载独立的 React 代码块。
            </Text>
          </Space>
        )}
      </Card>

      <Notice title="界面之外：一次指标更新发生了什么？">
        卡片中的每个数值都来自宿主进程。React 通过类型化契约调用 IHostApi.getRuntimeMetrics()，.NET 采集后以生成的 DTO
        返回；主题、就绪握手与宿主回调走同一座桥，因此在 Native AOT 下同样可用。
      </Notice>
    </>
  );
}
