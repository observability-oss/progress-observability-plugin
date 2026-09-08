> Preserved development history from `d2516e3`. File layouts, TODOs and test counts below are historical. Start with the [current handoff](./agent-builder-handoff-2026-09-08.md) and [planning index](./README.md).

# Prebuilt template interaction upgrade — 2026-09-03

The four-template customer-workflow runs below are a historical snapshot.
Operations Data Analyst was subsequently simplified; its current behavior and
passing checks are recorded in [the Analyst follow-up](operations-data-analyst-validation.md).
Earlier failed runs remain below and are not counted as passing evidence.

## Accepted scope

| Template | Required interaction | Evidence needed |
|---|---|---|
| Operations Data Analyst | Free metrics questions, explicit view changes, clickable KPIs, daily trend and breakdown/comparison bars, Reset | Actual model questions change or preserve the intended view; chart and KPIs use deterministic figures and matching scope |
| Docs Q&A | One-exchange follow-ups, Ask about this section, clear source scope, New question | Context is sent; every answer still reads real allowed sources; reset and in-flight changes cannot leak prior results |
| Ticket Triage | Ask about selected ticket, missing-field-only intake, temporary scenario and Re-evaluate/reset | Typed scenario changes the decision without modifying fixtures; provenance distinguishes supplied facts; another ticket starts clean |
| Release Evidence Reviewer | Remember tool-identified project and last exchange, explicit project switch, New review | Follow-ups check the actual project again; new review has no hidden project; separate requests remain isolated |

All projects remain standalone .NET 10 assets with one HTML page, bundled mock
business data, read-only tools, bounded model execution and metadata-only traces.
No database, persistent conversation store, uploads, external ticket writes,
chart designer, generated SQL/code, or deployment is in scope.

## Verification gates

- Build and credential-free behavioral tests for all four canonical apps.
- Regenerated Copilot payload and catalog/copier/configuration regression tests.
- Fresh full CLI copy/build/three-live-smoke/UI-health sessions for all four.
- Live browser checks of the new interactions, reset/context boundaries,
  loading/cancellation behavior and narrow-screen layout, plus offline/API
  rejection and failure-path tests. No simulated browser network outage required.
- Final comparison of canonical, packaged and copied app bytes.

Model access is real; only business fixtures are mocked. Local trace IDs do not
prove Progress backend ingestion. No installation or publication is authorized.

## Current offline evidence

- Four canonical Release builds: zero warnings and errors.
- Template behavioral assertions: Release 68, Docs 96, Analyst 240,
  Ticket 121 (525 total). These execute real framework/tool
  loops with scripted model clients.
- Docs shipped-JavaScript state harness: 21 assertions, including follow-up,
  source scope, reset, cancellation and stale-response isolation.
- Existing custom-builder suites: 59 helper + 156 runtime assertions pass.
- Python copier/catalog/configuration suite: 14 tests pass.
- Copilot payload regenerated; 54 skill files mirrored. Skill validation and
  MCP-reference synchronization pass.
- Independent Analyst review found and verified fixes for accidental scope
  changes, submitted-question attribution and partially empty comparisons.

Fresh unchanged customer sessions are recorded under
`/Users/dipeykov/repos/agent-builder-tests/ux-20260903.Xj9UzR`.

## Current customer-workflow evidence

| Template | Session / folder | Latest smoke | Local UI |
|---|---|---|---|
| Release | `40839081-2e3f-42ec-abf1-b17d5ac85f4b` / `final-release` | 3/3 pass | `http://127.0.0.1:64359/` |
| Docs | `51698d0e-ffdc-48c9-92b6-517c7321b9cd` / `final-docs` | 3/3 pass; prior failed invocation retained | `http://127.0.0.1:64373/` |
| Ticket | `7ae63c47-ed82-49ca-a64e-4b64d0f282ca` / `final-ticket` | 3/3 pass | `http://127.0.0.1:64345/` |
| Analyst | `29ad2685-7c7a-4aed-beed-c859a00297e2` / `final-analyst-v7` | 3/3 pass on its first invocation | `http://127.0.0.1:50141/` |

