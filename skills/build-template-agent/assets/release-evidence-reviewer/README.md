# Release Evidence Reviewer

A small .NET 10 agent that reviews local Markdown release evidence. It reports
`Ready`, `Blocked`, or `not_found`; it never approves or performs a release.

## Configure

From this folder, store secrets outside the repository:

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set "Progress:Observability:ApiKey" "ac_p_..."
```

`AzureOpenAI:ApiKey` is optional when `DefaultAzureCredential` is configured.
The Progress value must be the Integration key used by the app to write traces,
not the separate MCP key used by your coding agent to read them.

## Build, smoke test, and run

```bash
dotnet build
dotnet run -- --smoke
dotnet run
```

The UI opens at <http://127.0.0.1:5078>. The smoke command runs exactly three
cases and prints one parseable `SMOKE_REPORT=<json>` line with each case ID,
status, and W3C trace ID. Its three non-secret prompts live under `Smoke` in
`appsettings.json`; the runner rejects missing or empty prompt values.

The template sends trace metadata to Progress Observability but disables raw
prompt and response capture (`RecordInputs=false`, `RecordOutputs=false`).
