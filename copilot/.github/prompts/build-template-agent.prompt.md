---
mode: 'agent'
description: 'Build the ready Release Evidence Reviewer template and return its local UI plus one Progress Observability Tracing-page link.'
---

In GitHub Copilot agent mode, follow the **build-template-agent** workflow in
`.github/copilot-instructions.md`. Use the fixed Release Evidence Reviewer asset
and the default target `./release-evidence-reviewer`; do not ask domain or design
questions and do not generate source. Require .NET 10 and use the skill's
package-free .NET copy helper; do not require Python.

Build with .NET 10, run `dotnet run -- --smoke`, and require the
`SMOKE_REPORT=<json>` marker to pass for `policy-markdown`, `atlas-blocked`, and
`unknown-not-found`. Preserve their exact trace IDs and verify those IDs with
the connected read-only Progress Observability MCP tools. Start and health-check
the local UI.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing-page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Do not resolve or list three per-trace links. Never request or print
secrets.
