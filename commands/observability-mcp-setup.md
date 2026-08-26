---
description: Wire an AI client (VS Code Copilot, Copilot CLI, Claude Code, or Cursor) up to the Progress Observability MCP server.
---

Use the `observability-mcp-setup` skill.

Confirm the user has a paid plan and an MCP API key (API Keys → MCP API Keys in the platform UI — this skill cannot create one). Detect which client's config file to write (`.vscode/mcp.json`, `~/.copilot/mcp-config.json`, `.mcp.json`, `.cursor/mcp.json`, or `.codex/config.toml`), check it for an existing `progress-observability` entry first, and never write the raw key into a project file — reference it via each client's own secret mechanism (VS Code `inputs`, an env var, or Codex's `env_http_headers`). After writing the config, verify with the `tools/list` curl (7 tools = Metadata only, 9 = With content), then hand off to `/health-check`.

If the user is installing this whole plugin via the Claude Code or Copilot CLI plugin system, tell them the MCP connection is already wired for them and this skill isn't needed.
