# Build Custom Agent MVP

Branch: `build-custom-agent`, stacked on `build-template-agent` in the same
worktree.

## Goal

Add one low-friction Copilot skill that asks for Purpose and supports two
honest outcomes:

1. **Build a safe local prototype** when the core behavior can be demonstrated
   with workspace files or mock data. A complex idea may use this same path
   after the user confirms a meaningful, explicitly reduced-scope mock PoC.
2. **Provide an integration plan only** for the full idea when live systems,
   real side effects, or advanced architecture are needed. The user may choose
   this without building a prototype.

Decision question:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials, live external calls, or real side
> effects?

The model-provider and Progress Integration settings are required only for a
live Mode 1 smoke run and are stored with .NET Secret Manager. The builder does
not require MCP access. SharePoint, Jira, database, or other business-system
credentials remain deferred.

When no meaningful bounded mock slice exists or mocks are declined, offer
plan/revise only. Uncertainty about a safe and useful slice means no mock offer.
An explicit request for an implementation plan selects that outcome directly;
no additional confirmation is needed for the chat-only response.

## Representative intents

| Intent | Outcome |
|---|---|
| "Answer HR questions using our SharePoint HR knowledge base." | Build with supplied or generated Markdown; document the future SharePoint adapter. |
| "Read Jira issues and summarize release blockers." | Build with synthetic Jira records and clearly simulated read-only tools. |
| "Update Jira priorities, collect approval, and notify Slack." | Offer the full plan or a proposal for mock priority recommendations; no writes, approval workflow, or notifications. |
| "Monitor production Kubernetes and automatically restart or roll back services." | Offer the full plan or propose a mock incident/remediation advisor; get reduced-scope confirmation before building. |
| "Connect to SAP and bank APIs, approve expenses, reimburse, and notify managers." | Offer the full plan or propose assessment of mock expense claims using sample rules; no approvals, transfers, or notifications. |
| "Remediate the real cluster; a mock is not useful." | Plan/revise only. |

## IDE interaction

- If Purpose is missing, ask: `In 1-2 short sentences, what should this agent help someone accomplish?`
- Purpose is required. Infer Name, Knowledge, and up to three Actions when they
  are omitted.
- Ask at most one clarification only when Purpose is not actionable.
- Present every two-choice decision in the native question picker: CLI
  `ask_user`, or VS Code `vscode/askQuestions`. Keep complex-request context
  inside the question body immediately before `How would you like to continue?`,
  followed by the selectable options. Do not print a separate rationale in chat.
  Skip scenario-summary preambles before and after loading the routing reference.
  Local requests still show their four-field proposal before the picker.
  Fall back to numbered text only if no question tool is available. A cancelled
  or unanswered question never selects a default or authorizes a build.
- Inside a complex-request picker, briefly explain why the full goal is outside
  the local MVP, what value each offered path provides, and what the mock PoC
  cannot connect to, do, or validate. Keep internal mode names out of the UI.
- Determine scope internally. Show the four-field proposal only for a buildable
  local request or after the user requests a simplified mock PoC; nothing has
  been built yet.
- For a buildable external scenario, disclose mock data or supplied local files,
  no use of an existing live adapter, and continuation in `INTEGRATION_PLAN.md`.
- A **Proposed local prototype** offers `Build proposed agent` and
  `Revise proposed agent`. Building uses the displayed values without further
  customization questions; missing prerequisites or errors still stop the run.
- A complex/live request opens a question containing the brief scope explanation, not a plan
  heading, plan preview, or four-field block. Explain the separate developer
  work and, if a useful mock slice fits the starter, its capabilities and limits.
  Then offer `Show implementation plan` and
  `Propose a simplified mock PoC`. Otherwise, or if mocks are declined, offer
  `Show implementation plan` and `Revise proposed agent`.
- Requesting a mock PoC only shows its reduced Purpose, Name, Knowledge, and
  Actions plus excluded live behavior. Then offer build/revise and wait; create
  no files until `Build proposed agent` confirms that proposal. No new interview.
- The PoC uses the same fixed UI, up to three deterministic read/compute tools,
  and three smoke cases. Its configured UI Purpose, responses, and handoff make
  the mock-data limitation visible: sample logic is tested, not live outcomes
  or production safety. Preserve the full goal, assumptions, and remaining
  ordered developer steps in `INTEGRATION_PLAN.md`.
- Selecting the implementation plan, including requesting it explicitly in the
  initial prompt, returns it in chat with no project, traces, or extra confirmation.
  Its ordered steps support separate full implementation with a coding agent;
  choosing it does not start that implementation.
