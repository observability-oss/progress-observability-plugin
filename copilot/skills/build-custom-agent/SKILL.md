---
name: build-custom-agent
description: Build a bounded .NET 10 local agent prototype from a short Purpose. For requests requiring live access, real effects, or complex orchestration, offer an integration plan and, when meaningful, a separately confirmed simplified mock PoC. Use for the custom-agent path, not a ready template or an existing app.
---

# Build a custom agent

<!-- copilot:start -->
This workflow has two honest outcomes: a safe local prototype, or an integration
plan returned only in chat. It never implements live business-system access or
real side effects.

Treat `Try-and-refine: on` or `Try-and-refine: off` as an optional workflow
preference, not part of Purpose. Retain the latest explicit choice across
revisions and path changes; do not ask an extra question about it.

When Purpose is supplied, read the routing reference without a scenario-summary
preamble. If a complex request needs a choice, go directly from that read to
the question tool: its question body owns the scope explanation. Do not repeat
or paraphrase that explanation in progress commentary before or after the read.
This does not suppress the missing-Purpose question, optional ambiguity
clarification, local proposal, explicit plan response, or text fallback below.

## Intake and routing

1. Purpose is required. If the user has not already supplied it, ask this exact
   first question:

   > In 1-2 short sentences, what should this agent help someone accomplish?

2. Read this skill's `references/scope-routing.md`. Normalize Purpose and infer
   a bounded name, knowledge mode, and at most three actions. Ask at most one
   clarification, and only when Purpose is not actionable under that reference.
   Never ask for credentials or secret values.

3. Assess the current requested behavior, not just named systems. Advisory
   Jira triage or SharePoint Q&A without required live access goes directly to
   a disclosed local proposal when safe local sources fit the request, not an
   extra plan/mock gate. Then choose:

   - **Buildable local request:** show a compact **Proposed local prototype**
     with Purpose, Name, Knowledge, and Actions. Nothing has been built yet.
     Name the mock data or supplied local files and say that no live
     business-system adapter will be used, even if available. Prefer plain
     language such as `mock Jira issues`, not `synthetic data`.
   - **Complex/live request:** prepare a brief scope explanation for the
     interactive question body, not a separate chat message. Begin with
     `This agent needs ...`. In one compact
     paragraph, explain (1) which live dependencies, ongoing operation, or consequential
     effects keep the full request outside the local MVP build; (2) why each
     offered path is useful; and (3) what a mock PoC would not connect to, do,
     or validate. For plan/mock, say the plan guides full developer
     implementation while the mock PoC validates a bounded decision slice. For
     plan/revise, say revision can narrow the scope to a safe local prototype.
     Do not expose mode labels or show a plan heading, plan preview, or
     four-field proposal at this step. The reference supplies an example.

   An explicit request for an implementation plan already selects that outcome:
   return the chat-only plan without another menu or confirmation.
   Declining mocks is not a request for a plan: retain the native plan/revise
   choice unless the user explicitly asks for the plan.

   Present exactly two choices for the current situation through the host's
   interactive question tool. A simplified mock PoC uses the same
   local-prototype outcome, not a third mode:

   | Situation | Choices |
   |---|---|
   | **Proposed local prototype** | `Build proposed agent` or `Revise proposed agent` |
   | Complex request with a meaningful bounded mock alternative | `Show implementation plan` or `Propose a simplified mock PoC` |
   | Complex request when mocks are declined or no suitable PoC exists | `Show implementation plan` or `Revise proposed agent` |

   Call Copilot CLI's `ask_user` with the two labels in its structured `choices`
   array. For complex requests, set `question` to
   `<brief scope explanation>\n\nHow would you like to continue?` so the rationale
   and options appear together inside "Copilot needs information." Do not send
   the rationale as a standalone chat paragraph or put it only in option help
   text. For every local proposal (initial, revised, or simplified mock), show
   the four-field summary, then actually call the tool with the short question
   `How would you like to continue?` and exactly
   `choices: ["Build proposed agent", "Revise proposed agent"]`.
   Ending with that question as ordinary text does not present a native picker.
   In VS Code Copilot, use `vscode/askQuestions` with the same question-body
   content and equivalent single-select options when available. Do not just
   print Markdown bullets or embed the options only in the question text.
   Apply this to every choice gate, including revised complex requests.
   If no interactive question tool is available, show the context and two
   numbered text choices together, then wait for an explicit reply; never
   silently select an outcome.

   Follow the reference's eligibility rules; never offer to build the original
   live behavior or silently substitute a mock.

