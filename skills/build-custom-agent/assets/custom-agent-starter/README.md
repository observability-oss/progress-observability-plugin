# Custom Agent local prototype

A bounded .NET 10 starter for an agent grounded in local files or deterministic
synthetic data. It does not connect to or update a live business system.

## Configure

From this folder, store secrets outside the repository:

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set "Progress:Observability:ApiKey" "ac_p_..."
```

`AzureOpenAI:ApiKey` is optional when `DefaultAzureCredential` is configured.
The Progress value is the Integration key used by the app to write traces. It
is separate from the read-only MCP key used by Copilot to verify them.

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

Files in `data/` and tools representing future external sources must be labeled
as simulated. A live SharePoint, Jira, database, or other adapter remains
developer-owned work and is documented in `INTEGRATION_PLAN.md` when relevant.
