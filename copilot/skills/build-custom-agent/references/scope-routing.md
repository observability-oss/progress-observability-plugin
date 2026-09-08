# Custom-agent scope routing

Use this reference after Purpose is available to choose a truthful local
prototype or zero-write integration plan, optionally through a separately
confirmed reduced mock PoC.

## Normalize and decide

Purpose is actionable when it names a concrete outcome and enough subject,
input, or action for a safe demonstration. For only a vague topic or role, ask
the one allowed clarification:

> What should the agent mainly do: answer from information, analyze or summarize
> records, or take actions in another system?

Ask nothing further. If uncertainty remains, offer plan/revise without inventing
a scenario. A user-selected revision is a separate editing flow, not another
automatic clarification.

Normalize internally:

```yaml
purpose: <required, concise>
name: <inferred display name>
service_slug: <lowercase-hyphen slug>
knowledge:
  mode: workspace-files | generated-docs | mock-records | none
  workspace_sources: [<workspace-relative paths only>]
actions:
  - name: <at most three>
    behavior: local-read | local-compute | mock-read
external_systems: [<future adapters>]
requested_side_effects: [<writes, approvals, notifications>]
recommended_outcome: local-prototype | plan-only
reason: <one concise user-visible sentence>
```

Local fields describe a possible prototype, not a rewritten live goal. Preserve
the original sources/actions for later revision and planning. Include no secrets,
raw document content, absolute paths, or hidden reasoning.

Ask internally:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials, live external calls, or real side
> effects?

Choose a local prototype only when yes. Supplied safe files, generated documents,
and deterministic mock records are allowed. A named system alone does not force
plan-only: advisory Jira triage or SharePoint-style Q&A may use disclosed local
content. Never infer live access or effects the user did not request, and honor
rejected source types or mocks.

Choose plan-only for essential live data/credentials, real writes or
notifications, approval or durable multi-step execution, production RAG,
multi-agent orchestration, deployment, or provider switching. Uncertainty about
a useful safe slice means no mock offer. Reapply this decision after revisions;
classification and proposals never authorize a build.

## Present the path choice

A buildable request gets **Proposed local prototype** with Purpose, Name,
Knowledge, and up to three Actions, followed by the native Build/Revise picker.
Identify mock versus user-supplied sources, say no live adapter is used, and do
not imply the agent already exists.

For a complex/live request, put two to four plain sentences only in the native
question body: why the full goal is outside this MVP, why both paths help, and
what the mock slice excludes. A plan guides the full developer implementation;
a mock PoC checks only a bounded decision slice. If no mock fits, explain that
revision can narrow the scope. Append a blank line and
`How would you like to continue?`; show neither a plan preview nor four fields.
Go directly to this question after reading the reference, without a scope-summary
commentary message. Example CLI `ask_user` arguments (invoke; do not print):

```json
{
  "question": "This agent needs live cluster access, continuous operation, and production-changing actions, which are outside this local MVP. I can provide a developer plan for building the full solution separately, or propose a local PoC to test decisions using mock incidents. The PoC would not access your cluster or perform remediation, and cannot prove production safety.\n\nHow would you like to continue?",
  "choices": ["Show implementation plan", "Propose a simplified mock PoC"]
}
```

Keep all rationale in that box, with short structured choice labels. Use the same
native mechanism for plan/revise and build/revise; follow `SKILL.md` for fallback
and cancellation. Declining mocks retains plan/revise unless the user explicitly
requests the plan. A plan selection does not start implementation.

For an external-system prototype, prefer concrete disclosure such as `mock Jira
issues`, not `synthetic data`, and say it will not use even an available adapter,
connector, or MCP connection. User-supplied safe files are `supplied`, never
mock. Plan-only creates neither a prototype nor mock dataset.

## Simplified mock PoC

Offer `Propose a simplified mock PoC` only when a useful advisory or analytical
slice fits local content and at most three deterministic read/compute tools. Do
not offer it when mocks were rejected, only a plan was requested, or no meaningful
slice fits; do not repeatedly re-offer a declined mock unless the user revises
that preference.

Selecting this option requests only a proposal. Create no files, inspect no
credentials, and call no business system. Show the reduced Purpose, Name,
Knowledge, Actions, and excluded behavior, then ask exactly `Build proposed
agent` / `Revise proposed agent`. Preserve the original goal and deferred work
for `INTEGRATION_PLAN.md`.

