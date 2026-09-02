---
description: Turn a purpose into either a safe local .NET 10 agent prototype or a no-write integration plan.
argument-hint: "optional 1-2 sentence purpose"
---

Use the `build-custom-agent` skill.

Skip scenario-summary preambles when loading the skill or routing reference.
While a complex request awaits a choice, its scope explanation belongs only
inside the question body, never in progress commentary.

Purpose: $ARGUMENTS

For every two-choice gate, use the host's native question tool:
Copilot CLI `ask_user` with structured `choices`, or VS Code
`vscode/askQuestions` with single-select options. Do not replace the picker with
plain bullets. Use the skill's text fallback only when no question tool is
available; cancellation or no answer never authorizes a build.
For a complex request, put the brief context inside the tool's question body,
followed by a blank line and `How would you like to continue?`. Do not print the
rationale as a separate chat message or put it only in option help text.
That context must briefly explain why the full goal is outside the local MVP,
what value each offered path provides, and what the mock
PoC cannot connect to, do, or validate. Do not expose internal mode labels.

If no purpose was supplied, ask only: `In 1-2 short sentences, what should this
agent help someone accomplish?` Assess scope internally. A buildable local
request gets the four-field proposal (Purpose, Name, Knowledge, Actions) and
`Build proposed agent` / `Revise proposed agent`. A complex request starts with
only a brief scope explanation and choices together in the question box: no
plan heading, plan preview, or four-field block. Explain the separate developer
work and possible mock slice,
then offer `Show implementation plan` / `Propose a simplified mock PoC` when a
useful read/compute-only slice fits the starter. The plan guides later full
implementation with the user's coding agent; the mock option first proposes
reduced scope. Keep plan/revise if mocks are declined or no suitable PoC exists.
If the user explicitly requests the implementation plan, return it in chat
directly without another confirmation.
Requesting a mock PoC only shows a reduced-scope four-field proposal with
explicit exclusions and build/revise choices; create nothing and wait for
confirmation. Preserve the original goal for the continuation plan. A revision
uses one prefilled four-field block; keep unchanged fields, reassess scope and
mock eligibility, then show a local proposal or complex scope explanation as
appropriate. Create files only after `Build proposed agent` confirms the latest
in-scope proposal, then build without further customization questions.
State upfront that an external-system prototype
uses mock data (or explicitly supplied local files), not a live adapter, even
if the user already has one. Do not use existing connectors or business-system
MCP tools to fetch real records. It must not call live business systems or
perform real side effects. `Show implementation plan` returns its result in
chat and creates no project files.

For a prototype, never source or copy a parent `.env`; use the generated
README's .NET user-secrets setup. If app configuration is missing, name only the
missing keys and point to those commands. Require successful copy, project
validation before build and again before handoff, build, and exactly three
passing smoke results with IDs `knowledge`, `tool`, and `not-found`, then
start the UI with
`dotnet run --project <target>/CustomAgent.csproj --no-build -- --urls http://127.0.0.1:0`.
Wait for that process's own `Now listening on:` URL and health-check only that
URL; never guess or scan ports.

Return the project path, verified local UI, smoke results with their emitted
trace IDs, and exactly one Progress Observability Tracing-page link:
`https://observability.progress.com/observations`. Treat the trace IDs as local
execution evidence and state that backend trace ingestion is not independently
verified; do not construct per-trace deep links. Never request, inspect, or print
credential values. For an external-source prototype or simplified mock PoC,
link `INTEGRATION_PLAN.md` with the original goal and ordered steps for separate
future development; do not execute those steps during this build. A simplified
PoC's UI Purpose, responses, and handoff must disclose mock data and reduced
scope. Its smoke tests check sample decisions, not real outcomes or production
safety. Plan-only remains zero-write and credential-free.
