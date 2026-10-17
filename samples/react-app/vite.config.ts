import { defineConfig } from "vite";

export default defineConfig({
  optimizeDeps: {
    // Semi Design has a large module graph. Prepare it before the WebView asks for the app.
    include: [
      "@douyinfe/semi-ui",
      "@douyinfe/semi-ui/react19-adapter",
      "react",
      "react-dom/client",
    ],
  },
  server: {
    // Vite starts before ReactHost; transform the entry while the host is being built/launched.
    warmup: {
      clientFiles: ["./src/main.tsx", "./src/App.tsx"],
    },
  },
});
