import { createRoot } from "react-dom/client";
import { requestTheme, subscribe } from "@avalonia-react/bridge";
import App from "./App";
import "./style.css";

let themeApplied = false;

function applyTheme(theme: "light" | "dark") {
  themeApplied = true;
  document.documentElement.dataset.theme = theme;
  if (theme === "dark") {
    document.body.setAttribute("theme-mode", "dark");
  } else {
    document.body.removeAttribute("theme-mode");
  }
}

function systemTheme(): "light" | "dark" {
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

subscribe<{ theme: "light" | "dark" }>("theme", ({ theme }) => applyTheme(theme));

const root = document.getElementById("root");
if (root) {
  if (typeof window.invokeCSharpAction === "function") {
    requestTheme();

    const startedAt = performance.now();
    const renderWhenThemeIsReady = () => {
      if (themeApplied || performance.now() - startedAt > 300) {
        if (!themeApplied) {
          applyTheme(systemTheme());
        }
        createRoot(root).render(<App />);
      } else {
        requestAnimationFrame(renderWhenThemeIsReady);
      }
    };
    renderWhenThemeIsReady();
  } else {
    applyTheme(systemTheme());
    createRoot(root).render(<App />);
  }
}
