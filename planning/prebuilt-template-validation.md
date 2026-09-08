> Preserved development history from `d2516e3`. File layouts, TODOs and test counts below are historical. Start with the [current handoff](./agent-builder-handoff-2026-09-08.md) and [planning index](./README.md).

# Prebuilt template validation — 2026-09-03

Historical initial-template checkpoint. The subsequent
[interaction upgrade](./prebuilt-template-ux-validation.md) changes these assets;
its separate current-state evidence is required for the upgraded versions.

Build and CLI gates: **four complete workflows, 12/12 live smoke cases,
485 credential-free assertions, and 14 packaging/configuration tests passed**.
Every UI was exercised independently in the browser, including successful live
actions on the final copied apps. This is not a marketplace publication or a
verification of Progress backend ingestion.

## Scope

Three self-contained .NET 10 agents were added alongside the unchanged Release
Evidence Reviewer. The canonical catalog is
[`templates.json`](../skills/build-template-agent/templates.json). Access Request
Reviewer is deliberately absent. No custom-builder runtime or external platform
selector was changed, and nothing was installed, committed, pushed, or published.

| Template | Bundled evidence | Customer experience |
|---|---|---|
| Release Evidence Reviewer | Markdown release policy and projects | Existing review chat |
| Docs Q&A | Four Markdown documents / eight sections | Question, answer, source preview, document library |
| Operations Data Analyst | 90 CSV daily records across three services | Filters, weighted KPIs, SVG trend, paged rows, optional explanation |
| Ticket Triage | Seven JSON tickets and Markdown policy | Inbox, intake facts, queue/priority suggestion, missing information |

Each copied app has its own README, pinned dependencies, configuration, fixtures,
UI, and three live smoke cases. There is no shared runtime project, database,
frontend build, arbitrary code execution, upload, or business-system write tool.
All reuse the existing local Secret Manager ID and metadata-only telemetry.
Each new source payload is 42–58 KB before dependency restore, with five or six
C# files and one HTML page. Maintainer test harnesses are outside those payloads.

## Automated validation

- Four canonical and four fresh copier-produced Release builds: zero warnings
  and errors. Fresh output fixtures, HTML, and settings match source bytes.
- Maintainer runtime harnesses execute real MAF/tool loops with scripted model
  clients, covering domain invariants, call/deadline limits, cancellation,
  isolation, out-of-scope calls, and actual smoke-report/catalog agreement.
  Final counts: Docs 49, Analyst 131, Ticket 90 (270 new-template assertions).
- Analyst regression additionally invokes the customer copier, clean Debug build,
  and Release publish; both outputs must contain exactly the runtime-consumed CSV,
  HTML, and settings paths with identical bytes.
- Python packaging/configuration suite: 14 tests passed. Bad IDs/arguments,
  traversal, malformed catalogs, symlinks, nonempty destinations, and accidental
  secret/build-file copying are covered.
- Existing custom-builder helper/runtime suites: 59 + 156 assertions passed.
- Generated Copilot payload, skill references, skill validation, JavaScript
  syntax, and `git diff --check` passed.
- Independent distribution audit: all 52 current skill files match the canonical
  and Copilot payloads, and all four final copied apps match current assets.
  The final Ticket clean Release build passed with zero warnings/errors and exact
  fixture/UI/settings output paths and bytes.

## Full customer CLI sessions

Actual interactive Copilot CLI 1.0.82 sessions loaded isolated fixtures containing
the generated Copilot skill. They copied unchanged source into empty directories,
built, ran the three Azure OpenAI smoke cases, and started their own loopback
port-0 process. Checks use the process's emitted listening URL, ready health,
and root HTTP 200—not a guessed port. Local transcript exports preserve command
outputs, exact smoke reports, and trace IDs.

Local evidence root:
`/Users/dipeykov/repos/agent-builder-tests/prebuilt-20260903.VSBwM5`

| Final session directory | Session ID | Result |
|---|---|---|
| `final-release` | `3fcc1520-fd9e-4467-bd72-5478858a1a85` | Build, 3/3 live cases, ready UI |
| `final-docs` | `06e9dcd8-90ed-4165-808a-54cf562bb119` | Build, 3/3 live cases, ready UI |
| `final-analyst-v3` | `53dc4df0-26e0-4afd-b521-5c26b6130c63` | Build, 3/3 live cases, ready UI |
| `final-triage-v3` | `c5eb072f-519e-4947-b2e4-08ae0784edc1` | Build, 3/3 live cases, ready UI |

Each directory contains `session.md` and the unchanged copied `app/`. Earlier
failed/exploratory transcripts are retained separately; they are not counted as
final passing evidence. CLI startup showed inherited skill/MCP warnings; the
selected builder skill loaded and made no MCP calls. Final sessions did not list
secrets or inspect credential stores. The first exploratory Release session
listed redacted key names; the workflow now explicitly forbids that prerequisite.
An earlier Ticket session needed intervention after an unnecessary detach/restart
attempt requested a shared `/tmp` log. That write was denied. The skill now
retains the healthy process and prohibits shared temporary launch logs; the final
Ticket session kept its original process and a project-local launch log.
Its final narrative incorrectly inferred that mock data meant model credentials
were unnecessary. A no-command follow-up corrected that statement (preserved in
`session-corrected.md`); all smoke cases actually used configured Azure OpenAI.
The current skill explicitly distinguishes mock business fixtures from real
model calls. That three-line documentation clarification is the only difference
between the final CLI's v5 fixture and the current v6 skill payload; app bytes
are unchanged. The final narrative was therefore reviewed, not accepted blindly.

