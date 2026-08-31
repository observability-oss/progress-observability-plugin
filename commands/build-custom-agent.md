---
description: Turn a purpose into either a safe local .NET 10 agent prototype or a no-write integration plan.
argument-hint: [optional 1-2 sentence purpose]
---

Use the `build-custom-agent` skill.

Purpose: $ARGUMENTS

If no purpose was supplied, ask only: `In 1-2 short sentences, what should this
agent help someone accomplish?` Follow the skill's build-versus-plan decision
and present its outcome choices. Do not create project files unless the user
selects `Build local prototype`. A prototype may use only workspace files or
clearly synthetic data; it must not call live business systems or perform real
side effects. A plan-only result creates no project files.

Never request or print credential values. For a completed prototype, return the
project path, verified local UI, three smoke results, and the single Progress
Observability Tracing-page link required by the skill. Use exactly
`https://observability.progress.com/observations`; do not construct per-trace
deep links.
