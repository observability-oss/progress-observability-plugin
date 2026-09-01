---
description: Turn a purpose into either a safe local .NET 10 agent prototype or a no-write integration plan.
argument-hint: "optional 1-2 sentence purpose"
---

Use the `build-custom-agent` skill.

Purpose: $ARGUMENTS

If no purpose was supplied, ask only: `In 1-2 short sentences, what should this
agent help someone accomplish?` Follow the skill's build-versus-plan decision
and present its outcome choices. Do not create project files unless the user
selects `Build local prototype`. A prototype may use only workspace files or
clearly synthetic data; it must not call live business systems or perform real
side effects. A plan-only result creates no project files.

For a prototype, never source or copy a parent `.env`; use the generated
README's .NET user-secrets setup. If app configuration is missing, name only the
missing keys and point to those commands. Require successful copy, project
validation before build and again before handoff, build, and exactly three
passing smoke results with IDs `knowledge`, `tool`, and `not-found`, then
health-check the UI.

Return the project path, verified local UI, smoke results with their emitted
trace IDs, and exactly one Progress Observability Tracing-page link:
`https://observability.progress.com/observations`. Treat the trace IDs as local
execution evidence and state that backend trace ingestion is not independently
verified; do not construct per-trace deep links. Never request, inspect, or print
credential values. Plan-only remains zero-write and credential-free.
