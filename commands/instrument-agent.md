---
description: Add Progress Observability instrumentation to an existing agent (Python, TypeScript, or .NET), then hand off with where to confirm the traces.
argument-hint: [optional: path or repo to instrument, and anything unusual about it]
---

Use the `instrument-agent` skill.

Target: $ARGUMENTS

Detect the language, LLM SDKs/frameworks, any existing telemetry, and the config
style — and report the planned diff before editing. Then wire instrumentation
per the matching language reference (`references/python.md`, `typescript.md`, or
`dotnet.md`) with the smallest possible diff: latest package, init at process
start before any LLM client exists, Integration key (`ac_p_…`) via config, agreed
`app_name`, flush on exit. Have the app run once (the decorator/manual-span path
works with no LLM credentials). This skill does not read the platform back — tell
the user wiring is done, point them at the trace explorer for their `app_name`,
and note that `/health-check` confirms arrival over MCP when they want it.

If no target was given and the working directory isn't obviously the agent to
instrument, ask which project to instrument before touching anything.