- Revision shows the four current fields in one copyable text block. Apply the
  user's changes from one reply, preserve unchanged fields, reassess scope, and
  show a local proposal or complex scope explanation as appropriate. A revision
  never authorizes a build, and required live behavior is never silently
  replaced with a mock.

## Planned files

```text
skills/build-custom-agent/
|-- SKILL.md
|-- references/
|   `-- scope-routing.md
|-- scripts/
|   |-- copy-template.cs
|   `-- validate-project.cs
|-- tests/
|   `-- helper-tests.cs
`-- assets/custom-agent-starter/
    |-- CustomAgent.csproj
    |-- Program.cs
    |-- AgentDefinition.cs
    |-- AgentRuntime.cs
    |-- KnowledgeBase.cs
    |-- Tools.cs
    |-- SmokeRunner.cs
    |-- appsettings.json
    |-- docs/
    |-- data/
    |-- wwwroot/index.html
    |-- README.md
    `-- .gitignore
```

Keep `Program.cs`, the HTTP routes, UI shell, model configuration,
observability lifecycle, health endpoint, and `SmokeRunner.cs` fixed. Copilot
may change only `AgentDefinition.cs`, `Tools.cs`, `docs/`, `data/`, the `Agent`
and `Smoke` values in `appsettings.json`, and `INTEGRATION_PLAN.md` for external
sources or a simplified PoC's deferred full scope. Keep the project name and
namespace `CustomAgent`; keep all other runtime and model configuration fixed.

`AgentDefinition.cs` owns the bounded definition contract and tool-registration
list; `appsettings.json` supplies its display name, service slug, Purpose,
instructions, and examples. Fixed `Program.cs` and `AgentRuntime.cs` consume
that contract and contain no domain-specific identity or tool names. The fixed
smoke-runner engine consumes exactly three declarative cases from configuration:
stable case ID, prompt, and bounded expected result markers.

## Implementation TODOs

### 0. Branch and baseline

- [x] Create `build-custom-agent` from `build-template-agent` in the existing
  feature worktree.
- [x] Add this implementation plan.
- [x] Resolve the inherited CI step that targets the currently absent
  `skills/build-template-agent/tests` directory, in a separate `chore:` commit.

### 1. Skill contract

- [x] Add `skills/build-custom-agent/SKILL.md` with the Purpose question,
  two-outcome decision rule, editable-file allowlist, and stopping conditions.
- [x] Add `references/scope-routing.md` with representative intents
  and the minimal MAF mapping for tools, middleware, and workflows.
- [x] Keep the builder independent from the read-only MCP contract.
- [x] Keep the current `scaffold-agent` unchanged during this iteration.
- [x] Use two mode-specific choices, a prefilled four-field revision block,
  and fresh scope/confirmation checks after each revision.
- [x] Offer a simplified mock-PoC proposal for eligible complex requests;
  preserve the original goal and require separate reduced-scope build
  confirmation. Respect declined mocks and keep the existing runtime unchanged.
- [x] Make complex intake scope-first: show only the explanation and choices,
  then reveal the actual plan or local-prototype proposal after selection.

### 2. Generic .NET 10 starter

- [x] Derive one domain-neutral starter from the Release Evidence Reviewer
  runtime; do not copy its release-specific language or rules.
- [x] Make UI name, description, examples, service slug, instructions, and
  smoke prompts configuration-driven.
- [x] Make fixed `Program.cs` and `AgentRuntime.cs` consume
  `AgentDefinition.cs`, including its tool-registration list and service
  identity; leave no inherited release-specific constants.
- [x] Make the fixed content loader and project file support `.md`/`.txt` from
  `docs/` and `.json`/`.csv` from `data/`, with source labels and deterministic
  `not_found` behavior.
- [x] Support up to three local or simulated tools in `Tools.cs`.
- [x] Make the fixed smoke-runner engine read three bounded declarative cases
  from configuration instead of containing domain assertions.
- [x] Label all synthetic data and tool results as `local prototype` or
  `simulated`; never report a mock side effect as completed.
- [x] Generate `INTEGRATION_PLAN.md` for an external source or simplified mock
  PoC, listing the original goal, sample limitations, and remaining developer work.

### 3. Safe materialization

- [x] Add a package-free C# copier based on the prebuilt-template helper.
- [x] Add a package-free C# validator that compares fixed files with the
  bundled starter, permits only the editable-file allowlist, rejects new
  packages, and blocks network/process/file-write/secret-reading APIs in
  generated C#.
- [x] Require a missing or real empty target and reject symlinks, overwrites,
  `..`, build output, and every `.env*` file.
- [x] Import only workspace-relative regular `.md`, `.txt`, `.json`, or `.csv`
  files: no symlinks or hidden paths, at most 10 files, 1 MiB per file, and
  5 MiB total.
- [x] Keep Python out of the customer workflow.

### 4. Build outcome

- [x] Infer bounded Name, Knowledge, Actions, sample content, and three smoke
  prompts from Purpose.
- [x] Use workspace-relative `.md`/`.txt` files when supplied; otherwise create
  small representative documents or synthetic structured records.
- [x] Run `dotnet build`, then exactly three smoke cases: known knowledge/data,
  a local or simulated tool behavior, and `not_found`.
- [x] Run the project validator before build and again before handoff.
- [x] Preserve the emitted trace IDs as local smoke evidence and state that
  backend ingestion is not independently verified.
- [x] Start the UI, verify `/api/health`, and return the project path, UI URL,
  smoke summary, and one Progress Observability tracing-page link.

### 5. Plan-only outcome

- [x] Make no project writes and do not run build, smoke, or trace checks.
- [x] Return a concise plan covering MAF shape, external adapters,
  authentication, permissions, data contracts, side-effect controls, failure
  handling, tests, deployment, and observability.
- [x] State which parts the builder provides and which remain developer-owned.
- [x] Return the plan in Copilot chat only. `INTEGRATION_PLAN.md` is created
  only inside an approved local prototype, never by the plan-only outcome.

### 6. Packaging

- [x] Add the skill to `scripts/build_copilot.py`, the instruction template,
  mirrored `copilot/skills/`, command/prompt surfaces, and both READMEs.
- [x] Add `build-custom-agent` to `scripts/sync_skill_refs.py`.
- [x] Extend both Copilot CI definitions to build the starter and run copier
  and project-validator tests.
- [x] Regenerate derived artifacts only from their canonical sources.

### 7. Acceptance checks

- [ ] Forward-test representative intents in fresh Copilot sessions.
- [ ] Confirm both prototype examples build, pass three smoke cases, expose a
  healthy UI, and emit trace IDs without live external calls.
- [ ] Confirm the plan-only example creates no files.
- [ ] In a fresh Copilot session, verify name-only revision preserves the other
  fields, a live-integration revision rechecks the complex-request choices, and
  every revised local proposal waits for build confirmation.
- [ ] In fresh Copilot sessions, check both Kubernetes and SAP/bank intents:
  initial scope explanation has no plan preview or four-field block; plan
  selection creates no files; requesting a mock PoC shows the reduced proposal
  without building; only its subsequent build confirmation starts the existing
  prototype flow. Verify declined mocks keep plan/revise only.
- [x] Independently dry-run six conversation sequences covering mock proposals,
  plan selection, revisions, rejected mocks, local files, and unclear intent.
  These check the instructions, not a full Copilot build run.
- [x] Recheck scope-first intake in four independent conversation dry runs:
  Kubernetes mock/build and plan selection, supplied HR files, and declined
  mocks with a name-only revision. No runtime or Copilot CLI was exercised.
- [x] In an actual interactive Copilot CLI 1.0.82 session with GPT-5.6 Terra
  medium, verify the Kubernetes Plan/Mock and subsequent Build/Revise decisions
  render as native selectable questions. Cancel Build/Revise and confirm zero
  project changes; no build or external-system action was run.
- [x] Re-run complex intake with the scope rationale inside the native question
  body: context, question, and choices render in the same box, without a separate
  rationale above it. Cancellation leaves the test folder empty; no MCP servers
  are connected and no build runs.
- [x] After a later user run reproduced an early duplicate, move its suppression
  ahead of the routing-reference read. Two fresh interactive Copilot CLI 1.0.82
  sessions with GPT-5.6 Terra medium show the Kubernetes rationale only inside
  the native question box. Both cancel without project files; these are
  question-only checks with read/question tools and no MCP connections, not
  prototype build tests or a guarantee of every future model response.
- [x] Run copier tests, starter build/tests, mirror checks, reference sync,
  `git diff --check`, and an independent skill review.

The fresh-session checks remain open until exercised in Copilot. Only actual
prototype build/smoke runs need the user-provided model and Progress Integration
settings under the shared `Progress.AgentBuilder.Mvp` user-secrets ID.

## Explicitly deferred

- Live SharePoint, Jira, database, SaaS, or other API adapters.
- Production credential provisioning.
- Real writes, approvals, purchases, notifications, or other side effects.
- Production vector RAG, multi-agent orchestration, durable workflows,
  deployment, and provider switching.
- Automatic dependency on a separate MAF skill.

## Product dependency to review

The platform can measure the `Start from scratch` selection. Purpose is now
entered in the IDE and must not be transmitted silently. Collecting raw Purpose
centrally requires a separate, consented telemetry contract; it is not part of
this skill implementation.