## Browser and HTTP checks

- Release: Atlas blocked, Orion ready, unknown project, loading and retry controls.
- Docs: grounded and two-source answers (90-day audit retention / 24-hour export
  availability), exact source switching, no-match response, busy input lock,
  and immutable submitted-question attribution after editing the next question.
- Analyst: 30→60 row expansion; Checkout Aug 1–3 produces 6,060 requests,
  60 errors, 0.99% weighted error rate and 200 ms; Aug 24 flags the seeded
  10% error / 900 ms spike. Explanations disclose actual tool/service/date scope.
  Empty dates and invalid reversed dates clear stale results; updates cancel
  in-flight explanations; the view recovers with valid dates.
- Ticket: production outage → Operations/P1; how-to → Product Support/P3;
  incomplete intake → no proposed queue/priority and explicit missing fields;
  empty filter, keyboard clear, loading, changed-selection cancellation, and
  visible error/retry state.
- Invalid Docs and Ticket inputs return HTTP 400; a ticket-assignment POST has
  no write route (405). No real ticket was assigned, changed, or sent.
- Analyst reversed dates return 400. A well-formed unknown service returns a
  typed empty result with no invented figures; it does not broaden the query.
- Desktop and narrow-screen layout checks use actual rendered DOM and screenshots;
  temporary browser viewport overrides are reset after testing. The corrected
  Analyst at 390 CSS pixels has no page overflow: document width 371, inner table
  viewport 318, table scroll width 498. Horizontal scroll stays inside the table.

Final copied-app browser evidence (local trace IDs, not ingestion proof):

| App / current local URL | Verified action | Trace |
|---|---|---|
| Release / `http://127.0.0.1:63548` | Orion ready | `089a92ba65c8d9ffe27d85befa123db0` |
| Docs / `http://127.0.0.1:63546` | Audit retention: 90 days with exact source | `3c46e915bf4fe8965784ee2dc049407b` |
| Analyst / `http://127.0.0.1:50055` | Checkout selected-period spike explanation | `ecce8ce9869d6c0b981d1316d432aedc` |
| Ticket / `http://127.0.0.1:53744` | Production outage: Operations/P1 | `859068cb7f6165c2ac65a62f3d8c8e14` |

The final Ticket browser ran five consecutive successful actions: T-1001,
T-1005, T-1003, T-1001, T-1005. Missing-information traces include
`86ff91bfbf05d4589a4c20fa6d3615ff` and `7b7303397ea0edf066e44fb533e879e5`;
how-to trace `a42c08ec67f2f72068a2b05a233446c7` proposed Product Support/P3.
All five displayed truthful `GetTicket` tool history. The final narrow-screen
Ticket check also had no page overflow (390 CSS pixels / document width 371).

## Defects found and resolved during validation

1. Nested MAF client wrapping could evade the intended model-request bound.
   The provided bounded client is now used directly; real-loop tests cover it.
2. Analyst tools could drift outside the dashboard selection. Runs now bind
   immutable normalized scope, refuse changed arguments, and display provenance.
3. Fresh Analyst output copied CSV into `data/data`. An explicit runtime path and
   clean copy/build/publish regression prevent stale outputs masking this defect.
4. Docs input edits could misattribute answers. Busy controls are locked and the
   result labels the submitted question.
5. Ticket could validly stop after an unknown-ticket lookup but fail the redundant
   tool-sequence requirement. `GetTicket` now returns the real ticket together
   with a deterministic, policy-derived recommendation preview. Validation checks
   actual scoped inspection and typed evidence, not all three tool names; full
   policy reading and recalculation remain available when useful. Actual tool
   history stays truthful. Scripted MAF regressions cover Get-only found/missing/
   unknown cases, Get+Suggest without policy, absent inspection, and wrong IDs.
   Five post-fix live web requests and three sequential smoke processes passed
   14/14 calls without retries, followed by the fresh final CLI and browser checks.
6. Analyst mobile grid sizing let the table widen the page. The grid child can
   now shrink; the table scrolls within its panel. A fresh full CLI run and
   independent browser action passed after this CSS fix.

Earlier Ticket UI requests for missing-information and known tickets showed safe
retry states. Their precise causes were not reproduced in diagnostic runs; they
are retained as observed failures, not counted as successful requests. The
redundant choreography guard was independently demonstrated and removed, but is
not asserted to be the proven cause of those specific traces. The final copied
UI passed both outcomes; future failures expose allowlisted reason codes and a
trace ID, never raw provider exceptions.

## Boundaries

These are local, synthetic, read-only demonstrations that require configured
model access. Live trace IDs prove local execution, **not successful Progress
backend ingestion**. Ingestion, external platform-card deployment, marketplace
installation, Windows execution, and production hardening were not verified.
