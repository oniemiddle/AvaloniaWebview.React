import assert from "node:assert/strict";
import test from "node:test";
import { execFileSync } from "node:child_process";

test("the package can be imported without a browser window", () => {
  const moduleUrl = new URL("../dist/index.js", import.meta.url).href;
  const script = [
    `import { bridgeMessageTypes, bridgeDomainEvent, isBridgeAvailable } from ${JSON.stringify(moduleUrl)};`,
    "if (bridgeMessageTypes.ready !== 'avalonia:ready') process.exit(2);",
    "if (bridgeDomainEvent !== 'avalonia-react:message') process.exit(3);",
    "if (isBridgeAvailable()) process.exit(4);",
    "process.stdout.write('ok');",
  ].join(" ");

  const output = execFileSync(process.execPath, ["--input-type=module", "-e", script], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });

  assert.equal(output, "ok");
});
