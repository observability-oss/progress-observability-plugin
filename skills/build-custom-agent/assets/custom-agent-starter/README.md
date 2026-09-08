# Custom Agent local prototype

A bounded .NET 10 starter for an agent grounded in supplied local files or mock
data. It does not connect to or update a live business system, even if you
already have an adapter or connector configured.

For a simplified mock PoC, only the agreed local decision logic is demonstrated.
Mock inputs and assumed rules do not validate real outcomes or production
safety; `INTEGRATION_PLAN.md` describes the original goal and remaining work.

## Configure

Store the local builder settings once under the shared Secret Manager ID. These
commands can run from any folder, including before this project is copied:

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
```

`AzureOpenAI:ApiKey` is optional when `DefaultAzureCredential` is configured.
The Progress value is the Integration key used by the app to write traces, not
an MCP key. Do not create or copy a `.env` file into the project.
Secret Manager is for local development; use your deployment environment's
secret store in production.

## Build, smoke test, and run

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

For the UI, .NET chooses an available loopback port and prints it in the
standard `Now listening on: http://127.0.0.1:<port>` line. Smoke mode runs
exactly the `knowledge`, `tool`, and `not-found` cases configured in
`appsettings.json` and prints one `SMOKE_REPORT=<json>` line. Each run is capped
at 45 seconds, three tool iterations, 800 output tokens, and 8,000 streamed
characters.

Progress receives explicit metadata only: span timing and status, model names,
provider-reported token counts, and declared tool names. The pinned SDK's
automatic `AddObservability()` wrapper is intentionally not used because it can
capture prompts and tool arguments despite its content flags. The fixed
metadata-only wrappers omit prompts, answers, tool arguments, results, and
exception text.

A passing smoke run checks execution and expected answer fragments, without
telling the model those expected answers. It does not prove reasoning quality,
actual tool use, or production safety. The emitted trace IDs do not independently
prove backend ingestion; open the
[Progress Tracing page](https://observability.progress.com/observations) to
confirm that the traces arrived.

## Chat behavior

The chat sends up to six completed exchanges (at most 24,000 characters), plus
the current question, to the configured Azure model. History is kept only in
this browser tab's memory: **New chat** or a reload clears it. Older exchanges
fall out of context as those limits are reached; failed requests are not kept.
There is no shared server-side conversation or conversation database. Each smoke
case still starts independently.

Answers default to short plain text. Local Markdown passages retain their exact
section headings for citations; the agent can read the full bounded source when
more context is needed. Citations and model judgments still need review for
important decisions.

## Customization boundary

The builder may customize `AgentDefinition.cs`, `Tools.cs`, the `Content`,
`Agent`, and `Smoke` values in `appsettings.json`, supported files under `docs/`
and `data/`, and an optional `INTEGRATION_PLAN.md`. Every content file must have
one exact `Content:Sources` entry whose value is `mock` or `supplied`; startup
fails rather than reading undeclared files or falling back to the working
directory. The runtime, web routes, UI shell, smoke engine, project dependencies,
and observability wiring stay fixed.

The fixed UI reads its title, Purpose, suggested prompts, one of four visual
presets (`knowledge`, `review`, `workflow`, or `analysis`), and its input hint
from the `Agent` section. Customize those values in `appsettings.json`; do not
edit the generated copy under `bin/`.

Mock data and tools representing future external sources must use provenance
`mock`; user-supplied local files must use `supplied`. For a prototype
representing SharePoint, Jira, a database, or another external source, see
`INTEGRATION_PLAN.md` for the remaining adapter work and a follow-up Copilot
prompt. A simplified mock PoC also includes this plan to distinguish its tested
sample logic from the original full scope and deferred developer work.
Connecting the real system is a separate step, not part of this build. Other
local-file-only prototypes do not need that plan.

The project validator checks the fixed starter, allowed shape, declared content,
and obvious disallowed capabilities as defense in depth. It is not a sandbox or
a proof that arbitrary editable C# is safe; review `AgentDefinition.cs` and
`Tools.cs` before running code from an untrusted source.
