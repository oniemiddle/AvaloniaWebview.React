import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const packageDirectory = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const npmCli = process.env.npm_execpath;
const npm = process.platform === "win32" ? "npm.cmd" : "npm";
const manifest = JSON.parse(readFileSync(resolve(packageDirectory, "package.json"), "utf8"));

if (manifest.private === true) {
  throw new Error("package.json must not be private before publishing.");
}

if (!manifest.files?.includes("dist") || !manifest.files?.includes("README.md") || !manifest.files?.includes("LICENSE")) {
  throw new Error("package.json files must include dist, README.md, and LICENSE.");
}

const output = npmCli
  ? execFileSync(process.execPath, [npmCli, "pack", "--dry-run", "--json"], {
      cwd: packageDirectory,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    })
  : execFileSync(npm, ["pack", "--dry-run", "--json"], {
      cwd: packageDirectory,
      encoding: "utf8",
      shell: process.platform === "win32",
      stdio: ["ignore", "pipe", "pipe"],
    });
const [pack] = JSON.parse(output);
const files = new Set(pack.files.map(file => file.path));
const required = [
  "package.json",
  "README.md",
  "LICENSE",
  "dist/index.js",
  "dist/index.d.ts",
  "dist/react.js",
  "dist/react.d.ts",
];

for (const file of required) {
  if (!files.has(file)) {
    throw new Error(`The packaged tarball is missing ${file}.`);
  }
}

for (const file of files) {
  if (file.startsWith("src/") || file.startsWith("test/") || file.startsWith("scripts/") || file.startsWith("node_modules/")) {
    throw new Error(`The packaged tarball should not include ${file}.`);
  }
}

if (!pack.filename.endsWith(".tgz")) {
  throw new Error(`Unexpected npm pack filename: ${pack.filename}`);
}

console.log(`Verified ${manifest.name}@${manifest.version}: ${pack.filename} (${pack.size} bytes, ${pack.unpackedSize} unpacked)`);
