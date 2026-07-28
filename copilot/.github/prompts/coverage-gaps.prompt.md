---
mode: 'agent'
description: 'Find production behaviors with no evaluation measuring them, and prioritize which judges to build.'
---

Follow the **coverage-gaps** workflow in `.github/copilot-instructions.md`, using the connected `progress-observability` MCP tools.

Focus (optional service): ${input:focus}

Characterize recent traffic with `list_observations` (metadata only, within 72h), inventory existing judges with `list_evaluation_tasks`, diff behaviors against the failure modes that catch their faults, and rank gaps by volume × blast-radius. Report a coverage table plus a prioritized shortlist, and offer to run the generate-eval workflow on the top gap.
