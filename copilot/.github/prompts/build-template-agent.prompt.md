---
mode: 'agent'
description: 'Build and locally verify a selected ready .NET agent template without MCP.'
---

In GitHub Copilot agent mode, follow the **build-template-agent** workflow in
`.github/copilot-instructions.md`. Resolve the requested template using
`skills/build-template-agent/templates.json`. Default to `release-evidence-reviewer`
only when no template is specified; reject an explicitly unknown ID. Use the
requested target or `./<template-id>`; do not ask domain/design questions or
generate source. Require .NET 10 and use the skill's package-free copy helper
with `--template <id> --target <target>`; no Python is needed.

Build with .NET 10, run `dotnet run -- --smoke`, and require the
`SMOKE_REPORT=<json>` marker to pass for exactly the selected catalog entry's
three smoke-case IDs. Never source or copy a parent `.env`; use the copied
README's shared .NET user-secrets setup. If configuration is missing, name only
the missing keys and point to those commands. Preserve the emitted trace IDs as
local evidence, then start the selected project with
`--no-build -- --urls http://127.0.0.1:0`. Use its own `Now listening on:` URL,
require `status=ready` at `/api/health` and HTTP 200 at `/`. If browser tools are
available, check its main action too; distinguish that from HTTP-only checks.
Do not call MCP.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing-page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Do not resolve or list three per-trace links. Never request or print
secrets. State explicitly that backend trace ingestion is not independently
verified by this workflow.
