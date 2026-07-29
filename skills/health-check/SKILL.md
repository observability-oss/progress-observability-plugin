---
name: health-check
description: Verify that a project's Progress Observability setup is wired up correctly — connection, key scope, whether traces are flowing, and how deep the instrumentation goes. Use when the user asks "is my observability set up right", "why am I seeing no traces", "check my connection", "is my data flowing", "verify my setup", or is running the plugin for the first time.
---

# Observability health check

Read `references/mcp.md` first for the tool contract, the key scopes, and the
72h window. This skill is read-only and uses metadata tools only — no trace content,
so no injection surface. If that file isn't present (this skill was lifted out on
its own), ask the user for the plugin's `references/mcp.md`, or fall back to the
hard rules: every tool is read-only, observation queries cap at a 72-hour window,
and a Metadata-only key exposes 7 tools while a With-content key exposes all 9.

<!-- copilot:start -->
Run a fixed diagnostic sequence that turns "it's not working" into a specific cause,
then report a short status checklist with the exact fix for anything red. Most signals
are ambiguous alone — an empty trace list has several possible causes — so run the
steps in order and interpret them together, each one narrowing the fault.

## Workflow

1. **Connection & key scope.** Confirm the `progress-observability` MCP tools are
   available to you, then identify the scope by **presence of the content tools**:
   `get_observation_details_with_content` present = **With-content** key (9 tools);
   absent = **Metadata-only** key (7 tools). Report which scope is active and what
   it implies — e.g. `eval-from-trace` needs a With-content key to quote real
   examples. If the tools are missing or every call returns an auth error, the
   server didn't authenticate: the fix is `OBSERVABILITY_MCP_API_KEY` / the
   `.mcp.json` key, not the platform. (For a raw check outside the agent, the
   connectivity `curl` is in `references/mcp.md`.)

2. **Recent data.** `list_observations`, `type: "traces"`, last 24h, small `limit`.
   - **Non-empty** — report the count, the services seen, and the most recent
     timestamp (freshness).
   - **Empty at 24h** — widen to the 72h max and retry. Present at 72h but not 24h
     means **traffic is stale** (nothing recent — the SDK stopped or the app went quiet).
   - **Empty at 72h** — no data. Separate the causes rather than guessing: no
     instrumentation running, the wrong key/project, or all traffic older than the 72h
     window.

3. **Instrumentation depth.** `get_observation_details` (metadata, `include_children`)
   on a couple of the most recent traces — it's available on both key scopes.
   Children with real span types (tool calls, retrieval, generations) is healthy.
   Traces with **no children** mean the SDK is capturing top-level spans only —
   flag it, since `trace-triage` and `coverage-gaps` will be blind to sub-spans.

   If the app uses a **framework** (LangChain, LlamaIndex, Haystack, CrewAI,
   LangGraph) but every trace is a bare LLM call with no workflow/task
   structure, name the most likely cause rather than just reporting thinness:
   in Python, the framework instrumentor is gated on a *distribution name*, so
   depending on `langchain-core` or `llama-index-core` without the `langchain` /
   `llama-index` meta package disables it silently. Measured: adding that one
   line took a fixture from 2 spans to 8, and another from 1 to 17. Suggest
   `/instrument-agent` to fix it.

4. **Evaluations.** `list_evaluation_tasks`, then `get_evaluation_scores` on a few.
   None configured is fine — but note that `coverage-gaps` and any eval monitoring need
   at least one judge, and offer `generate-eval`. Tasks that exist but have no recent
   scores are **dormant** (defined, not running).

5. **Report** a compact checklist — one line per check, a status marker, and for
   anything not green, the single concrete next step. Keep it scannable:

   ```
   ✓ Connection      reachable, auth OK
   ● Key scope       Metadata only (7 tools, no content tools) — eval-from-trace needs a With-content key
   ✓ Data freshness  312 traces / 24h, latest 4m ago, 3 services
   ● Instrumentation 2 of 5 sampled traces have no child spans — check SDK span nesting
   ✓ Evaluations     4 tasks, 3 scored in the last 24h
   ```

   End with the one highest-priority fix if anything is red, or a clear all-clear.

Never write back to the platform — the server is read-only.
<!-- copilot:end -->

<!-- canonical sync test, reverted next commit -->
