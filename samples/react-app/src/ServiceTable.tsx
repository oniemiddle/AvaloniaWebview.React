import { Space, Table, Tag, Typography } from "@douyinfe/semi-ui";
import type { ComponentProps } from "react";
import { services, type ServiceInfo } from "./services";

const { Text } = Typography;

type TableColumns = NonNullable<ComponentProps<typeof Table>["columns"]>;

const columns: TableColumns = [
  {
    title: "服务",
    dataIndex: "service",
    render: (service: string, row: ServiceInfo) => (
      <Space vertical align="start" spacing={0}>
        <Text strong>{service}</Text>
        <Text type="tertiary" size="small">
          {row.role}
        </Text>
      </Space>
    ),
  },
  {
    title: "状态",
    dataIndex: "status",
    render: () => (
      <Tag color="green" size="small">
        运行中
      </Tag>
    ),
  },
  {
    title: "响应时间",
    dataIndex: "latency",
    align: "right",
    render: (latency: string) => <Text className="mono">{latency}</Text>,
  },
  {
    title: "备注",
    dataIndex: "detail",
    align: "right",
    render: (detail: string) => (
      <Text type="tertiary" size="small">
        {detail}
      </Text>
    ),
  },
];

export default function ServiceTable() {
  return <Table columns={columns} dataSource={services} rowKey="key" pagination={false} size="small" />;
}
