---
name: build-custom-agent
description: Build a bounded .NET 10 local agent prototype from a short Purpose, or return a zero-write integration plan when live systems, real side effects, or advanced architecture are essential. Use for the custom-agent path, not a ready template or an existing app.
---

# Build a custom agent

<!-- copilot:start -->
This workflow has two honest outcomes: a safe local prototype, or an integration
plan returned only in chat. It never implements live business-system access or
real side effects.

## Intake and routing

1. Purpose is required. If the user has not already supplied it, ask this exact
   first question:

   > In 1-2 short sentences, what should this agent help someone accomplish?

2. Read this skill's `references/scope-routing.md`. Normalize Purpose and infer
   a bounded name, knowledge mode, and at most three actions. Ask at most one
   clarification, and only when Purpose is not actionable under that reference.
   Never ask for credentials or secret values.

3. Show the inferred summary and recommended outcome. Offer `Build local
   prototype`, `Show plan only`, `Revise purpose`, and `Decide for me`. Do not
   write files until `Build local prototype` is selected. `Decide for me`
   selects the prototype only when it is safe and meaningful; otherwise it
   selects plan only.

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
- optional `INTEGRATION_PLAN.md`, only when the prototype represents a future
  external adapter.

Choose the closest UI preset from Purpose: `knowledge` for reference Q&A,
`review` for checking evidence, `workflow` for triage or process work, and
`analysis` for summaries or metrics. Keep the input placeholder to one short,
scenario-specific example of what the user can ask.

Do not edit `Program.cs`, `AgentRuntime.cs`, `KnowledgeBase.cs`, `SmokeRunner.cs`,
the project file, HTTP/UI/health code, model or observability wiring, package
versions, or any other file. Do not add dependencies. Do not generate network
calls, process execution, filesystem writes, secret reads, or real side effects.
Label synthetic records and simulated results plainly. If Purpose names an
external source represented by local or synthetic data, create
`INTEGRATION_PLAN.md` describing the separate live adapter, authentication,
data contract, failure handling, tests, and developer-owned work.

Run the project validator before building:

```bash
dotnet run --file <skill-directory>/scripts/validate-project.cs -- --target <target>
dotnet build <target>/CustomAgent.csproj
dotnet run --project <target>/CustomAgent.csproj -- --smoke
```

Parse the single-line `SMOKE_REPORT=<json>` marker. Require overall `pass` and
exactly three passing results with IDs `knowledge`, `tool`, and `not-found`.
Preserve their exact trace IDs as local execution evidence. If app configuration
is missing, report only the missing configuration names and point to the
starter README's user-secrets commands; never request, inspect, or print values.

Start the already-built app, keep it running, and verify `/api/health` at the
local URL printed by the app. If the default port is occupied, retry with
`dotnet run --no-build -- --urls http://127.0.0.1:<free-port>` and use the URL
the app prints. Run the validator again before handoff. Report the absolute
project path, verified UI URL, three smoke results, and exactly one Progress
Observability tracing-page link:
`https://observability.progress.com/observations`. Do not construct per-trace
deep links. Report success only when copy, both validations, build, all three
smoke cases, and health pass. State explicitly that backend trace ingestion is
not independently verified by this workflow.

## Integration plan only

Create no files and run no copier, build, smoke, app, or trace commands. Return
the plan in chat, covering the proposed MAF shape, external adapters,
authentication and permissions, data contracts, side-effect controls, failure
handling, tests, deployment, observability, and developer-owned work. Do not
call the named external systems, inspect or configure credentials, or imply that
MAF supplies their integrations. This outcome is zero-write and credential-free.
<!-- copilot:end -->
