# Release Evidence Reviewer

A small .NET 10 agent that reviews local Markdown release evidence. It reports
`Ready`, `Blocked`, or `not_found`; it never approves or performs a release.

The chat UI lives in `wwwroot/index.html`: one self-contained, responsive page
with the same visual style as the custom starter, fixed release-review content,
and no frontend dependencies or separate build step.

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
The Progress value must be the Integration key used by the app to write traces,
not an MCP key. Do not create or copy a `.env` file into the project.
Secret Manager is for local development; use your deployment environment's
secret store in production.

## Build, smoke test, and run

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

For the UI, .NET chooses an available loopback port and prints it in the
standard `Now listening on: http://127.0.0.1:<port>` line. The smoke command
runs exactly three cases and prints one parseable `SMOKE_REPORT=<json>` line
with each case ID, status, and W3C trace ID. Its three non-secret prompts live
under `Smoke` in `appsettings.json`; the runner rejects missing or empty prompt
values.

A passing smoke run proves the local agent execution and tracing setup. The
emitted trace IDs do not independently prove backend ingestion; open the
[Progress Tracing page](https://observability.progress.com/observations) to
confirm that the traces arrived.

The template sends trace metadata to Progress Observability but disables raw
prompt and response capture (`RecordInputs=false`, `RecordOutputs=false`).
