---
name: build-template-agent
description: "Copy, build, smoke-test, and run a finished .NET 10 agent template: Release Evidence Reviewer, Docs Q&A, Operations Data Analyst, or Ticket Triage. Use for ready/prebuilt agents, not custom generation or existing apps."
---

# Build a ready agent template

<!-- copilot:start -->
Copy the selected finished asset without generating or customizing source.
This workflow writes one new project folder. GitHub Copilot in agent mode is
the default customer execution surface. It makes no MCP calls.

Use the bundled `templates.json` in this skill directory for the exact IDs,
project filenames, and three smoke-case IDs. A platform handoff supplies the
selected template ID:

| ID | Data and experience |
|---|---|
| `release-evidence-reviewer` | Markdown release evidence; contextual review chat and New review |
| `docs-qa` | Markdown Q&A; source-scoped questions and follow-ups (local lexical RAG) |
| `operations-data-analyst` | Mock CSV; ask questions, inspect KPIs, and change the dashboard view |
| `ticket-triage` | JSON inbox and Markdown policy; questions and temporary intake scenarios |

Honor the requested ID or unambiguous display name. An explicitly unknown
template is an error, not a request to invent one. If no template is specified,
preserve the `release-evidence-reviewer` default. The default target is `./<id>`.
Do not ask domain/design questions or route to the custom builder.

## Workflow

1. Require the .NET 10 SDK. Locate this skill's own directory and read the
   selected catalog entry. Run its package-free .NET file-based helper:

   ```bash
   dotnet run --file <skill-directory>/scripts/copy-template.cs -- --template <id> --target <target>
   ```

   The copier accepts only a missing target or a real empty directory. It must
   succeed before continuing. Do not work around a refusal, overwrite content,
   or reconstruct the asset from memory. This customer workflow does not
   require Python. `--list` prints the catalog without creating a project.

2. From the copied project, run `dotnet build`. Do not modify the copied source
   to make the build pass; report an asset defect if the unchanged template
   fails.

3. Never source or copy a parent `.env`. The app reads local settings from .NET
   user-secrets as documented in the copied README. Run all three local cases
   with `dotnet run -- --smoke`. Parse the single-line
   `SMOKE_REPORT=<json>` marker and require overall `pass` plus exactly the
   three passing case IDs from the selected catalog entry. Do not accept
   skipped tests, another template's cases, or a report from an earlier run.

   Preserve each exact trace ID emitted by the runner. Azure OpenAI settings
   (`AzureOpenAI:Endpoint`, `AzureOpenAI:Deployment`, and the optional
   `AzureOpenAI:ApiKey` when local Azure identity is not used) and
   `Progress:Observability:ApiKey` (an **Integration** credential) are app
   inputs. If configuration is missing, name only the missing keys and point to
   the copied README's user-secrets commands; never request, inspect, or print
   credential values. Let the app report missing configuration: do not run
   `dotnet user-secrets list`, read secret-store files, or dump the environment
   as a prerequisite check.
   Mock data describes bundled business fixtures, not a mock model: smoke cases
   and UI actions call configured Azure OpenAI. A successful run used available
   app configuration; never infer that credentials or model access are unnecessary.

4. Start the already-built web app on an OS-assigned loopback port:

   ```bash
   dotnet run --project <target>/<catalog-project> --no-build -- --urls http://127.0.0.1:0
   ```

   Keep that process running and wait for its own standard
   `Now listening on: http://127.0.0.1:<port>` line. Verify `/api/health` only
   at that exact URL. Never guess, scan, or reuse a default or nearby port. If
   the process exits or never prints the listening line, the health gate fails.
   Use the execution tool's background/session support; do not restart a healthy
   process just to detach it. Keep any necessary launch log beside the requested
   target, never in a shared `/tmp` filename.
   Require HTTP 200 with `status=ready` from `/api/health` and HTTP 200 from `/`;
   a degraded health response is not a pass. Read HTTP results directly without
   creating health-check files (for example, `curl --fail --silent --show-error
   <actual-url>/api/health`). If browser tools are available,
   also exercise the selected page's main action and inspect its result/error
   state. Never claim a browser interaction was checked when only HTTP was used.

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
