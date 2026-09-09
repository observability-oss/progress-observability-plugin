# Release Evidence Reviewer

Runtime retrieval stays within the requested review: fresh unnamed questions
search policy only; a named/selected project adds only that project's evidence.
Unrequested readiness checks are rejected. Explicit project switches supersede
the previous selection; New review does not choose a project from retrieved docs.

A small .NET 10 agent that reviews local Markdown release evidence. It reports
`Ready`, `Blocked`, or `not_found`; it never approves or performs a release.

The chat UI lives in `wwwroot/index.html`: one self-contained, responsive page
with the same visual style as the custom starter, fixed release-review content,
and no frontend dependencies or separate build step.

Review Atlas, then ask “What is still missing?”: the next request carries the
project identified by the real readiness tool and just the last question/answer.
The agent checks current evidence again; a prior answer is never evidence.
Naming another project switches the review. **New review** clears the project,
last exchange and visible discussion, and cancels any in-flight response.
There is no server conversation store, database, or browser persistence.

`POST /api/chat` accepts `message` and optional `context: {project, question,
answer}`. Messages/prior questions are capped at 4,000 characters, prior answers
at 8,000, and project context at 100. Each request has fresh tool state, a
45-second deadline, at most six tool calls and four model requests. Replies
include the actual tool-derived project and tool history alongside the answer.

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

Each reply shows the documented evidence behind the verdict — every release
requirement, its documented value, whether it is satisfied, and the Markdown file
it came from — followed by the trace ID, the resolved project and the tools the
agent actually ran. Those rows are the typed result of `CheckReleaseReadiness`,
the same facts the model received, so the panel cannot disagree with the answer.
An unknown project shows no rows rather than inventing evidence.

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

Tracing uses the Progress SDK's `AddObservability()` setup in `Program.cs`.
`FunctionInvokingChatClient` already supplies tool tracing, so no additional tool
wrapper is needed ([SDK documentation](https://www.telerik.com/ai-observability-platform/documentation/sdk/dotnet#tool-observability)).
Static template identifiers use `AdditionalTags` for filtering.

`Progress:Observability:RecordInputs` and `Progress:Observability:RecordOutputs`
default to `true` for the local demo: LLM inputs and outputs are sent to Progress
when tracing is enabled. To disable the SDK's LLM message recording for one run,
replace the normal run command with these overrides for that process:

```bash
PROGRESS__OBSERVABILITY__RECORDINPUTS=false \
PROGRESS__OBSERVABILITY__RECORDOUTPUTS=false \
dotnet run --no-build -- --urls http://127.0.0.1:0
```

The overrides do not persist. These flags control the SDK's LLM message
recording; they do not independently control the function-invocation middleware's
native tool contents or fully exclude exception text. Even with both flags
`false`, do not assume content-free telemetry or full privacy with SDK 1.2.2.

The workflow span also records `agent.tool.count`, `agent.source.count` and
`agent.readiness.status`, so traces can be filtered by verdict and by how much
evidence backed it.
