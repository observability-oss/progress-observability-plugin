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

For a prototype, use the skill's package-free .NET helpers, keep edits inside
the allowlist, pass validation and build, require all three smoke cases, verify
their trace IDs, and health-check the UI. Return the absolute project path,
verified UI URL, three smoke results, and one Progress Observability Tracing
page link: `https://observability.progress.com/observations`. Do not construct
per-trace deep links. For plan-only, return the integration plan in chat and
write nothing.