4. Wait for the question tool's returned selection (or an explicit text reply
   in the fallback). A skipped, cancelled, empty, or failed question is not a
   selection: stop and wait, without choosing a default or building.

   - `Propose a simplified mock PoC`: show a reduced-scope Purpose, Name,
     Knowledge, and Actions proposal plus what it does not implement or prove.
     Ask `Build proposed agent` / `Revise proposed agent` through the same
     interactive question tool and wait. This selection creates no files and
     is not build approval; keep the original
     goal for the continuation plan. Do not start another intake interview.
   - `Build proposed agent`: confirms the latest displayed, in-scope local
     proposal, including any scope reduction. Start without more intake or
     customization questions. Missing prerequisites or execution failures still
     stop with safe setup guidance.
   - `Show implementation plan`: return the plan in chat without creating files.
   - `Revise proposed agent`: follow the reference's one-reply revision flow,
     then reassess scope and use step 3 for the next response.

   No project writes before the latest local proposal is accepted.

## Safe local prototype

Use the requested target or a lowercase-hyphen folder inferred from the name.
Require the .NET 10 SDK, locate this skill's directory, then run:

```bash
dotnet run --file <skill-directory>/scripts/copy-template.cs -- --target <target>
```

The copier accepts only a missing target or a real empty directory. Do not work
around a refusal, overwrite content, or reconstruct the starter.

Never source or copy a parent `.env`. The generated app reads local settings
from .NET user-secrets as documented in its README.

After copying, edits are restricted to:

- `AgentDefinition.cs`: only the bounded definition contract and one-to-three
  tool registrations;
- `Tools.cs`: at most three deterministic local or clearly simulated tools;
- regular supported files under `docs/` and `data/`;
- `appsettings.json`: the `Agent` display name, service slug, Purpose,
  instructions, examples, UI preset (`knowledge`, `review`, `workflow`, or
  `analysis`), input placeholder, plus prompts and expected markers for the
  fixed smoke cases `knowledge`, `tool`, and `not-found`; keep every other
  setting fixed;
- `INTEGRATION_PLAN.md`: required for a future external adapter or a simplified
  mock PoC's deferred full scope; omit it otherwise.

Choose the closest UI preset from Purpose: `knowledge` for reference Q&A,
`review` for checking evidence, `workflow` for triage or process work, and
`analysis` for summaries or metrics. Keep the input placeholder to one short,
scenario-specific example of what the user can ask.
Keep custom instructions aligned with the fixed response policy: concise plain
text, source and exact section citations, and explicit source referrals or
missing evidence. Do not request diagnostic tokens unless the user asks to debug.
Reuse actual source labels from the local content tools; never invent a filename
in tool results, descriptions, instructions, or smoke markers. Literal `docs/`
and `data/` file references must resolve to bundled files.

Do not edit `Program.cs`, `AgentRuntime.cs`, `ChatHistory.cs`, `KnowledgeBase.cs`,
`SmokeRunner.cs`, the project file, HTTP/UI/health code, model or observability
wiring, package versions, or any other file. Do not add dependencies. Do not
generate network calls, process execution, filesystem writes, secret reads, or
real side effects.
Do not invoke an existing adapter, installed connector, or business-system MCP
tool to fetch real records during intake, building, or smoke tests. Its
availability does not change the MVP scope; Azure OpenAI and Progress runtime
connections remain as configured.
Label mock records and simulated results plainly. For a simplified mock PoC,
make the reduced scope and mock-data limitation visible in the configured
Purpose and retain it in the agent instructions and responses. Demonstrate
only local lookups, calculations, or recommendations, never successful real
effects or a background control loop. Keep the UI shell unchanged.
For an external-source prototype or simplified mock PoC, create
`INTEGRATION_PLAN.md` with the reference's separate, developer-led continuation
steps. Do not execute that follow-up as part of this build.

