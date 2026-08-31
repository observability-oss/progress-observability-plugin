# Custom-agent scope routing

Use this reference after Purpose is available. It decides whether the request
can become a truthful local prototype or must remain a zero-write integration
plan.

## Minimal conversation

Purpose is actionable when it names a concrete outcome and enough subject,
input, or action to infer a safe demonstration. If it is only a vague topic or
role, ask this one optional clarification:

> What should the agent mainly do: answer from information, analyze or summarize
> records, take actions in another system, or Decide for me?

Do not ask another clarification. If the result is still uncertain, route to
plan only. A user-requested `Revise purpose` is a replacement input, not an
additional clarification.

Normalize the result internally before proposing an outcome:

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

Do not put credentials, secret values, raw document contents, absolute paths,
or hidden reasoning in this record.

## Decision rule

Ask:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials, live external calls, or real side
> effects?

Choose `local-prototype` only when the answer is yes. It may use supplied safe
workspace documents, generated Markdown, or deterministic synthetic records.
A future external read adapter may be represented by a clearly simulated tool
when that still demonstrates the core user experience.

Choose `plan-only` when live data or credentials are essential, a real write or
notification is required, the request needs approval or durable multi-step
execution, or it depends on production RAG, multi-agent orchestration,
deployment, or provider switching. Choose plan only whenever uncertain.

`Decide for me` applies this same rule; it is not blanket permission to create
files. Before any write, state which outcome was selected and why.

## Representative intents

| Purpose | Outcome |
|---|---|
| Answer HR questions using our SharePoint HR knowledge base. | Build from supplied or generated Markdown, label it a local prototype, and document the future SharePoint adapter. |
| Read Jira issues and summarize release blockers. | Build with synthetic Jira records and clearly simulated read-only tools. |
| Update Jira priorities, collect approval, and notify Slack. | Return an integration plan in chat and create no project files. |

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

Return a concise chat response containing:

1. recommended agent, tool, middleware, context-provider, and workflow shape;
2. each external adapter plus its authentication owner and required permission;
3. request/response data contracts and source-of-truth decisions;
4. approval, idempotency, retry, compensation, and failure behavior for effects;
5. local, contract, integration, and end-to-end tests;
6. deployment, secret-management, and observability responsibilities;
7. what the builder could provide later versus what remains developer-owned.

Do not create `INTEGRATION_PLAN.md` or any other file in plan-only mode.
