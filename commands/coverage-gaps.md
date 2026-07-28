---
description: Find production behaviors that have no evaluation measuring them, and prioritize which judges to build.
argument-hint: [optional service or focus, e.g. "checkout-agent"]
---

Use the `coverage-gaps` skill.

Focus: $ARGUMENTS

Characterize recent traffic with `list_observations` (metadata only, within 72h), inventory existing judges with `list_evaluation_tasks`, diff behaviors against the failure modes that would catch their faults, and rank the gaps by volume × blast-radius. Report a coverage table plus a prioritized shortlist, and offer to run `generate-eval` on the top gap.
