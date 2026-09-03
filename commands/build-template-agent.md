---
description: Build a selected ready .NET agent, pass its three local smoke cases, and return its local UI and Tracing page.
argument-hint: "[template ID] [target folder]; defaults to release-evidence-reviewer"
---

Use the `build-template-agent` skill.

Template and/or target: $ARGUMENTS

Resolve the template from the skill's `templates.json`; use
`release-evidence-reviewer` only when no template was specified. Reject an
explicitly unknown template. The default target is `./<template-id>`.
Require .NET 10 and use the skill's package-free copier with
`--template <id> --target <target>`; no source generation or domain questions.
Build it, run `dotnet run -- --smoke`, and require `SMOKE_REPORT=<json>` to pass
with exactly the selected catalog entry's three case IDs.
Never source or copy a parent `.env`;
use the copied README's .NET user-secrets setup. If app configuration is
missing, name only the missing keys and point to those commands. Start and
health-check the local UI with
`dotnet run --project <target>/<catalog-project> --no-build -- --urls http://127.0.0.1:0`.
Wait for that process's own `Now listening on:` URL and use only that URL; never
guess or scan ports. Require `status=ready` from `/api/health` and HTTP 200 from
`/`. When browser tools are available, exercise the main UI action as well;
distinguish browser verification from HTTP-only checks.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Include the emitted trace IDs as local execution evidence, but state that
backend trace ingestion is not independently verified. Do not return three
per-trace links. Never request, inspect, or print secret values.
