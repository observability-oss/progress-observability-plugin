# Build Custom Agent MVP

Branch: `build-custom-agent`, stacked on `build-template-agent` in the same
worktree.

## Goal

Add one low-friction Copilot skill that asks for Purpose, then chooses between
two honest outcomes:

1. **Build a safe local prototype** when the core behavior can be demonstrated
   with workspace files or deterministic synthetic data.
2. **Provide an integration plan only** when the core behavior cannot be
   demonstrated meaningfully without live systems, real side effects, or
   advanced architecture.

Decision question:

> Can the core behavior be demonstrated safely and meaningfully without
> target-business-system credentials, live external calls, or real side
> effects?

The model-provider, Progress Integration, and read-only MCP credentials remain
required for live smoke and trace verification; they are separate from the
deferred SharePoint, Jira, database, or other business-system credentials.

When uncertain, choose the plan-only outcome.

## Representative intents

| Intent | Outcome |
|---|---|
| "Answer HR questions using our SharePoint HR knowledge base." | Build with supplied or generated Markdown; document the future SharePoint adapter. |
| "Read Jira issues and summarize release blockers." | Build with synthetic Jira records and clearly simulated read-only tools. |
| "Update Jira priorities, collect approval, and notify Slack." | Return an implementation plan; create no project files. |

## IDE interaction

- Ask first: `In 1-2 short sentences, what should this agent help someone accomplish?`
- Purpose is required. Infer Name, Knowledge, and up to three Actions when they
  are omitted.
- Ask at most one clarification only when Purpose is not actionable.
- For a buildable external scenario, explain that the generated project is a
  local prototype and that the live adapter is separate.
- Offer `Build local prototype`, `Show plan only`, and `Revise purpose`.
- `Decide for me` selects the safe local prototype when it is meaningful;
  otherwise it selects plan only.

## Planned files

