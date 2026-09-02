---
mode: 'agent'
description: 'Turn a purpose into a safe local .NET 10 prototype or a no-write integration plan.'
---

In GitHub Copilot agent mode, follow the **build-custom-agent** workflow in
`.github/copilot-instructions.md`.

Skip scenario-summary preambles when loading the workflow or routing reference.
While a complex request awaits a choice, its scope explanation belongs only
inside the question body, never in progress commentary.

For every two-choice gate, call
`#tool:vscode/askQuestions` with one single-select question and the two options.
Use `ask_user` with structured `choices` in Copilot CLI. Do not merely print
bullets. If no question tool is available, follow the skill's text fallback;
cancelled or unanswered questions never authorize a build.
For a complex request, put the brief context inside the tool's question body,
followed by a blank line and `How would you like to continue?`. Do not print the
rationale as a separate chat message or put it only in option help text.
The context must explain why the full goal is outside the local MVP,
what value each offered path provides, and
what the mock PoC cannot connect to, do, or validate. Keep mode labels internal.

If Purpose was not supplied, ask only: `In 1-2 short sentences, what should this
agent help someone accomplish?` Assess scope internally. For a buildable local
request, show Purpose, Name, Knowledge, and Actions with
`Build proposed agent` / `Revise proposed agent`. For a complex request, first
show only a brief scope explanation and choices together in the question box,
without a plan heading, plan preview, or four-field block. Explain the separate developer work and
possible local slice, then offer `Show implementation plan` /
`Propose a simplified mock PoC` when that slice fits the starter. The plan guides
later full development with the user's coding agent; the mock option first
proposes reduced scope for review.
If mocks are declined or no suitable PoC exists, keep plan/revise. An explicit
request for the implementation plan returns it directly in chat without another
confirmation. Requesting a mock PoC only presents a reduced-scope four-field
proposal, its exclusions, and build/revise choices; create nothing and wait for
confirmation. Preserve the original goal for the continuation plan.
Revision uses one prefilled four-field
block: preserve unchanged values, reassess scope and mock eligibility, then
show a local proposal or complex scope explanation as appropriate. Create no
files until the user accepts the latest local proposal with
`Build proposed agent`, then proceed without further customization questions.

Explain that external-system prototypes use mock data or supplied local files;
an existing live adapter or connector is not used. Never call live business
systems, perform real side effects, or request or print secret values.

For a prototype, never source or copy a parent `.env`; use the generated
README's shared .NET user-secrets setup. Use the package-free .NET helpers, keep
edits inside the allowlist, pass both validations and build, require the three
`knowledge`, `tool`, and `not-found` smoke cases, and health-check the UI. Do not
call MCP. Return the absolute project path, verified UI URL, smoke results with
their emitted trace IDs, and one Progress Observability Tracing page link:
`https://observability.progress.com/observations`. State explicitly that backend
trace ingestion is not independently verified. For an external-source prototype
or simplified mock PoC, link `INTEGRATION_PLAN.md` with the original goal and
ordered developer-led continuation steps. Keep the PoC's mock-data and
reduced-scope limits visible in its UI Purpose, responses, and handoff; passing
sample smoke cases does not validate real outcomes or production safety. After
`Show implementation plan` is selected, return the plan in chat, write nothing,
and require no credentials.
