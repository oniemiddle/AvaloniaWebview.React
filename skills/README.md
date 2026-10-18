# Agent skills

This directory contains installable Agent Skills for people using AvaloniaWebview.React, not instructions for developing the library itself.

## `avalonia-webview-react-hybrid`

Use this skill when an agent is helping you:

- add React to an Avalonia desktop app (or add Avalonia to a React app),
- create typed host APIs and DTOs shared through `.d.ts` files,
- wire `signalReady`, host messages, theme, errors, and cancellation,
- choose Debug Vite vs Release `wwwroot` vs assembly-embedded assets,
- diagnose blank pages, WebView runtime failures, blocked APIs, or navigation issues,
- move a React view between windows/tabs,
- make the bridge work with trimming/Native AOT.

The skill deliberately covers both greenfield setup and debugging an existing hybrid app.

### Install for DSH

Add this repository's `skills` directory as a skill repository in DSH Settings → Skills, or copy the skill into the user skills root:

```powershell
New-Item -ItemType Directory -Force "$HOME\.dsh\skills" | Out-Null
Copy-Item -Recurse "skills\avalonia-webview-react-hybrid" "$HOME\.dsh\skills\"
```

After the catalog refreshes, invoke it by name with the `skill` tool or let the agent discover it from the description.

### Install for Claude Code / Codex

```powershell
New-Item -ItemType Directory -Force "$HOME\.claude\skills" | Out-Null
Copy-Item -Recurse "skills\avalonia-webview-react-hybrid" "$HOME\.claude\skills\"
```

Use `~/.codex/skills` instead for Codex. On macOS/Linux, replace `Copy-Item` with `cp -r`.

### Layout

```text
avalonia-webview-react-hybrid/
├── SKILL.md
└── references/
    ├── api-reference.md
    ├── recipes.md
    └── troubleshooting.md
```

`SKILL.md` is the entry point and stays focused on decisions and workflow. The reference files are loaded only when the agent needs exact API names, complete recipes, or a diagnostic path.
