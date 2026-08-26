---
description: Build the ready Release Evidence Reviewer template, prove it with three smoke traces, and return its local UI and direct trace links.
argument-hint: [optional target folder; defaults to ./release-evidence-reviewer]
---

Use the `build-template-agent` skill.

Target: $ARGUMENTS

Use `./release-evidence-reviewer` when no target was supplied. Copy the fixed
asset without source generation or domain questions, build it with .NET 10, run
`dotnet run -- --smoke`, and require the `SMOKE_REPORT=<json>` marker to pass for
`policy-markdown`, `atlas-blocked`, and `unknown-not-found`. Verify their exact
trace IDs through the existing read-only Progress Observability MCP tools. Start
and health-check the local UI.

Finish only with the absolute project path, one verified local UI link, and
three official per-trace UI deep links explicitly returned or supplied by the
Progress platform card/lookup and matched to the verified trace IDs. Never
construct a URL, return `/api/Traces/<id>`, or fall back to the generic
Observations page. If any UI deep link is absent, return
`TRACE_UI_DEEPLINK_UNAVAILABLE` with all three verified IDs and do not claim the
template is ready. Never request or print secrets.