Run the project validator before building. After it passes, copy the current
`appsettings.json` once to `smoke-baseline.json` in a temporary directory outside
the project, before the first smoke run. Keep that snapshot unchanged and use
`--smoke-baseline <snapshot>` on every subsequent validation, including after
repairs. It freezes the three smoke prompts and expected markers, not the Agent
settings. Never delete or replace it to get a pass. If a test expectation really
needs redesign, report the build as unverified instead of silently weakening it.

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target>
# Save the validated appsettings.json to the temporary smoke baseline here.
dotnet build <target>/CustomAgent.csproj
dotnet run --project <target>/CustomAgent.csproj -- --smoke
```

Parse the single-line `SMOKE_REPORT=<json>` marker. Require overall `pass` and
exactly three passing results with IDs `knowledge`, `tool`, and `not-found`.
Use ordinary questions without expected-answer hints; keep expected markers
separate, grounded in stable facts or source references rather than `status=`
or `mode=` tokens. Green smokes are execution and content checks, not proof of
reasoning quality, actual tool invocation, or backend ingestion.
For a simplified mock PoC, these exercise sample knowledge, a local decision,
and a missing-record path; they do not validate live integration, real outcomes,
or production safety.
Preserve their exact trace IDs as local execution evidence. If app configuration
is missing, report only the missing configuration names and point to the
starter README's user-secrets commands; never request, inspect, or print values.

Start the already-built app on an OS-assigned loopback port:

```bash
dotnet run --project <target>/CustomAgent.csproj --no-build -- --urls http://127.0.0.1:0
```

Launch it as a persistent background process, not merely a command that is
asynchronous while Copilot is open. In Copilot CLI use `bash` with
`mode: "async", detach: true`; keep its shell/process ID for later cleanup or
restart. Other hosts should use their supported persistent-process equivalent.
If persistence is unavailable, disclose that the UI stops with the session.
Keep that process running and wait for its own standard
`Now listening on: http://127.0.0.1:<port>` line. Verify `/api/health` only at
that exact URL. Never guess, scan, or reuse a default or nearby port. If the
process exits or never prints the listening line, the health gate fails. Run
the validator again with the frozen smoke baseline before the optional review
or handoff:

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target> --smoke-baseline <snapshot>
```

## Optional try and refine

Default: `Try-and-refine: on`.

Only after a confirmed local prototype passes all the required gates above,
and the preference is not `off`, read and follow
`references/try-and-refine.md` once. Otherwise skip that reference and its extra
calls and edits. Plan-only never enters this step. This is a build-session
preference, not a generated app setting; it changes none of the required gates.
If the reference is unavailable, report the optional check as skipped and
continue to handoff without reconstructing it. For a prototype with the
preference `off`, add only `Behavior check: skipped (off)` to the handoff.

## Prototype handoff

Report the absolute project path, verified
UI URL, three smoke results, and exactly one Progress Observability tracing-page link:
`https://observability.progress.com/observations`. Do not construct per-trace
deep links. Report success only when copy, both validations, build, all three
smoke cases, and health pass. State explicitly that backend trace ingestion is
not independently verified by this workflow.
For an external-source prototype or simplified mock PoC, repeat which mock data
or supplied local files were used, confirm that no live adapter was used, and
link `INTEGRATION_PLAN.md` for the remaining full-scope work. For a simplified
PoC, distinguish the tested sample decisions from unvalidated production behavior.

## Integration plan only

After `Show implementation plan` is selected, create no files and run no copier,
build, smoke, app, or trace commands. Return the plan in chat, covering the
proposed MAF shape, external adapters, authentication and permissions, data
contracts, side-effect controls, failure handling, tests, deployment,
observability, and developer-owned work. Do not call the named external systems,
inspect or configure credentials, or imply that
MAF supplies their integrations. This outcome is zero-write and credential-free.
Preserve requested automation; label recommended safety changes as changed
assumptions rather than silently substituting human approval for automation.
<!-- copilot:end -->
