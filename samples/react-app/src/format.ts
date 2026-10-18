export function splitBytes(bytes: number): { value: string; unit: string } {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return { value: "0", unit: "B" };
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  const digits = value >= 100 ? 0 : 1;
  return { value: value.toFixed(digits), unit: units[unitIndex] };
}

export function formatBytes(bytes: number): string {
  const { value, unit } = splitBytes(bytes);
  return value + " " + unit;
}

export function formatUptime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return "0s";
  }

  const total = Math.floor(seconds);
  const days = Math.floor(total / 86400);
  const hours = Math.floor((total % 86400) / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const remainingSeconds = total % 60;

  if (days > 0) {
    return days + "d " + hours + "h";
  }
  if (hours > 0) {
    return hours + "h " + String(minutes).padStart(2, "0") + "m";
  }
  if (minutes > 0) {
    return minutes + "m " + String(remainingSeconds).padStart(2, "0") + "s";
  }
  return remainingSeconds + "s";
}

export function formatClock(value: string | number | Date | null | undefined): string {
  if (value === null || value === undefined || value === "") {
    return "—";
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "—";
  }
  return date.toLocaleTimeString("zh-CN", { hour12: false });
}

export function formatPercent(part: number, total: number): string {
  if (!Number.isFinite(part) || !Number.isFinite(total) || total <= 0) {
    return "0%";
  }
  const ratio = (part / total) * 100;
  return (ratio >= 10 ? ratio.toFixed(0) : ratio.toFixed(1)) + "%";
}

export function clampRatio(part: number, total: number): number {
  if (!Number.isFinite(part) || !Number.isFinite(total) || total <= 0) {
    return 0;
  }
  return Math.min(1, Math.max(0.02, part / total));
}
