---
name: build-custom-agent
description: Build a bounded .NET 10 local agent prototype from a short Purpose. For requests requiring live access, real effects, or complex orchestration, offer an integration plan and, when meaningful, a separately confirmed simplified mock PoC. Use for the custom-agent path, not a ready template or an existing app.
---

# Build a custom agent

<!-- copilot:start -->
This workflow produces either a safe local prototype or a zero-write integration
plan in chat. It never implements live business-system access or real effects.
`Try-and-refine: on|off` is an optional workflow preference, not Purpose; retain
the latest explicit value across revisions and path changes without asking for it.

When Purpose is present, read `references/scope-routing.md` without a scenario
summary. For a complex path choice, go directly to the question tool: its body
contains the scope explanation. Do not duplicate that explanation in commentary.

## Intake and approval

1. If Purpose is absent, ask exactly:

   > In 1-2 short sentences, what should this agent help someone accomplish?

2. Apply the routing reference. Infer a bounded Name, Knowledge, and no more
   than three Actions. Ask at most its one permitted ambiguity clarification;
   never ask for credentials or secret values.

3. A buildable request gets a compact **Proposed local prototype** containing
   Purpose, Name, Knowledge, and Actions. Say which sources are user-supplied
   and which are mock, and that no live adapter will be used. Nothing is built
   yet. A complex/live request instead gets only the reference's scope-first
   question; do not show a plan preview or four-field block.

   Use exactly the applicable two choices:

   | Situation | Choices |
   |---|---|
   | Local proposal | `Build proposed agent` / `Revise proposed agent` |
   | Complex request with a useful bounded mock slice | `Show implementation plan` / `Propose a simplified mock PoC` |
   | Complex request without an eligible mock slice | `Show implementation plan` / `Revise proposed agent` |

   For a local proposal, call CLI `ask_user` (or VS Code
   `vscode/askQuestions`) with `How would you like to continue?` and exactly the
   Build/Revise labels. For a complex request, its `question` is
   `<scope explanation>\n\nHow would you like to continue?` with the applicable
   structured choices. Printing options as Markdown is not a picker. If no
   question tool exists, present the same context and two numbered choices,
   then wait. Cancellation, empty input, or tool failure selects nothing.

   An explicit implementation-plan request selects the plan directly. Declining
   mocks does not; keep plan/revise unless the user asks for the plan.

4. Interpret a returned selection only as follows:

   - `Propose a simplified mock PoC`: show the reduced four fields plus excluded
     behavior, then ask Build/Revise and wait. Preserve the full goal for
     `INTEGRATION_PLAN.md`; do not restart intake or create files.
   - `Build proposed agent`: approve only the latest displayed local scope.
     Begin without more customization questions; prerequisites may still stop it.
   - `Show implementation plan`: follow the zero-write plan section.
   - `Revise proposed agent`: use the reference's one-reply revision, reroute,
     and ask the appropriate next choice.

No project writes occur before explicit Build approval. Use interactive Copilot
for the guided flow. In noninteractive mode (such as `copilot -p`), return the
proposal and end the turn unless the conversation already contains explicit
approval of that exact displayed scope. Tool permission flags such as
`--allow-all` do not approve a proposed scope.

## Build the local prototype

Use the requested target or infer a lowercase-hyphen folder. Require .NET 10,
locate this skill, and run:

```bash
dotnet run --file <skill-directory>/scripts/copy-template.cs -- --target <target>
```

Accept only a missing target or a real empty directory. Never bypass a refusal,
overwrite content, reconstruct the starter, or source/copy a parent `.env`.

Edits are limited to:

- `AgentDefinition.cs`: its bounded definition and one-to-three registrations;
- `Tools.cs`: at most three deterministic local or clearly mock tools;
- regular `.md`/`.txt` files under `docs/` and `.json`/`.csv` under `data/`;
- `appsettings.json`: `Content:Sources`, `Agent`, and the three fixed `Smoke`
  cases. `Content:Sources` must map every content path exactly once to `mock` or
  `supplied`; use `supplied` only for files the user provided;
- `INTEGRATION_PLAN.md`, required only for an external-source prototype or
  simplified mock PoC.

Choose UI preset `knowledge` for Q&A, `review` for evidence checking,
`workflow` for triage/process work, or `analysis` for summaries/metrics. Keep
the input hint short and scenario-specific. Instructions must preserve concise
plain text, exact source/section citations, provenance, qualifications, and
honest missing evidence. Keep conditional rules within their source's scope;
a requirement for one case must not become a requirement for all cases.
Every literal `docs/` or `data/` reference must resolve.

