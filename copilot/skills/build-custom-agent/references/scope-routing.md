# Custom-agent scope routing

Use this reference after Purpose is available. It distinguishes a truthful
local prototype from a zero-write integration plan, and when a complex request
can offer a separately confirmed, reduced-scope mock PoC.

## Minimal conversation

Purpose is actionable when it names a concrete outcome and enough subject,
input, or action to infer a safe demonstration. If it is only a vague topic or
role, ask this one optional clarification:

> What should the agent mainly do: answer from information, analyze or summarize
> records, or take actions in another system?

Do not ask another clarification. If the result is still uncertain, route to
plan only and leave missing details unspecified internally rather than inventing
a scenario. A user-requested `Revise proposed agent` updates the proposal; it is
not an additional automatic clarification.

Normalize the result internally before offering an outcome:

```yaml
purpose: <required, concise>
name: <inferred display name>
service_slug: <lowercase-hyphen slug>
knowledge:
  mode: workspace-files | generated-docs | synthetic-records | none
  workspace_sources: [<workspace-relative paths only>]
actions:
  - name: <at most three>
    behavior: local-read | local-compute | simulated-read
external_systems: [<named future adapters>]
requested_side_effects: [<writes, approvals, or notifications>]
recommended_outcome: local-prototype | plan-only
reason: <one concise, user-visible sentence>
```

The local `knowledge` and `actions` fields describe potential prototype tools,
not the full live goal. For complex requests, keep the original requested
sources and actions for revisions and planning; leave local fields unset until
a mock scope is proposed. Never rewrite the original intent to fit these enums.

Do not put credentials, secret values, raw document contents, absolute paths,
or hidden reasoning in this record.

## First response and data-source disclosure

Keep mode labels internal. For a buildable local request, show a short
**Proposed local prototype** with Purpose, Name, Knowledge, and up to three
Actions. Do not describe a proposal as an already-created agent.

For a complex/live request, compose two to four plain sentences for the native
question body, not a separate assistant message:
why the full idea cannot be built safely as this local MVP, why both offered
paths remain useful, and what a mock slice excludes. For plan/mock, explain that
the plan guides developers through the full solution while the mock PoC can
validate only a bounded decision slice. For plan/revise, explain that revision
can narrow the goal to a safe local prototype. Append a blank line and
`How would you like to continue?` in that same question body, then call the
host's interactive question tool with the two structured choices from `SKILL.md`.
At this point, show neither a plan heading/preview nor
a Purpose/Name/Knowledge/Actions block. Detailed planning follows plan selection;
the local-prototype proposal follows selection of the simplified mock option.
Example Copilot CLI `ask_user` arguments (invoke the tool; do not print this JSON):

```json
{
  "question": "This agent needs live cluster access, continuous operation, and production-changing actions, which are outside this local MVP. I can provide a developer plan for building the full solution separately, or propose a local PoC to test decisions using mock incidents. The PoC would not access your cluster or perform remediation, and cannot prove production safety.\n\nHow would you like to continue?",
  "choices": ["Show implementation plan", "Propose a simplified mock PoC"]
}
```

All of that context must appear inside the question box with the choices, not
as an earlier chat paragraph or only as a description of one option. When a
complex request still needs a path choice, reading this reference is not a
reason to narrate the assessment: go straight to the question call without a
scope-summary preamble or progress update. Do not
duplicate or paraphrase the rationale outside the box. Keep choice labels short
and separate from the question body.
Use the same interactive selection for build/revise and plan/revise; follow
`SKILL.md` for the text-only fallback and cancelled questions.

If no mock alternative is appropriate, replace the example's mock explanation
with the value of revising the scope, and use
plan/revise instead. Do not imply that choosing a plan starts implementation.

For an external-system prototype, explicitly tell the user that it will use
mock data and will not connect to their real system or use an existing adapter,
connector, or MCP connection, even if already configured. Prefer `mock data`
or a concrete phrase such as `mock Jira issues` over `synthetic data`.
For example:

> Proposed local prototype — try the triage flow with mock
> Jira issues. This build will not connect to your Jira instance or use your
> existing Jira adapter. `INTEGRATION_PLAN.md` will explain how to connect the
> real system later as a separate development step.

