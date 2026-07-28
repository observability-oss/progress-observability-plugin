---
mode: 'agent'
description: 'Summarize and explain LLM spend and quota usage from the Progress Observability Platform.'
---

Follow the **cost-report** workflow in `.github/copilot-instructions.md`, using the connected `progress-observability` MCP tools (metadata only).

Scope (optional date range and/or focus): ${input:scope}

Pull `get_cost_breakdown` (group by model/application/day as useful) and `get_usage_summary`, then report the headline spend, top drivers, day-over-day trend with any spike explained, quota burn, and a cheaper-model hypothesis only if the data supports it. Default to the last 7 days if no range was given. Keep it tight.
