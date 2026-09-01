---
name: build-template-agent
description: Copy, build, smoke-test, and run the finished .NET 10 Release Evidence Reviewer template. Use for the ready/prebuilt-template path, not for custom agent generation or instrumenting an existing app.
---

# Build the Release Evidence Reviewer

<!-- copilot:start -->
Materialize the finished `release-evidence-reviewer` asset without generating or
customizing source. This workflow writes a new project folder. The default target
is `./release-evidence-reviewer`. GitHub Copilot in agent mode is the default
customer execution surface.

## Workflow

1. Require the .NET 10 SDK. Use the requested target folder, or the default
   above. Do not ask domain or design questions: this route has one fixed
   template. Locate this skill's own directory, then run its package-free .NET
   file-based helper:

   ```bash
   dotnet run --file <skill-directory>/scripts/copy-template.cs -- --target <target>
   ```

   The copier accepts only a missing target or a real empty directory. It must
   succeed before continuing. Do not work around a refusal, overwrite content,
   or reconstruct the asset from memory. This customer workflow does not
   require Python.

2. From the copied project, run `dotnet build`. Do not modify the copied source
   to make the build pass; report an asset defect if the unchanged template
   fails.

3. Never source or copy a parent `.env`. The app reads local settings from .NET
   user-secrets as documented in the copied README. Run all three local cases
   with `dotnet run -- --smoke`. Parse the single-line
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
   the copied README's user-secrets commands; never request, inspect, or print
   credential values.

4. Start the already-built web app, keep it running, and verify its `/api/health`
   endpoint. Use the local URL printed by the app rather than guessing it.

5. Return exactly one Progress Observability **Tracing page** link:
   `https://observability.progress.com/observations`. Do not resolve, construct,
   or list per-trace deep links.

Report success only after copy, build, all three smoke cases, and UI health pass.
Return the absolute project path, verified local UI link, one Progress
Observability Tracing page link, and the three smoke-case results with their
emitted trace IDs. Those IDs are local execution evidence; this workflow does
not verify backend ingestion. State explicitly that backend trace ingestion is
not independently verified by this workflow. On a failure, report the failed
gate and the smallest safe retry; do not claim the template is ready.
<!-- copilot:end -->