If the user explicitly supplies safe local files, identify those files as the
source instead; do not call real local copies mock data. The no-live-adapter
boundary still applies. If the user chooses the plan-only outcome, explain that
no prototype or mock dataset will be built and the plan will be returned in chat.

## Decision rule

Ask:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials, live external calls, or real side
> effects?

Choose `local-prototype` only when the answer is yes. It may use supplied safe
workspace documents, generated Markdown, or deterministic synthetic records.
A future external read adapter may be represented by a clearly simulated tool
when that still demonstrates the core user experience.

Recommend `plan-only` for the original goal when live data or credentials are
essential, a real write or notification is required, the request needs approval
or durable multi-step execution, or it depends on production RAG, multi-agent
orchestration, deployment, or provider switching. Assess whether the bounded
mock alternative below is meaningful; otherwise keep plan/revise. Uncertainty
about a useful, safe local slice means no mock offer.

Use this rule before every proposal, including after revisions. Never silently
replace required live behavior with a mock. Use the conditional two-choice
menu in `SKILL.md`; neither classification nor requesting a proposal permits
a build.

## Simplified mock PoC for a complex request

Offer `Propose a simplified mock PoC` alongside `Show implementation plan` only
when a useful part of the idea can be explored with local mock records and at
most three deterministic read/compute tools in the existing starter. This is
an advisory or analytical slice, not a simulator of the whole external system.
Do not offer it if the user has ruled out mocks, asks only for a plan, or no
meaningful demonstration fits those limits. Do not repeatedly offer a declined
mock alternative; the user can explicitly revise that preference later.
An explicit request for the implementation plan selects that outcome directly;
do not require another confirmation before returning the chat-only plan.

Put the scope-first context above inside the question body, not a plan proposal
or separate chat message.
Selecting the mock option only requests a proposal: create no files,
inspect no credentials, and call no business systems. Show the reduced Purpose,
Name, Knowledge, and Actions with the existing build/revise choices. Preserve
the original goal and deferred requirements for `INTEGRATION_PLAN.md`; do not
present the reduced Purpose as fulfillment of that full goal.

For example, production Kubernetes auto-remediation can become **Kubernetes
Remediation Advisor**: inspect mock incident snapshots, suggest a response from
an example runbook, and explain risks or when human review is needed. State the
assumed rules and explicitly exclude cluster access, continuous monitoring,
actual restarts, rollback execution, and an implemented approval workflow.
Use disclosure such as:

> This PoC uses mock data and assumed rules to explore decisions. It will not
> connect to your cluster or use an existing adapter. It does not remediate or
> validate a real cluster; passing smoke tests does not prove production safety.

Only `Build proposed agent` confirming that displayed reduced scope enters the
existing local-prototype build. No new runtime, dependencies, workflow engine,
executable remediation tools, or background monitoring are added. Results must
describe recommendations or missing evidence, never claim a restart, approval,
payment, or notification occurred. Keep the limitation visible in the configured
UI Purpose, agent responses, and final handoff. Use the existing `knowledge`,
`tool`, and `not-found` smoke cases to check only the demonstrated sample logic.

## Revise the proposal in one reply

When the user selects `Revise proposed agent`, show the current four values in
one copyable text block, prefilled with the current proposal or inferred intent.
This user-requested editing block is not an upfront plan preview:

```text
Purpose: <current purpose>
Name: <current name>
Knowledge: <current data sources>
Actions: <current actions>
```

Ask the user to change any fields in one reply; they can paste an edited block
or simply describe their changes. Keep fields they do not change. Do not ask
four separate questions or require a special IDE form. If edits conflict with
preserved fields, flag the mismatch without silently rewriting those fields;
use the scope rule below to determine the safe outcome.

Apply the edits and rerun the scope decision: show an updated four-field
proposal for a local prototype, or the scope explanation and choices together
inside the question box for a complex request. A revision is not build approval,
even if it only renames the
agent. If new Knowledge or Actions require live access or real effects, return
to the complex-request decision and reassess mock eligibility, honoring any
rejection of mocks. Only a meaningfully revised local scope or the explicit
mock-proposal flow can lead back to a prototype; neither bypasses its build
confirmation. Do not silently drop requirements to make a request fit.

