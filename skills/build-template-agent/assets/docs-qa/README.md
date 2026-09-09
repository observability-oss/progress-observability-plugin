# Docs Q&A (RAG)

A small .NET 10 read-only document workspace: ask about four bundled synthetic
Markdown manuals, inspect answers and open the exact source sections. Retrieval
is meaningful-token lexical search, not embeddings or a vector database.
Every displayed citation is captured from an actual `ReadSection` call after
`SearchDocuments`; source IDs are never scraped from model prose. Citations prove
which excerpts were read, not that every generated claim is automatically correct.

## Configure and run

Uses the same `Progress.AgentBuilder.Mvp` user-secrets ID as Release Evidence
Reviewer. Set `AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, optionally
`AzureOpenAI:ApiKey` (otherwise `DefaultAzureCredential`), and
`Progress:Observability:ApiKey` through .NET user-secrets or environment variables.
The Progress key is an Integration key, not an MCP key. Do not copy secrets or
`.env` files into this package. User-secrets are for local development only.

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

Open the actual `Now listening on:` loopback URL printed by the process. There
is no frontend build, database, upload endpoint or external document connector.
The source pane works without a model call, but application startup still needs
the configured Azure endpoint/deployment.

Use **Ask a follow-up** below an answer to carry the last question/answer into a
new lookup. **Ask about this section** scopes the next question to the current
source preview; the scope chip stays visible until cleared. Follow-ups can keep
that scope. **New question** cancels pending work and clears context, scope and
results. Merely previewing a source does not change the active scope. Each result
identifies its submitted question, prior question (if any), and source scope.

## Small runtime contract

- `GET /api/health`: corpus readiness and tracing state, not model connectivity.
- `GET /api/documents`: bundled sections only.
- `POST /api/ask` with `{ "question": "How long are audit logs retained?" }`:
  `status`, `answer`, structured `citations`, actual `toolCalls`, and `traceId`.
  Each answer shows its trace ID and the tools the agent actually ran beneath it.
- Optional request fields: `previousTurn: { "question": "...", "answer": "..." }`
  and `sourceId: "exports#export-availability"`. Context is one previous Q/A pair,
  not full conversation history; omit both fields for an independent lookup.
- Question, prior question and prior answer each allow 1–4000 trimmed characters;
  answers are capped at 4000 characters. Runs have a 45-second deadline,
  four model round trips and at most six tool calls. Tools/citations are isolated
  per request: follow-ups retrieve/read again, and prior model text never becomes
  citation evidence. No conversation is stored on the server. Search has no match
  on common words alone; an exact scope restricts both search and reads. Unknown
  or invalid source IDs return a safe 400 before any model request. Partial answers
  must state what the selected evidence does not cover; clear scope to search wider.
- UI uses text-only DOM rendering, accessible source buttons, responsive panels,
  and explicit loading, empty, no-match and failure states.

`--smoke` runs exactly `grounded-answer`, `two-sources`, `unknown-not-found`,
checking actual tool calls, source IDs, known fixture values and W3C trace IDs.
It requires model access and a Progress Integration key, and prints one
`SMOKE_REPORT=<json>` line.

Tracing uses the Progress SDK's standard `AddObservability()` setup in
`Program.cs` and `AddToolObservability()` for tools, under the existing workflow
span. `RecordInputs = false` and `RecordOutputs = false` disable LLM message
content recording. In pinned SDK 1.2.2, tool arguments/results and exception text
can still be recorded. These settings do not guarantee content-free telemetry
or full privacy.

A passing local smoke is not proof of backend ingestion: verify those exact
trace IDs on the [Progress Tracing page](https://observability.progress.com/observations).
