---
name: cost-report
description: Report and explain LLM spend and usage on the Progress Observability Platform. Use when the user asks "what's driving my LLM costs", "how much am I spending", "cost by model/app", "am I close to my quota", "why did spend spike", or wants a cost/usage summary or a cheaper-model recommendation.
---

# Cost & usage report

Read `references/mcp.md` first for the tool contract and limits. This skill
uses only metadata tools — no trace content, so no injection surface. If that file
isn't present (this skill was lifted out on its own), ask the user for the plugin's
`references/mcp.md`, or fall back to the hard rules: every tool is read-only, and
`get_cost_breakdown` / `get_usage_summary` are the only tools this skill needs.

<!-- copilot:start -->
Turn the platform's cost and usage data into a short, decision-ready report.

## Workflow

1. **Scope.** Confirm the date range (`get_cost_breakdown` takes `start_date` /
   `end_date`) and what the user cares about — total spend, a suspected spike, model
   mix, or quota headroom. Default to the last 7 days if unspecified.

2. **Pull cost.** `get_cost_breakdown`. Run it more than once when useful:
   `group_by: "model"` for model mix, `"application"` for which app spends,
   `"day"` for the trend. Use `"all"` for a first pass.

3. **Pull usage/quota.** `get_usage_summary` for current billing-period usage and
   remaining quota.

4. **Report.**
   - **Headline** — total spend for the range and where the billing period stands
     vs. quota.
   - **Top drivers** — the models/apps carrying most cost, with shares.
   - **Trend** — day-over-day movement; call out any spike and which model/app it
     came from.
   - **Quota** — projected burn vs. remaining (extrapolate linearly from
     period-to-date usage — state that assumption), and whether the current rate
     lands over quota before the period ends.
   - **Recommendation** (only if the data supports it) — e.g. a high-volume, low-
     stakes call path that could move to a cheaper model; frame it as a hypothesis
     to validate, not a certainty.

Keep it tight — a few bullets and one small table, not a wall of numbers.

Never write back to the platform — the server is read-only.
<!-- copilot:end -->
