---
description: Build the ready Release Evidence Reviewer template, pass three local smoke cases, and return its local UI and Tracing page.
argument-hint: "optional target folder; defaults to ./release-evidence-reviewer"
---

Use the `build-template-agent` skill.

Target: $ARGUMENTS

Use `./release-evidence-reviewer` when no target was supplied. Require .NET 10
and use the skill's package-free .NET helper to copy the fixed asset without
source generation or domain questions. Build it, run `dotnet run -- --smoke`,
and require the `SMOKE_REPORT=<json>` marker to pass for `policy-markdown`,
`atlas-blocked`, and `unknown-not-found`. Never source or copy a parent `.env`;
use the copied README's .NET user-secrets setup. If app configuration is
missing, name only the missing keys and point to those commands. Start and
health-check the local UI with
`dotnet run --project <target>/ReleaseEvidenceReviewer.csproj --no-build -- --urls http://127.0.0.1:0`.
Wait for that process's own `Now listening on:` URL and use only that URL; never
guess or scan ports.

Finish only with the absolute project path, one verified local UI link, one
Progress Observability Tracing page link, and the three smoke-case results.
Use exactly `https://observability.progress.com/observations` for the Tracing
page. Include the emitted trace IDs as local execution evidence, but state that
backend trace ingestion is not independently verified. Do not return three
per-trace links. Never request, inspect, or print secret values.
