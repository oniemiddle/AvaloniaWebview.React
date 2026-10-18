import assert from "node:assert/strict";
import test from "node:test";

function createFakeWindow() {
  const listeners = new Map();
  const messages = [];

  const window = {
    invokeCSharpAction(body) {
      messages.push(JSON.parse(body));
    },
    addEventListener(type, handler) {
      if (!listeners.has(type)) listeners.set(type, []);
      listeners.get(type).push(handler);
    },
    removeEventListener(type, handler) {
      const handlers = listeners.get(type);
      if (!handlers) return;
      const index = handlers.indexOf(handler);
      if (index >= 0) handlers.splice(index, 1);
    },
    dispatch(type, detail) {
      for (const handler of [...(listeners.get(type) ?? [])]) {
        handler({ detail });
      }
    },
  };

  return { window, messages };
}

const { window, messages } = createFakeWindow();
globalThis.window = window;

// Use a distinct module instance so the top-level host-invocation listeners bind
// to this file's fake window rather than another test file's window.
const bridge = await import("../dist/index.js?react-invoke");

function nextTurn() {
  return new Promise(resolve => setTimeout(resolve, 0));
}

function dispatchMessage(type, payload) {
  window.dispatch(bridge.bridgeDomainEvent, { type, payload });
}

function findResult(requestId) {
  return messages.find(message =>
    message.type === bridge.bridgeMessageTypes.reactResult &&
    message.payload?.requestId === requestId);
}

test("host-to-React invocation calls the registered handler and returns its result", async () => {
  const unregister = bridge.registerReactHandler("sum", ({ a, b }) => a + b);
  try {
    dispatchMessage(bridge.bridgeMessageTypes.reactInvoke, {
      requestId: "invoke-1",
      handler: "sum",
      payload: { a: 2, b: 3 },
    });
    await nextTurn();

    assert.deepEqual(findResult("invoke-1"), {
      type: bridge.bridgeMessageTypes.reactResult,
      payload: { requestId: "invoke-1", result: 5 },
    });
  } finally {
    unregister();
  }
});

test("host-to-React invocation reports missing handlers", async () => {
  dispatchMessage(bridge.bridgeMessageTypes.reactInvoke, {
    requestId: "invoke-missing",
    handler: "missing",
    payload: {},
  });
  await nextTurn();

  const result = findResult("invoke-missing");
  assert.equal(result?.payload?.error, "No React handler registered for 'missing'.");
});

test("host-to-React invocation reports handler errors", async () => {
  const unregister = bridge.registerReactHandler("broken", () => {
    throw new Error("handler failed");
  });
  try {
    dispatchMessage(bridge.bridgeMessageTypes.reactInvoke, {
      requestId: "invoke-error",
      handler: "broken",
      payload: {},
    });
    await nextTurn();

    assert.equal(findResult("invoke-error")?.payload?.error, "handler failed");
  } finally {
    unregister();
  }
});

test("host-to-React cancellation aborts the handler signal", async () => {
  let observedSignal;
  const unregister = bridge.registerReactHandler("slow", async (_payload, context) => {
    observedSignal = context.signal;
    await new Promise((_, reject) => {
      context.signal.addEventListener("abort", () => reject(new Error("aborted")), { once: true });
    });
  });

  try {
    dispatchMessage(bridge.bridgeMessageTypes.reactInvoke, {
      requestId: "invoke-cancel",
      handler: "slow",
      payload: {},
    });
    await nextTurn();
    dispatchMessage(bridge.bridgeMessageTypes.reactCancel, { requestId: "invoke-cancel" });
    await nextTurn();

    assert.equal(observedSignal.aborted, true);
    assert.equal(findResult("invoke-cancel"), undefined);
  } finally {
    unregister();
  }
});
