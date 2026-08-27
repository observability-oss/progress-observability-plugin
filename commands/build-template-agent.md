---
description: Build the ready Release Evidence Reviewer template, prove it with three smoke traces, and return its local UI and Tracing page.
argument-hint: [optional target folder; defaults to ./release-evidence-reviewer]
---

Use the `build-template-agent` skill.

Target: $ARGUMENTS

Use `./release-evidence-reviewer` when no target was supplied. Require .NET 10
and use the skill's package-free .NET helper to copy the fixed asset without
source generation or domain questions. Build it, run `dotnet run -- --smoke`,
and require the `SMOKE_REPORT=<json>` marker to pass for `policy-markdown`,
`atlas-blocked`, and `unknown-not-found`. Verify their exact trace IDs through
the existing read-only Progress Observability MCP tools. Start and health-check
the local UI.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Do not return three per-trace links. Never request or print secrets.
