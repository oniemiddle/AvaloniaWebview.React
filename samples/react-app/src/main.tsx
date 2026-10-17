import { createRoot } from "react-dom/client";
import { postMessage, subscribe } from "@avalonia-react/bridge";
import App from "./App";
import "./style.css";

function applyTheme(theme: "light" | "dark") {
  document.documentElement.dataset.theme = theme;
  if (theme === "dark") {
    document.body.setAttribute("theme-mode", "dark");
  } else {
    document.body.removeAttribute("theme-mode");
  }
}

subscribe<{ theme: "light" | "dark" }>("theme", ({ theme }) => applyTheme(theme));

if (typeof window.invokeCSharpAction === "function") {
  postMessage("theme:request", {});
} else if (window.matchMedia("(prefers-color-scheme: dark)").matches) {
  applyTheme("dark");
}

const root = document.getElementById("root");
if (root) {
  createRoot(root).render(<App />);
}