## Representative intents

| Purpose | Outcome |
|---|---|
| Answer HR questions using our SharePoint HR knowledge base. | Build from supplied or generated Markdown, label it a local prototype, and document the future SharePoint adapter. |
| Read Jira issues and summarize release blockers, using an already configured Jira adapter. | Offer mock Jira issues and clearly simulated read-only tools; explicitly leave the existing adapter unused and document the separate live integration. Essential live access routes through the complex-request choice. |
| Update Jira priorities, collect approval, and notify Slack. | Offer an integration plan or a proposal for mock priority recommendations; no Jira writes, approval workflow, or Slack delivery. |
| Continuously monitor production Kubernetes, restart services, and roll back failed deployments. | Offer the full implementation plan or a proposal for a mock incident/remediation advisor; disclose the reduced scope before building. |
| Connect to SAP and bank APIs, approve claims, reimburse employees, and notify managers. | Offer the full plan or a proposal to assess mock expense claims against sample rules; never approve, pay, or notify. |
| Remediate our real cluster; a mock is not useful. | Offer plan/revise only; create no files unless the user later explicitly changes scope and confirms a local build. |

## Prototype boundaries

Imported sources must be workspace-relative, regular `.md`, `.txt`, `.json`, or
`.csv` files. Reject symlinks, hidden paths, more than 10 files, any file over
1 MiB, or more than 5 MiB total. Never copy `.env` files or other likely secret
material.

Generated tools are bounded deterministic methods over local or synthetic data.
They may simulate a read result but must not contact SharePoint, Jira, a
database, SaaS, or another API. They must not perform writes, approvals,
purchases, notifications, or claim a simulated effect occurred. When an
external source is represented locally, the prototype's `INTEGRATION_PLAN.md`
describes the future adapter and developer-owned work.

## Prototype continuation plan

Keep `INTEGRATION_PLAN.md` short and specific to the generated prototype: name
the mock data or supplied local files, the simulated tools, and what has not
been connected. For a simplified PoC, also preserve the original full goal,
identify the demonstrated decision slice and assumed rules, and list the
behavior and safety properties that remain unvalidated. Map the local tools
and their input/output contracts to the future live adapter; list
authentication/permissions, failure handling, and tests the developer must add.
Organize the remaining work as ordered developer steps. An existing adapter can
be assessed later, but is not used or validated by the current build.

End with a copyable follow-up Copilot prompt to review the plan and implement
the real integration as separate developer-led work, confirming permissions,
scope, and test strategy first. Do not run that prompt automatically. Link this
file in the final prototype handoff so the user can find the continuation.

## MAF mapping for plans

- Use a narrow function tool for an on-demand lookup or action after a developer
  supplies the typed client, authentication, schema, and safety behavior. The
  model decides when to call a tool.
- Use middleware for cross-cutting validation, authorization checks, redaction,
  logging, and error handling. Middleware is not the business-system adapter.
- Use a context provider when configured history, profile, or retrieved context
  must be injected on every run. It does not create or authorize the source.
- Use a runtime Agent Skill for reusable domain instructions or trusted
  resources whose execution may remain adaptive. This is distinct from this
  Copilot build skill and does not provide a live connector.
- Use a workflow for explicit ordering, branching, human approval,
  checkpointing, or costly side effects. Such a workflow belongs in the
  integration plan, not this MVP prototype.

MAF coordinates components developers provide. It does not provision external
credentials or permissions, infer data contracts and business rules, make
side effects idempotent, create production retrieval infrastructure, or turn
checkpoints into transactions across external systems.

## Plan-only response

After the user selects `Show implementation plan`, return a concise chat
response with actionable developer steps for the full proposed goal. Make the
dependencies and first implementation step clear, without executing them. Cover:

1. recommended agent, tool, middleware, context-provider, and workflow shape;
2. each external adapter plus its authentication owner and required permission;
3. request/response data contracts and source-of-truth decisions;
4. approval, idempotency, retry, compensation, and failure behavior for effects;
5. local, contract, integration, and end-to-end tests;
6. deployment, secret-management, and observability responsibilities;
7. what the builder could provide later versus what remains developer-owned.

Do not create `INTEGRATION_PLAN.md` or any other file in plan-only mode.
