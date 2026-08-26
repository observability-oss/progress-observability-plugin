---
name: build-template-agent
description: Copy, build, smoke-test, and run the finished .NET 10 Release Evidence Reviewer template. Use for the ready/prebuilt-template path, not for custom agent generation or instrumenting an existing app.
---

# Build the Release Evidence Reviewer

Read `references/mcp.md` before the trace-verification step. It defines the
read-only tool contract, credential scopes, and query limits.

<!-- copilot:start -->
Materialize the finished `release-evidence-reviewer` asset without generating or
customizing source. This workflow writes a new project folder. The default target
is `./release-evidence-reviewer`.

## Workflow

1. Use the requested target folder, or the default above. Do not ask domain or
   design questions: this route has one fixed template. Locate this skill's own
   directory, then run:

   ```bash
   python3 <skill-directory>/scripts/copy_template.py --target <target>
   ```

   The copier accepts only a missing target or a real empty directory. It must
   succeed before continuing. Do not work around a refusal, overwrite content,
   or reconstruct the asset from memory.

2. From the copied project, require the .NET 10 SDK and run `dotnet build`. Do
   not modify the copied source to make the build pass; report an asset defect if
   the unchanged template fails.

3. Run all three local cases with `dotnet run -- --smoke`. Parse the single-line
   `SMOKE_REPORT=<json>` marker and require overall `pass` plus exactly these
   passing case IDs:

   - `policy-markdown`: a question grounded in the Markdown release policy;
   - `atlas-blocked`: known project Atlas, whose missing rollback owner makes it
     `Blocked`;
   - `unknown-not-found`: an unknown project returns `not_found` rather than
     invented data.

   Preserve each exact trace ID emitted by the runner. Azure OpenAI settings
   (`AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, and the optional
   `AzureOpenAI:ApiKey` when local Azure identity is not used) and
   `Progress:Observability:ApiKey` (an **Integration** credential) are app
   inputs. If configuration is missing, name only the missing keys and point to
   the copied README; never request, inspect, or print credential values.

4. Verify those exact three traces with the existing read-only Progress
   Observability MCP tools. Use a narrow time window and service
   `release-evidence-reviewer`, match each emitted trace ID exactly, and inspect
   metadata only. Normal exporter delay may be retried briefly; do not substitute
   unrelated recent traces. The MCP credential belongs to Copilot and is
   separate from the app's Integration credential.

5. Start the already-built web app, keep it running, and verify its `/api/health`
   endpoint. Use the local URL printed by the app rather than guessing it.

6. Require one official per-trace **UI** URL for each verified ID, explicitly
   returned or supplied by the Progress platform card/lookup. Match each URL to
   its exact trace ID; do not construct URLs from an origin or guessed route.
   The `/api/Traces/<id>` endpoint is an authenticated API that requires tenant
   headers, not a usable user link. Never return it or the generic Observations
   page. If any official UI deep link is absent, stop with
   `TRACE_UI_DEEPLINK_UNAVAILABLE`, include all three verified trace IDs, and do
   not claim the template is ready.

Report success only after every gate passes: the absolute project path, the
verified local UI link, and three labeled official trace UI deep links with
their trace IDs. On a failure, report the failed gate and the smallest safe
retry; do not claim the template is ready.
<!-- copilot:end -->