All other files are fixed, including runtime, metadata-only telemetry wrappers,
HTTP/UI/health, project/packages, and smoke engine. Add no dependencies. Generate
no external calls, process execution, file writes, secret reads, live effects,
or background control loops. Do not use an installed adapter, connector, or MCP
tool during intake, build, or smoke. Azure OpenAI and Progress runtime settings
remain as documented user-secrets. The validator checks the fixed shape and
obvious forbidden capabilities as defense in depth; it is not a sandbox for
arbitrary C#.

For mock PoCs, keep the reduced scope and mock limitation visible in Purpose,
instructions, answers, and handoff. `INTEGRATION_PLAN.md` contains only the
separate developer continuation; never execute it during this build.

## Required gates

Validate before building. Once that passes, copy the current
`appsettings.json` to a temporary `smoke-baseline.json` outside the project,
before the first smoke. Never replace it. Every later validation, including
after repair, uses that snapshot; a genuine expectation redesign makes the
build unverified rather than authorizing a weaker baseline.

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target>
# Save the validated appsettings.json outside the project now.
dotnet build <target>/CustomAgent.csproj
dotnet run --project <target>/CustomAgent.csproj -- --smoke
```

Require one `SMOKE_REPORT=<json>` with overall `pass` and exactly the passing
IDs `knowledge`, `tool`, `not-found`. Prompts are ordinary questions; expected
markers remain separate and use stable facts/source paths, not internal
diagnostic tokens. Preserve trace IDs as local execution evidence. Smokes do
not prove reasoning quality, actual tool invocation, ingestion, live outcomes,
or production safety. On missing configuration, name only the missing keys and
refer to the README; never inspect or print values.

Start the built app persistently on an OS-assigned loopback port:

```bash
dotnet run --project <target>/CustomAgent.csproj --no-build -- --urls http://127.0.0.1:0
```

In GitHub Copilot CLI, launch `bash` with `mode: "async"` and `detach: true`,
leaving stdout and stderr attached to that session. When the launch returns a
shell ID, immediately call `read_bash` with the returned shell ID in the same
turn unless the URL is already present. Use short bounded reads for at most 30
seconds total and stop if the process exits. Do not wait for the server command
to complete, because a healthy server remains running, and do not end the turn
with a passive "Waiting..." status. On other hosts, use the persistent-process
mechanism and actively poll its output with the same deadline; if its output is
not readable, use one combined launch log beside the requested target, never a
shared `/tmp` filename, and disclose if the process cannot outlive the session.

Read only this process's own
`Now listening on: http://127.0.0.1:<port>` line and health-check that exact
process at `<url>/api/health`. Never guess or scan ports. A process exit,
missing listen line at the deadline, or failed health is a failed gate. Never
start a replacement because an output read was incomplete, and never restart
an already healthy process. Finally validate again:

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target> --smoke-baseline <snapshot>
```

## Optional try and refine

Default: `Try-and-refine: on`. Only after all required gates pass, read and
follow `references/try-and-refine.md` once unless preference is `off`. Plan-only
never enters it. If unavailable, report it skipped without reconstructing it;
for `off`, add `Behavior check: skipped (off)` to handoff. This preference is
not a generated setting and changes no required gate.

## Handoff

After all checks, including optional behavior checks, leave the app running
(in a background terminal in VS Code). If it was stopped, relaunch using the
persistent-start procedure above. Immediately before the final response, verify
both `/` and `/api/health` at its current URL, then include a clickable UI link.
Do not stop the app as test cleanup or substitute a launch command for a live link.

Report success only after copy, both validations, build, all smokes, and health.
Include the absolute project path, verified UI URL, three smoke results with
their emitted trace IDs, and exactly one link:
`https://observability.progress.com/observations`. Do not make
per-trace links; state that backend trace ingestion is not independently
verified. For external/mock prototypes, name mock and supplied sources, confirm
no live adapter was used, link `INTEGRATION_PLAN.md`, and distinguish tested
sample decisions from unvalidated production behavior.

## Integration plan only

After `Show implementation plan`, create no files and run no copier, build,
smoke, app, trace, credential, or external-system commands. Return a concise
chat plan covering MAF shape, adapters, authentication/permissions, data
contracts, effect controls, failures, tests, deployment, observability, and
developer ownership. Preserve requested automation; label safer alternatives
as changed assumptions rather than implying acceptance.
<!-- copilot:end -->
