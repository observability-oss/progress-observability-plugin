# Ticket Triage

A small .NET 10 agent for a fictional product-support inbox. Seven JSON tickets
and one Markdown policy demonstrate read-only queue and priority suggestions.
It never assigns, sends, persists, or connects to Jira/Slack. Structured intake
fields—not title keywords or model guesses—determine the recommendation.

## Run

Use the shared Secret Manager ID `Progress.AgentBuilder.Mvp` to configure
`AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, and optionally
`AzureOpenAI:ApiKey` (otherwise `DefaultAzureCredential` is used).
`Progress:Observability:ApiKey` is the Progress **Integration** key, not an MCP
key. Store secrets locally with `dotnet user-secrets` or in deployment environment
variables; never put them in this project, its fixtures, or a `.env` file.

Configure once in your terminal (replace placeholders locally, not in chat):

```bash
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:Deployment" "gpt-4.1"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "AzureOpenAI:ApiKey" "YOUR-AZURE-OPENAI-KEY"
dotnet user-secrets set --id Progress.AgentBuilder.Mvp "Progress:Observability:ApiKey" "ac_p_..."
```

Skip the API-key command when Azure identity is configured. Secret Manager is
for local development; use your deployment environment's secret store in production.

```bash
dotnet build
dotnet run -- --smoke
dotnet run --no-build -- --urls http://127.0.0.1:0
```

Open the actual `Now listening on:` loopback URL printed by the started process.
The single responsive `wwwroot/index.html` is an inbox/detail/recommendation UI;
it has no frontend dependencies or separate build. Select a ticket and choose
**Suggest triage**, then **Ask about this ticket** (for example, "Why P1?"). Each
question is answered from that selected ticket's real tool evidence, not a shared
chat history. The endpoint returns the deterministic tool result separately
from the model's short explanation, so UI queue/priority badges never parse prose.

When facts are missing, only those fields appear as typed choices. Supply one or
more and choose **Re-evaluate**. This is a **temporary scenario**, not a ticket
edit: supplied facts are labeled `temporary-scenario#<id>`, bundled facts keep
`tickets.json#<id>`, and the original values remain visible. **Clear scenario**
returns to the bundled ticket. Selecting any ticket clears the question and
scenario and cancels the previous in-flight response. Nothing is saved between
requests, page reloads, or users; the page sends the current scenario explicitly.

## Small, bounded contract

- `GetTicket`, `ReadTriagePolicy`, and `SuggestTriage` are available read-only tools.
  The required scoped inspection includes a deterministic recommendation preview;
  the model can read the full policy or explicitly recalculate when useful.
- Each run gets fresh tool state, at most eight tool invocations, a 45-second
  deadline, and only the selected ticket's scope. Three tool rounds plus one final
  synthesis request bound the model loop. The model chooses tool order.
- `proposed` supplies queue, priority, evidence, and policy references.
  `needs_information` names missing required fields without guessing a priority.
  An unknown ID returns `not_found` directly from the real `GetTicket` lookup.
  Validation requires actual selected-ticket inspection and a typed, policy-derived
  outcome, not a fixed three-tool sequence. Tool history records only actual calls.
- `GET /api/health`, `GET /api/tickets`, and `POST /api/triage` with
  `{"ticketId":"T-1001","question":"Why P1?"}` form the complete API.
  An optional `scenario` object may supply only required fields missing from the
  bundled ticket. For T-1005, for example:
  `{"environment":"production","impact":"single_customer","workaroundAvailable":false}`.
  Questions are limited to 1,000 characters. Unknown fields, duplicate JSON keys,
  invalid enum/boolean values, and overrides of existing or irrelevant facts are
  rejected before a model call. No ticket-mutation route exists.
- JSON unknown fields, invalid enum values, duplicate IDs, malformed input, and
  missing policy headings fail fixture loading. Missing intake facts are allowed
  and explicitly represented as null. This is routing, not free-text classification.

`--smoke` requires the model and Integration-key configuration, runs exactly
`clear-routing`, `missing-information`, and `unknown-not-found`, and prints one
`SMOKE_REPORT=<json>` with case status and W3C trace IDs. Assertions check actual
tool use and typed decisions/evidence, not only answer wording. Each case is a
real model run; local deterministic/scripted tests do not prove this live execution.
Emitted trace IDs do not independently prove backend ingestion: confirm them in
[Progress Tracing](https://observability.progress.com/observations).

Each run exports one trace shaped like the agent's real execution: a workflow
span, a `gen_ai.chat` span per model request, and a `gen_ai.execute_tool` span
per tool the model selected. Progress receives explicit metadata only — span
timing and status, the model and its provider-reported token counts, and the
declared tool name. The SDK's automatic `AddObservability()` instrumentation is
omitted because pinned SDK 1.2.2 captures prompts and tool arguments despite its
content flags; the wrappers in `MetadataOnlyChatClient.cs` and
`MetadataOnlyTool.cs` produce the same span shape without prompts, answers, tool
arguments, results or exception text.
Failures expose only allowlisted internal reason codes and a trace ID, never raw
provider exceptions or credential values.
The bundled policy is illustrative, not a production SLA. Add real intake,
authentication, and reviewed operational policy separately before production use.
