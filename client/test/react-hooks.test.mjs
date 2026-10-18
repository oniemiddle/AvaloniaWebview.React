import assert from "node:assert/strict";
import test from "node:test";
import { createElement } from "react";
import { renderToString } from "react-dom/server";
import * as hooks from "../dist/react.js";

test("react hooks render without a bridge window", () => {
  function Probe() {
    const api = hooks.useHostApi("HostApi");
    const available = hooks.useBridgeAvailable();
    const theme = hooks.useHostTheme();
    const sent = hooks.useSignalReady();
    const invocation = hooks.useHostApiInvoke("HostApi", "getGreeting");
    hooks.useAvaloniaMessage("probe", () => {});

    return createElement(
      "span",
      null,
      [
        typeof api.getGreeting,
        available,
        theme ?? "none",
        sent,
        typeof invocation.invoke,
        typeof invocation.cancel,
        invocation.loading,
      ].join("|"),
    );
  }

  const html = renderToString(createElement(Probe));
  assert.equal(html, "<span>function|false|none|false|function|function|false</span>");
});
