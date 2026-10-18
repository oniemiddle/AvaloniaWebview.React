export type ViewKey = "overview" | "playground" | "about";

export type BridgeEventKind = "ack" | "call" | "host" | "note";

export interface BridgeEvent {
  id: number;
  at: string;
  kind: BridgeEventKind;
  text: string;
}

export const VIEW_META: Record<ViewKey, { title: string; subtitle: string }> = {
  overview: { title: "运行概览", subtitle: "实时桥接监控" },
  playground: { title: "桥接实验", subtitle: "调用与消息" },
  about: { title: "关于示例", subtitle: "技术栈与运行方式" },
};