For example, production Kubernetes auto-remediation can become **Kubernetes
Remediation Advisor** over mock incidents and an example runbook. Explicitly
exclude cluster access, continuous monitoring, restart/rollback execution, and
implemented approvals, and state that sample decisions do not prove production
safety. Only explicit Build approval of that displayed scope starts the existing
prototype flow. Add no new runtime, workflow engine, live tools, or background
monitor. Results remain recommendations or missing evidence, never completed
restarts, approvals, payments, or notifications. Keep this boundary in Purpose,
answers, smoke interpretation, and handoff.

## Revise in one reply

On `Revise proposed agent`, show one copyable block:

```text
Purpose: <current purpose>
Name: <current name>
Knowledge: <current data sources>
Actions: <current actions>
```

Ask for all desired changes in one reply; accept an edited block or prose and
retain untouched fields. Do not ask four questions. Flag conflicts rather than
silently rewriting fields. Rerun routing and show either the revised local
proposal with Build/Revise or the complex explanation with its choices. Even a
name-only revision is not approval. New live/effect requirements return to the
complex choice; never drop requirements to fit the starter.

## Representative intents

| Purpose | Outcome |
|---|---|
| Triage Jira items into smaller actionable chunks. | Local proposal from supplied content or mock Jira issues; no plan/mock gate. |
| Answer HR questions from SharePoint. | Local proposal from supplied files or mock policies plus future-adapter plan; required live retrieval uses the complex choice. |
| Read the current Jira backlog through our adapter. | Developer plan or separately proposed mock slice; adapter remains unused. |
| Update Jira, collect approval, and notify Slack. | Plan or mock recommendations; no writes, workflow, or delivery. |
| Monitor Kubernetes and restart/roll back services. | Plan or mock incident/remediation advisor, with reduced scope confirmed. |
| Use SAP/bank APIs to approve, reimburse, and notify. | Plan or mock claims assessment; never approve, pay, or notify. |
| Remediate the real cluster; mocks are useless. | Plan/revise only; no files without later revised scope and Build approval. |

## Prototype content and continuation

Only workspace-relative regular `.md`, `.txt`, `.json`, or `.csv` files are
allowed: no links, hidden paths, `.env`, more than 10 files, files over 1 MiB,
or totals over 5 MiB. In `appsettings.json`, `Content:Sources` exhaustively maps
each exact path to `supplied` or `mock`. Generated content is `mock`; only actual
user-provided files are `supplied`.

Tools are deterministic over that content. They make no API/database calls or
effects. For an external-source or reduced mock prototype,
`INTEGRATION_PLAN.md` briefly records the original goal, local slice, sources,
assumptions, unvalidated behavior, and future adapter/tool contracts. List
authentication/permissions, failure handling, tests, and ordered developer work.
An existing adapter may be assessed later but is not used or validated now.

Preserve requested automation. Mark a safer alternative such as human approval
as a **Recommended changed assumption** and explain its effect; never imply
acceptance. End the file with a copyable Copilot prompt for separate developer-led
implementation after permissions, scope, and tests are confirmed. Do not run it.

## MAF guidance for plans

- Function tool: on-demand lookup/action after developers supply typed client,
  authentication, contracts, and safety behavior.
- Middleware: validation, authorization, redaction, logging, and errors—not the
  business adapter.
- Context provider: configured history/profile/retrieval injected per run; it
  neither creates nor authorizes a source.
- Runtime Agent Skill: reusable domain instructions/trusted resources, distinct
  from this Copilot build skill and not a connector.
- Workflow: explicit ordering, branching, approvals, checkpoints, or costly
  effects; plan it rather than adding it to this prototype.

MAF coordinates developer-provided components. It does not provision access,
invent contracts/rules, make effects idempotent, create production retrieval,
or turn checkpoints into cross-system transactions.

## Plan-only response

After `Show implementation plan`, return concise ordered developer steps for the
full goal: MAF shape; each adapter, auth owner, and permission; request/response
and source-of-truth contracts; effect approval/idempotency/retry/compensation;
local, contract, integration, and end-to-end tests; deployment, secrets, and
observability; and builder versus developer ownership. Make dependencies and the
first step clear. Execute nothing and create no `INTEGRATION_PLAN.md` or other file.
