import { Table, Tag } from "@douyinfe/semi-ui";

const columns = [
  { title: "服务", dataIndex: "service", key: "service" },
  { title: "状态", dataIndex: "status", key: "status", render: () => <Tag color="green">运行中</Tag> },
  { title: "响应时间", dataIndex: "latency", key: "latency" },
];

const data = [
  { key: "1", service: "Avalonia Host Bridge", status: "online", latency: "12 ms" },
  { key: "2", service: "React Renderer", status: "online", latency: "8 ms" },
  { key: "3", service: "Vite Dev Server", status: "online", latency: "24 ms" },
];

export default function ServiceTable() {
  return <Table columns={columns} dataSource={data} pagination={false} size="small" />;
}
