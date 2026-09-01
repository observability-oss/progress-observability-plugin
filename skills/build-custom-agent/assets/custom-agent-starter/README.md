# Custom Agent local prototype

A bounded .NET 10 starter for an agent grounded in local files or deterministic
synthetic data. It does not connect to or update a live business system.

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
dotnet run
```

The UI opens at <http://127.0.0.1:5078>. Smoke mode runs exactly the
`knowledge`, `tool`, and `not-found` cases configured in `appsettings.json` and
prints one `SMOKE_REPORT=<json>` line. Raw prompts and responses are not sent to
Progress telemetry (`RecordInputs=false`, `RecordOutputs=false`).

A passing smoke run proves the local agent execution and tracing setup. The
emitted trace IDs do not independently prove backend ingestion; open the
[Progress Tracing page](https://observability.progress.com/observations) to
confirm that the traces arrived.

If port 5078 is already in use, choose another loopback port without editing
the project:

```bash
dotnet run --no-build -- --urls http://127.0.0.1:5079
```

## Customization boundary

The builder may customize `AgentDefinition.cs`, `Tools.cs`, the `Agent` and
`Smoke` values in `appsettings.json`, supported files under `docs/` and `data/`,
and an optional `INTEGRATION_PLAN.md`. The runtime, web routes, UI shell, smoke
engine, project dependencies, and observability wiring stay fixed.

The fixed UI reads its title, Purpose, suggested prompts, one of four visual
presets (`knowledge`, `review`, `workflow`, or `analysis`), and its input hint
from the `Agent` section. Customize those values in `appsettings.json`; do not
edit the generated copy under `bin/`.

Files in `data/` and tools representing future external sources must be labeled
as simulated. A live SharePoint, Jira, database, or other adapter remains
developer-owned work and is documented in `INTEGRATION_PLAN.md` when relevant.
