import type { ReactNode } from "react";
import { InfoIcon } from "./Icons";

export function Notice({ tone = "info", title, children }: { tone?: "info" | "warning"; title: string; children: ReactNode }) {
  return (
    <div className={tone === "warning" ? "notice is-warning" : "notice"}>
      <span className="notice-icon">
        <InfoIcon size={16} />
      </span>
      <div className="notice-body">
        <span className="notice-title">{title}</span>
        <span className="notice-text">{children}</span>
      </div>
    </div>
  );
}
