---
mode: 'agent'
description: 'Add Progress Observability instrumentation to an existing agent (Python, TypeScript, or .NET), then hand off with where to confirm the traces.'
---

Follow the **instrument-agent** workflow in `.github/copilot-instructions.md`.

Target (path or repo, and anything unusual about it): ${input:target}

Detect the language, LLM SDKs/frameworks, any existing telemetry, and the config style — and report the planned diff before editing. Then wire instrumentation with the smallest possible diff: latest package (`progress-observability` on PyPI, `@progress/observability` on npm, `Progress.Observability.Instrumentation` on NuGet), init at process start before any LLM client exists, Integration key (`ac_p_…`) via config, an agreed `app_name`, and flush on exit. Have the app run once (the decorator/manual-span path works with no LLM credentials). This skill does not read the platform back — tell the user wiring is done, then have them run their agent and confirm the traces are flowing in at observability.progress.com for their `app_name`.

If no target was given and the workspace isn't obviously the agent to instrument, ask which project to instrument before touching anything.
