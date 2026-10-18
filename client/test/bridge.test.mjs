import assert from "node:assert/strict";
import test from "node:test";

function createFakeWindow() {
  const listeners = new Map();
  const messages = [];

  const window = {
    messages,
    invokeCSharpAction(body) {
      messages.push(JSON.parse(body));
    },
    addEventListener(type, handler) {
      if (!listeners.has(type)) {
        listeners.set(type, []);
      }
      listeners.get(type).push(handler);
    },
    removeEventListener(type, handler) {
      const handlers = listeners.get(type);
      if (!handlers) return;
      const index = handlers.indexOf(handler);
      if (index >= 0) handlers.splice(index, 1);
    },
    dispatch(type, event) {
      for (const handler of [...(listeners.get(type) ?? [])]) {
        handler(event);
      }
    },
    clearMessages() {
      messages.length = 0;
    },
  };

  return window;
}

const window = createFakeWindow();
globalThis.window = window;

const bridge = await import("../dist/index.js");

function lastMessage(type) {
  return [...window.messages].reverse().find((message) => message.type === type);
}

function completeLastInvocation(result) {
  const message = lastMessage(bridge.bridgeMessageTypes.invoke);
  assert.ok(message, "expected an invoke message");
  window.dispatch("avalonia-react:message", {
    detail: {
      type: bridge.bridgeMessageTypes.invokeResult,
      payload: { requestId: message.payload.requestId, result },
    },
  });
  return message;
}

test("postMessage sends the JSON envelope", () => {
  window.clearMessages();

  bridge.postMessage("ping", { value: 1 });

  assert.deepEqual(window.messages.at(-1), { type: "ping", payload: { value: 1 } });
});

test("postMessage rejects an empty type", () => {
  assert.throws(() => bridge.postMessage(" ", {}), /must not be empty/);
});

test("signalReady sends the reserved ready message", () => {
  window.clearMessages();

  assert.equal(bridge.signalReady({ version: 1 }), true);
  assert.deepEqual(lastMessage(bridge.bridgeMessageTypes.ready), {
    type: bridge.bridgeMessageTypes.ready,
    payload: { version: 1 },
  });
});

test("isBridgeAvailable follows the host callback", () => {
  const callback = window.invokeCSharpAction;
  delete window.invokeCSharpAction;
  try {
    assert.equal(bridge.isBridgeAvailable(), false);
    assert.equal(bridge.signalReady(), false);
  } finally {
    window.invokeCSharpAction = callback;
  }
});

test("invokeHostApi resolves with the host result", async () => {
  window.clearMessages();

  const pending = bridge.invokeHostApi("HostApi", "getGreeting", "Ada");
  const message = lastMessage(bridge.bridgeMessageTypes.invoke);
  assert.deepEqual(message.payload, {
    requestId: message.payload.requestId,
    api: "HostApi",
    method: "getGreeting",
    args: ["Ada"],
  });

  completeLastInvocation("hello Ada");
  assert.equal(await pending, "hello Ada");
});

test("invokeHostApi rejects with the host error", async () => {
  window.clearMessages();

  const pending = bridge.invokeHostApi("HostApi", "fail");
  const message = lastMessage(bridge.bridgeMessageTypes.invoke);
  window.dispatch("avalonia-react:message", {
    detail: {
      type: bridge.bridgeMessageTypes.invokeResult,
      payload: { requestId: message.payload.requestId, error: "boom" },
    },
  });

  await assert.rejects(pending, /boom/);
});

test("createHostApiProxy forwards property calls", async () => {
  window.clearMessages();

  const hostApi = bridge.createHostApiProxy("HostApi");
  const pending = hostApi.getGreeting("Ada", 2);
  const message = lastMessage(bridge.bridgeMessageTypes.invoke);
  assert.deepEqual(message.payload, {
    requestId: message.payload.requestId,
    api: "HostApi",
    method: "getGreeting",
    args: ["Ada", 2],
  });

  completeLastInvocation("ok");
  assert.equal(await pending, "ok");
});

test("invokeHostApiWithOptions rejects when the host is too slow and cancels the call", async () => {
  window.clearMessages();
  const pending = bridge.invokeHostApiWithOptions("HostApi", "slow", [], { timeoutMs: 10 });
  const invoke = lastMessage(bridge.bridgeMessageTypes.invoke);

  await assert.rejects(pending, /timed out after 10 ms/);

  const cancel = lastMessage(bridge.bridgeMessageTypes.invokeCancel);
  assert.equal(cancel.payload.requestId, invoke.payload.requestId);
});

test("invokeHostApiWithOptions honors an aborted signal and cancels the call", async () => {
  window.clearMessages();
  const controller = new AbortController();
  const pending = bridge.invokeHostApiWithOptions("HostApi", "slow", [], {
    signal: controller.signal,
  });
  const invoke = lastMessage(bridge.bridgeMessageTypes.invoke);

  controller.abort();
  await assert.rejects(pending, /was aborted/);

  const cancel = lastMessage(bridge.bridgeMessageTypes.invokeCancel);
  assert.equal(cancel.payload.requestId, invoke.payload.requestId);
});

test("pagehide rejects pending calls and cancels them", async () => {
  window.clearMessages();
  const pending = bridge.invokeHostApi("HostApi", "slow");
  const invoke = lastMessage(bridge.bridgeMessageTypes.invoke);

  window.dispatch("pagehide", {});

  await assert.rejects(pending, /page unloaded/);

  const cancel = lastMessage(bridge.bridgeMessageTypes.invokeCancel);
  assert.equal(cancel.payload.requestId, invoke.payload.requestId);
});

test("window errors are forwarded as avalonia:error", () => {
  window.clearMessages();

  window.dispatch("error", {
    message: "unique-error-for-test",
    error: new Error("unique-error-for-test"),
    filename: "app.js",
    lineno: 12,
    colno: 5,
  });

  const message = lastMessage(bridge.bridgeMessageTypes.error);
  assert.ok(message, "expected an avalonia:error message");
  assert.equal(message.payload.kind, "error");
  assert.equal(message.payload.message, "unique-error-for-test");
  assert.equal(message.payload.source, "app.js");
  assert.equal(message.payload.line, 12);
  assert.equal(message.payload.column, 5);
});