```text
skills/build-custom-agent/
|-- SKILL.md
|-- references/
|   |-- scope-routing.md
|   `-- mcp.md
|-- scripts/
|   |-- copy-template.cs
|   `-- validate-project.cs
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
may change only `AgentDefinition.cs`, `Tools.cs`, `docs/`, `data/`, and the
configured smoke prompts. Keep the internal project name and namespace
`CustomAgent`; customize only display/configuration values.

`AgentDefinition.cs` owns display name, service slug, instructions, examples,
and the tool-registration list. Fixed `Program.cs` and `AgentRuntime.cs` consume
that contract and must not contain domain-specific identity or tool names. The
fixed smoke-runner engine consumes exactly three declarative cases from
configuration: stable case ID, prompt, and bounded expected result markers.

## Implementation TODOs

### 0. Branch and baseline

- [x] Create `build-custom-agent` from `build-template-agent` in the existing
  feature worktree.
- [x] Add this implementation plan.
- [ ] Resolve the inherited CI step that targets the currently absent
  `skills/build-template-agent/tests` directory, in a separate `chore:` commit.

### 1. Skill contract

- [ ] Add `skills/build-custom-agent/SKILL.md` with the Purpose question,
  two-outcome decision rule, editable-file allowlist, and stopping conditions.
- [ ] Add `references/scope-routing.md` with the three representative intents
  and the minimal MAF mapping for tools, middleware, and workflows.
- [ ] Add the generated read-only MCP reference used for trace verification.
- [ ] Keep the current `scaffold-agent` unchanged during this iteration.

### 2. Generic .NET 10 starter

- [ ] Derive one domain-neutral starter from the Release Evidence Reviewer
  runtime; do not copy its release-specific language or rules.
- [ ] Make UI name, description, examples, service slug, instructions, and
  smoke prompts configuration-driven.
- [ ] Make fixed `Program.cs` and `AgentRuntime.cs` consume
  `AgentDefinition.cs`, including its tool-registration list and service
  identity; leave no inherited release-specific constants.
- [ ] Make the fixed content loader and project file support `.md`/`.txt` from
  `docs/` and `.json`/`.csv` from `data/`, with source labels and deterministic
  `not_found` behavior.
- [ ] Support up to three local or simulated tools in `Tools.cs`.
- [ ] Make the fixed smoke-runner engine read three bounded declarative cases
  from configuration instead of containing domain assertions.
- [ ] Label all synthetic data and tool results as `local prototype` or
  `simulated`; never report a mock side effect as completed.
- [ ] Generate `INTEGRATION_PLAN.md` when the original Purpose names an
  external source, listing the separate live adapter work.

### 3. Safe materialization

- [ ] Add a package-free C# copier based on the prebuilt-template helper.
- [ ] Add a package-free C# validator that compares fixed files with the
  bundled starter, permits only the editable-file allowlist, rejects new
  packages, and blocks network/process/file-write/secret-reading APIs in
  generated C#.
- [ ] Require a missing or real empty target and reject symlinks, overwrites,
  `..`, build output, and secret `.env` files while retaining `.env.example`.
- [ ] Import only workspace-relative regular `.md`, `.txt`, `.json`, or `.csv`
  files: no symlinks or hidden paths, at most 10 files, 1 MiB per file, and
  5 MiB total.
- [ ] Keep Python out of the customer workflow.

### 4. Build outcome

- [ ] Infer bounded Name, Knowledge, Actions, sample content, and three smoke
  prompts from Purpose.
- [ ] Use workspace-relative `.md`/`.txt` files when supplied; otherwise create
  small representative documents or synthetic structured records.
- [ ] Run `dotnet build`, then exactly three smoke cases: known knowledge/data,
  a local or simulated tool behavior, and `not_found`.
- [ ] Run the project validator before build and again before handoff.
- [ ] Verify the emitted trace IDs with the read-only Progress MCP tools.
- [ ] Start the UI, verify `/api/health`, and return the project path, UI URL,
  smoke summary, and one Progress Observability tracing-page link.

### 5. Plan-only outcome

- [ ] Make no project writes and do not run build, smoke, or trace checks.
- [ ] Return a concise plan covering MAF shape, external adapters,
  authentication, permissions, data contracts, side-effect controls, failure
  handling, tests, deployment, and observability.
- [ ] State which parts the builder provides and which remain developer-owned.
- [ ] Return the plan in Copilot chat only. `INTEGRATION_PLAN.md` is created
  only inside an approved local prototype, never by the plan-only outcome.

### 6. Packaging

- [ ] Add the skill to `scripts/build_copilot.py`, the instruction template,
  mirrored `copilot/skills/`, command/prompt surfaces, and both READMEs.
- [ ] Add `build-custom-agent` to `scripts/sync_skill_refs.py`.
- [ ] Extend both Copilot CI definitions to build the starter and run copier
  and project-validator tests.
- [ ] Regenerate derived artifacts only from their canonical sources.

### 7. Acceptance checks

- [ ] Forward-test all three representative intents in fresh Copilot sessions.
- [ ] Confirm both prototype examples build, pass three smoke cases, expose a
  healthy UI, and produce observable traces without live external calls.
- [ ] Confirm the plan-only example creates no files.
- [ ] Run copier tests, starter build/tests, mirror checks, MCP-reference sync,
  `git diff --check`, and an independent skill review.

## Explicitly deferred

- Live SharePoint, Jira, database, SaaS, or other API adapters.
- Credential collection or secret provisioning.
- Real writes, approvals, purchases, notifications, or other side effects.
- Production vector RAG, multi-agent orchestration, durable workflows,
  deployment, and provider switching.
- Automatic dependency on a separate MAF skill.

## Product dependency to review

The platform can measure the `Start from scratch` selection. Purpose is now
entered in the IDE and must not be transmitted silently. Collecting raw Purpose
centrally requires a separate, consented telemetry contract; it is not part of
this skill implementation.
