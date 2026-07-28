---
description: Check that your Progress Observability setup is wired up correctly — connection, key scope, whether traces are flowing, and instrumentation depth.
---

Use the `health-check` skill.

Run the diagnostic sequence in order: confirm the `progress-observability` MCP tools are available and identify the key scope by presence of the content tools (`get_observation_details_with_content` present = With-content, 9 tools; absent = Metadata-only, 7 tools); probe `list_observations` for recent data (last 24h, widen to the 72h max if empty); sample a couple of recent traces with `get_observation_details` (`include_children`) for instrumentation depth; check `list_evaluation_tasks` for judges. Report a compact status checklist with the exact fix for anything not green. Read-only, metadata only — no trace content needed.
