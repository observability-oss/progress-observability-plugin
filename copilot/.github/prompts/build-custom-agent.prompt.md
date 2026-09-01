---
mode: 'agent'
description: 'Turn a purpose into a safe local .NET 10 prototype or a no-write integration plan.'
---

In GitHub Copilot agent mode, follow the **build-custom-agent** workflow in
`.github/copilot-instructions.md`.

If Purpose was not supplied, ask only: `In 1-2 short sentences, what should this
agent help someone accomplish?` Infer the bounded details and present the
workflow's prototype-or-plan choice. Create no files until the user selects the
local prototype. Never call live business systems, perform real side effects,
or request or print secret values.

For a prototype, never source or copy a parent `.env`; use the generated
README's shared .NET user-secrets setup. Use the package-free .NET helpers, keep
edits inside the allowlist, pass both validations and build, require the three
`knowledge`, `tool`, and `not-found` smoke cases, and health-check the UI. Do not
call MCP. Return the absolute project path, verified UI URL, smoke results with
their emitted trace IDs, and one Progress Observability Tracing page link:
`https://observability.progress.com/observations`. State explicitly that backend
trace ingestion is not independently verified. For plan-only, return the plan
in chat, write nothing, and require no credentials.