Each folder retains `session.md` with raw tool-output `SMOKE_REPORT` evidence.
All four accepted apps built unchanged and passed their own process-assigned
port health/root-HTTP gates. Analyst v7's exact smoke traces are
`8ed04d48a2d39dff0f70cb5993644eb6` (aggregate),
`28d733fdec17523a7c25ec1f1844c36e` (comparison/outlier), and
`19dd5021a2123f874367f5d77a92872e` (empty selection), preserved in raw tool
output at `final-analyst-v7/session.md:215`. The previous passing v6 workflow
remains at `final-analyst-v6/session.md`; it is not current-package evidence.

The previous v5 comparison failed with trace
`f87a8e65a5b8165ce72dac175828d047`; its exact response was not retained. A
subsequent one-shot reproduction of the same request captured an undeclared
top-level `type` property and failed at strict intent parsing (`$.type`). An
offline test of the actual Azure adapter then proved the outgoing request used
JSON schema without provider strict mode. V6 enables that setting while keeping
local unknown/duplicate-property rejection. The new protocol regression failed
before the fix and passes after it; it verifies strict mode and all nested
object constraints on the actual serialized request, without network access.

A separate actual-function regression found omitted comparison arguments failed
binding. V6 makes them optional and inherits only the already-approved windows;
unapproved comparisons and explicit mismatches remain rejected. No retry,
permissive parser, weaker numeric predicate, new dependency or UI was added.
The older Analyst diagnostic preview at port 64667 has been stopped.

The v6 browser recheck exposed an incorrect model-selected busiest day (August
30 / 7,740 instead of August 24 / 9,920). V7 computes the maximum and all tied
dates inside `SummarizeMetrics`, after applying the approved selection, and
displays those tool figures in the evidence panel. Scope, narrowed dates,
empty/invalid selections and zero-volume ties are covered by six new assertions;
nine additional independent edge checks pass. This adds no tool, dependency or
layout control. V6's passing smokes alone did not catch this answer-quality bug.
The v7 browser question now returns August 24 / 9,920, matching its visible
tool evidence without changing the selected view. The replaced v6 process at
port 49351 is stopped; its source and transcript remain available.

After a successful bounded Docs diagnostic, its unchanged customer app passed
one authorized full rerun. Two earlier one-shot diagnostics failed before an
HTTP response (`ClientResultException` status 0 → `HttpRequestException` →
`SocketException`); the exact socket cause and relation to earlier CLI failures
remain unknown. Do not describe this as missing credentials or a proven outage.

An Analyst diagnostic classified the old `Call SummarizeMetrics` smoke wording
as unsupported. Its known/empty prompts now use ordinary user questions while
retaining every exact tool/numerical predicate. The corrected known and empty
cases passed; this was separate from the later strict-output defect.

The final canonical/Copilot/v7 snapshot contains 54 identical skill files.
Four standalone payloads total 252,213 bytes: Release/Docs each have five C#
files, Analyst seven and Ticket six. Maintainer tests, logs, secrets and build
artifacts do not ship. All 51 copied app files and 18 runtime content files
match the current assets; the SDK's additional compressed HTML also matches.

## Current browser evidence

- Release: fresh unnamed question remains policy-only; Atlas review and rollback
  follow-up retain Atlas/Blocked; New review clears project and conversation.
- Docs: 90-day audit answer; “Can that retention be extended?” resolves the prior
  topic and retrieves fresh evidence; export-section scope returns 24 hours;
  New question clears both prior exchange and source scope.
- Ticket: T-1005 starts missing three fields. Typed temporary facts produce
  Engineering/P2; a free question distinguishes bundled issue type from supplied
  assumptions. Clear scenario returns to missing information. `GET /api/tickets`
  still contains the original null facts.
