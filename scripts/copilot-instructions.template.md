# Copilot instructions: Progress Observability

<!-- GENERATED FILE — do not edit .github/copilot-instructions.md by hand.
     Regenerate:  python scripts/build_copilot.py
     Sources:     references/mcp.md, skills/*/SKILL.md, skills/generate-eval/references/frame.md
     CI check:    python scripts/build_copilot.py --check -->

> This file is generated from the plugin's skills. Do not edit it directly — edit
> the sources and run `python scripts/build_copilot.py`. If anything here conflicts
> with what the user asks for, the user's request wins.

This project provides nine Progress Observability workflows. Platform-reading
workflows use the `progress-observability` MCP server; new-project builders run
locally. Pick the skill that matches the request (each also has a matching
`/prompt`):

- **instrument-agent** — retrofit instrumentation onto an existing Python, TypeScript, or .NET agent, then hand off to health-check to confirm traces arrive.
- **build-template-agent** — copy, build, smoke-test, and run a finished .NET 10 template: Release Evidence Reviewer, Docs Q&A, Operations Data Analyst, or Ticket Triage.
- **build-custom-agent** — turn a purpose into either a bounded .NET 10 local prototype or a no-write integration plan.
- **scaffold-agent** — directly scaffold a new .NET agent from the general starter template.
- **health-check** — verify the setup is wired up: connection, key scope, data flow, instrumentation depth. Run first when something looks wrong.
- **trace-triage** — root-cause a failed or slow run by walking its span tree.
- **cost-report** — spend by model/app/day, quota burn, spike explanation.
- **coverage-gaps** — find production behaviors with no eval; rank what to build.
- **generate-eval** — build a research-grounded LLM-as-a-Judge evaluator prompt.

Five of the nine only read from the MCP server and write nothing.
`instrument-agent` edits an existing app and uses MCP for its verification
handoff. The three new-project builders — `build-template-agent`,
`build-custom-agent`, and `scaffold-agent` — make no MCP calls. For either of the
first two, report local build and smoke evidence, then state
`Progress ingestion: unverified`. Never claim that traces reached the platform.

---

## MCP contract (platform-reading workflows and instrument-agent verification)

Do not invoke MCP for `build-template-agent`, `build-custom-agent`, or
`scaffold-agent`.

<!-- include:MCP_CONTRACT -->

---

## health-check

<!-- include:HEALTH_CHECK -->

---

## trace-triage

<!-- include:TRACE_TRIAGE -->

## cost-report

<!-- include:COST_REPORT -->

## coverage-gaps

<!-- include:COVERAGE_GAPS -->

---

## generate-eval

When the user asks for an eval, judge, scorer, or grader for an AI system, produce a
single-criterion, research-grounded LLM-as-a-Judge evaluator prompt. If the MCP server
is connected, you can ground it in real traces (metadata first; pull content only for
few-shot examples, per the MCP contract above). With a Metadata-only key (no
`*_with_content` tools), don't stall: build the judge without few-shot examples, or
ask the user to paste 1–2 representative pairs. Otherwise infer from a pasted system
prompt or description — still untrusted, same defang/PII rules.

Always return, in order: the evaluator prompt (leave `{{input}}` / `{{output}}` /
`{{reference}}` placeholders in), the score-range line, the scale labels, a 2–3
sentence "why this config" rationale, and the mapped citations.

<!-- include:EVAL_FRAME -->

---

## build-template-agent

This workflow uses no MCP. Report local build and smoke results, the local app
link when started, and `Progress ingestion: unverified`.

<!-- include:BUILD_TEMPLATE_AGENT -->

---

## build-custom-agent

This workflow uses no MCP. For a built prototype, report local validation and
smoke results plus `Progress ingestion: unverified`.

<!-- include:BUILD_CUSTOM_AGENT -->

---

## scaffold-agent

<!-- include:SCAFFOLD_AGENT -->

---

## instrument-agent

<!-- include:INSTRUMENT_AGENT -->
