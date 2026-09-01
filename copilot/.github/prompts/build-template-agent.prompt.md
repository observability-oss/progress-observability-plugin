---
mode: 'agent'
description: 'Build and locally verify the ready Release Evidence Reviewer without MCP.'
---

In GitHub Copilot agent mode, follow the **build-template-agent** workflow in
`.github/copilot-instructions.md`. Use the fixed Release Evidence Reviewer asset
and the default target `./release-evidence-reviewer`; do not ask domain or design
questions and do not generate source. Require .NET 10 and use the skill's
package-free .NET copy helper; do not require Python.

Build with .NET 10, run `dotnet run -- --smoke`, and require the
`SMOKE_REPORT=<json>` marker to pass for `policy-markdown`, `atlas-blocked`, and
`unknown-not-found`. Never source or copy a parent `.env`; use the copied
README's shared .NET user-secrets setup. If configuration is missing, name only
the missing keys and point to those commands. Preserve the emitted trace IDs as
local evidence, then start and health-check the local UI. Do not call MCP.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing-page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Do not resolve or list three per-trace links. Never request or print
secrets. State explicitly that backend trace ingestion is not independently
verified by this workflow.
