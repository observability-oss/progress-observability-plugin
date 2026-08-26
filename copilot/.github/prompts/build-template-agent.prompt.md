---
mode: 'agent'
description: 'Build the ready Release Evidence Reviewer template and return its local UI plus three direct smoke-trace links.'
---

Follow the **build-template-agent** workflow in
`.github/copilot-instructions.md`. Use the fixed Release Evidence Reviewer asset
and the default target `./release-evidence-reviewer`; do not ask domain or design
questions and do not generate source.

Build with .NET 10, run `dotnet run -- --smoke`, and require the
`SMOKE_REPORT=<json>` marker to pass for `policy-markdown`, `atlas-blocked`, and
`unknown-not-found`. Preserve their exact trace IDs and verify those IDs with
the connected read-only Progress Observability MCP tools. Start and health-check
the local UI.

Finish only with the absolute project path, one verified local UI link, and
three official per-trace UI deep links explicitly returned or supplied by the
Progress platform card/lookup and matched to the verified trace IDs. Never
construct a URL, return `/api/Traces/<id>`, or fall back to the generic
Observations page. If any UI deep link is absent, return
`TRACE_UI_DEEPLINK_UNAVAILABLE` with all three verified IDs and do not claim the
template is ready. Never request or print secrets.
