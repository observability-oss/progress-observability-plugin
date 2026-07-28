---
description: Summarize and explain LLM spend and quota usage from the Progress Observability Platform.
argument-hint: [optional date range and/or focus, e.g. "last 30 days, by model"]
---

Use the `cost-report` skill.

Scope: $ARGUMENTS

Pull `get_cost_breakdown` (group by model/application/day as useful) and `get_usage_summary`, then report the headline spend, top drivers, day-over-day trend with any spike explained, quota burn, and a cheaper-model hypothesis only if the data supports it. Default to the last 7 days if no range was given. Keep it tight.