- Analyst v7: the free-form busiest-day question returns August 24 / 9,920
  requests, matching visible tool evidence without changing the view (trace
  `d7feda134df4f80d980edef3193bf6ec`). A prompt selects Checkout latency for
  August 20–30, synchronizing inputs, daily chart and 317.37 ms weighted KPI
  (`1f4ffe0f8b1d45dea7c2c4b3704dac5a`). “Compare two periods” preserves that
  metric, expands to August 17–30 and shows the correct 200 / 373.44 ms bars
  (`94f3c9a36b224c033e1ce671791ac194`). Clicking Total requests and Daily line
  changes the chart locally; Reset clears question, submitted question, answer,
  trace and scope back to the all-service month. Earlier diagnostic browser
  checks additionally verified error dissection (Checkout 1,080 errors) and
  service-error bars of 300 / 1,080 / 150 on unchanged interaction logic.
- Analyst v7's narrow-screen check: 390 CSS px viewport, 371 px document
  width; prompt, chart and table containers remain inside the page. Other three
  narrow-screen and cancellation checks were performed on identical UI source
  before the telemetry-only correction. Docs additionally has 21 shipped-JS
  state assertions; all four have offline context/scope/cancellation coverage.

## Earlier live findings and fixes

The first upgraded snapshot passed four unchanged CLI workflows and 12/12 live
smoke cases. Independent browser testing then found two interaction defects;
that snapshot is not the final acceptance evidence for Analyst or Release:

- Analyst: after requesting Checkout latency for August 20–30, comparing August
  17–23 with August 24–30 could fail or give an unhelpful clarification. Live
  intent inspection found defaulted view fields conflicting with valid comparison
  windows. A nullable, default-free intent patch now preserves service/metric and
  gives a specific proposal. Live Apply produced the exact 200 / 373.439049 ms
  period bars. The second copied snapshot returned an intent-only response for
  the comparison smoke; the exact failed intent was not retained in those reports.
  Its three failing runs are preserved, not counted as passing evidence. The
  test request was clarified without weakening its tool/numeric assertions.
- Release: New review correctly cleared context and canceled pending output, but
  a new unnamed-project question could retrieve Atlas incidentally and assume it
  was the subject. Unscoped retrieval must remain policy-only; named-project
  evidence must be tied to the current request or visible review context. The
  corrected fresh copy now passes all three live cases and a browser question
  with no selected project asks which project instead of assuming Atlas.

Docs and Ticket browser checks passed: grounded one-exchange follow-up, exact
source-only answers and honest missing facts, typed partial/complete intake,
scenario provenance, reset, and cancellation without stale output. Ticket T-1005
stayed unchanged in `GET /api/tickets` after temporary Engineering/P2 evaluation.

The third Analyst customer copy passed its single full live smoke invocation;
the optional-filter fix is also independently verified by actual framework tests.
Earlier failures remain unclassified beyond one captured strict JSON parsing
rejection; later exact-capture probes passed. No provider outage was established.
An offline real-wrapper probe confirmed response format/schema forwarding intact.

## Telemetry correction

The installed SDK's client-local content-capture defaults do not inherit the
global `RecordInputs=false` / `RecordOutputs=false` settings. A declared health
flag was therefore insufficient evidence of metadata-only client instrumentation.
An offline test of installed SDK 1.2.2 then proved that even explicit client
flags still allow raw tool arguments in `gen_ai.response.tool_calls`, both
streaming and non-streaming. All four apps therefore omit automatic client
wrapping and retain only explicit workflow spans. There are no automatic
per-model/token-usage spans until that SDK behavior is fixed. A no-export test
captures the actual Docs tool workflow and failure path and verifies metadata
without question, answer, prior-turn, source or tool content.

Earlier demo processes were stopped; their files/transcripts remain. Test
prompts/data were synthetic. Backend ingestion or retention of earlier trace
content has not been verified. New previews use the corrected implementation.

The existing custom-agent starter has the same parameterless-client pattern;
it is outside this four-prebuilt-template change and is a separate follow-up.
