import { Card, Col, List, Row, Tag, Typography } from "@douyinfe/semi-ui";
import { Notice } from "../components/Notice";
import { CheckIcon } from "../components/Icons";

const { Text, Title, Paragraph } = Typography;

const STACK = [
  { name: "Avalonia", version: "12.1", note: "桌面 UI 与原生窗口" },
  { name: ".NET", version: "10", note: "宿主运行时与 HostApi" },
  { name: "React", version: "19", note: "界面与 Hook 状态" },
  { name: "Semi Design", version: "2.92", note: "组件库与主题" },
  { name: "Vite", version: "6", note: "开发服务器与构建" },
  { name: "@avalonia-react/bridge", version: "0.19", note: "类型化 JSON-RPC 桥" },
];

const CAPABILITIES = [
  "由 .d.ts 契约生成 C# 接口、DTO 与 TypeScript 代理",
  "宿主 → React 的 Promise 回调（useReactHandler / InvokeReactAsync）",
  "主题同步、就绪握手与加载遮罩",
  "Debug 走 Vite，Release 使用嵌入式资源",
  "Native AOT 下无反射的 JSON 序列化",
  "多窗口共享会话与隔离会话",
  "React.lazy 代码分割与组件库按需加载",
];

export function AboutView() {
  return (
    <>
      <div className="page-hero">
        <div>
          <Text type="tertiary" size="small" strong>
            HYBRID WORKBENCH
          </Text>
          <Title heading={3} className="page-title">
            关于本示例
          </Title>
          <Paragraph className="page-lead" type="tertiary">
            一个可运行的 Avalonia + React 混合桌面工作台：界面由 Web 技术构建，数据与能力来自 .NET 宿主，两侧通过类型化契约保持同步。
          </Paragraph>
        </div>
      </div>

      <Row type="flex" gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title="技术栈" style={{ height: "100%" }}>
            <List
              dataSource={STACK}
              split={false}
              renderItem={item => (
                <List.Item>
                  <div className="sider-row" style={{ width: "100%" }}>
                    <div>
                      <Text strong>{item.name}</Text>
                      <br />
                      <Text type="tertiary" size="small">
                        {item.note}
                      </Text>
                    </div>
                    <Tag size="small">{item.version}</Tag>
                  </div>
                </List.Item>
              )}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card
            title="已演示的能力"
            headerExtraContent={<Tag color="blue">0.19.0</Tag>}
            style={{ height: "100%" }}
          >
            <List
              dataSource={CAPABILITIES}
              split={false}
              renderItem={item => (
                <List.Item>
                  <Text type="tertiary" size="small" style={{ display: "inline-flex", gap: 6 }}>
                    <CheckIcon size={13} />
                    {item}
                  </Text>
                </List.Item>
              )}
            />
          </Card>
        </Col>
      </Row>

      <Card title="运行示例" headerExtraContent={<Tag>Debug · Vite</Tag>}>
        <pre className="code-block">
          <div>$ npm --prefix samples/react-app install</div>
          <div>$ dotnet run --project samples/ReactHost -f net10.0-windows</div>
        </pre>
        <Paragraph type="tertiary" style={{ marginTop: 12, marginBottom: 0 }}>
          Release 构建会把前端产物嵌入程序集并通过本机回环地址提供；Windows 上还可以发布为 Native AOT 可执行文件，启动时无需安装 .NET 运行时。
        </Paragraph>
      </Card>

      <Notice title="想看更完整的说明？">
        仓库根目录的 README 覆盖了宿主配置、契约生成、嵌入式资源、多窗口会话、安全边界与 Native AOT 发布；samples
        下的源码就是这份界面的全部实现。
      </Notice>
    </>
  );
}
