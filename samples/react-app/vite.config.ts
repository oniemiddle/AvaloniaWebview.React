import { defineConfig } from "vite";

export default defineConfig({
  resolve: {
    // @avalonia-react/bridge is a file: dependency (a junction to ../../client) that carries its own
    // react/react-dom devDependencies for its tests. Without dedupe Vite resolves the bridge's React
    // imports through client/node_modules, bundles a second React copy, and hooks throw
    // "Cannot read properties of null (reading 'useMemo')" at startup.
    dedupe: ["react", "react-dom"],
  },
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
