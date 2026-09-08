# Agent builder handoff — 8 September 2026

The development branch now contains the implementation through `e6e9521`, the
preserved planning history, the review that motivated the latest fixes, and a
self-contained interactive code walkthrough. This updates the
[6 September handoff](./agent-builder-handoff-2026-09-06.md).

## Branches and saved walkthrough

- `agent-builder-clear-pr` is the product/PR branch. Its fetched local and remote
  heads matched at `e6e95211ef9461058797094c07db921d549bb94a` during this update.
- `build-custom-agent` previously stopped at `d2516e3`. This update merges the
  three later commits: `5160cb7` (cleanup/hardening), `d34f734` (same-turn launch
  output handling), and `e6e9521` (grounding, provenance and validation fixes).
  The merge is local; it does not publish or push this development branch.
- The clean branch had intentionally removed `planning/`. This development
  merge retains its five original documents and labels them as historical.
- Development checkout:
  `/Users/dipeykov/repos/progress-observability-plugin-build-custom-agent`.
  The existing PR checkout remains on `agent-builder-clear-pr`.
- An existing uncommitted change to the PR checkout's generated Docs Q&A
  `MetadataOnlyChatClient.cs` was not included or altered. It must be reviewed
  separately; generated mirrors are not the source of truth.

Open [the saved walkthrough](./walkthroughs/agent-builder-2026-09-08.html) directly
in a browser. It has ten chapters, 218 files (134 canonical/support files and
84 generated mirrors), 647 annotated sections, 49 C#/.NET explanations, eight
illustrative flows, quizzes and browser-local progress. It contains all required
HTML, JavaScript, CSS and source text. Following a simulated flow does not run
an agent or call a model. Microsoft Learn links are optional external reading.

Its source snapshot is `3f6742062983`. The metadata records `d34f734` plus local
edits at generation time; all 218 embedded files were subsequently compared
with this development merge and match the committed implementation at
`e6e9521`. Keep that original metadata rather than implying the guide was
regenerated after the commit. The guide was copied byte-for-byte from the
session artifact. Its original authoring files remain locally under
`/Users/dipeykov/.codex/artifacts/agent-builder-walkthrough-2026-09-08`;
they are not required to read the saved HTML.

## Latest changes and why

The [initial review](./agent-builder-review-2026-09-06.md) found small mismatches
between authoritative local evidence, generated prose and custom workflow
checks. Commit `e6e9521` addresses them without adding distributed files,
dependencies, model calls or larger tool/token limits. The canonical payload
change was 35 net lines / 2,890 bytes.

| Area | Implemented behavior |
| --- | --- |
| Release Evidence Reviewer | Both Ready and Blocked tool results include security approval and rollback owner, so prose sees the same facts as the UI. Mobile evidence has enough space for readable values. |
| Docs Q&A | The source count uses the correct singular/plural wording. |
| Operations Data Analyst | Explicit per-request latency percentile questions take the existing unsupported path before model execution. Instructions distinguish them from percentiles of daily averages. A data favicon removes the missing-icon request. |
| Ticket Triage | Tool output separates the unchanged bundled ticket/source from scenario assumptions and authoritative recommendation evidence. Prose guidance uses everyday intake terms. |
| Custom workflow | Noninteractive runs stop at the proposal without explicit approval of its exact scope; tool permission flags do not approve a build. Generated rules must preserve source qualifications. The existing four behavior checks include a known near-miss and relevant source review. |
| Custom helpers | Deadline cancellation is latched so the next request cannot slip through timer rounding. Meaningful short smoke facts such as P2/Ada are permitted alongside a longer fact or source marker. |

Earlier cleanup moved maintainer checks into `scripts/tests/`, removed obsolete
CI/development material from the clean branch, hardened content/provenance and
metadata-only telemetry, and simplified instruction surfaces. The same-turn
launch fix reads the newly detached process's output, discovers its assigned
loopback URL and finishes health checks without a passive waiting handoff.

## Architecture to preserve

- `skills/` owns both builder implementations; `scripts/build_copilot.py`
  generates the classic bundle under `copilot/skills/`. The copies are packaging,
  not separate .NET implementations or independently maintained runtimes.
- Template builds copy one of four complete standalone .NET 10 apps unchanged:
  Release Evidence Reviewer, Docs Q&A, Operations Data Analyst, Ticket Triage.
- Custom builds use their own fixed starter, a short Purpose-led proposal,
  explicit Build/Revise choices and bounded editable surfaces. Complex requests
  can select an implementation plan in chat or propose a reduced mock PoC;
  choosing a PoC still requires Build approval before writing files.
- Local business fixtures/tools are distinct from the configured live model.
  Deterministic calculations, routing and evidence remain authoritative.
- Preserve the differentiated UIs, bounded execution/history and metadata-only
  tracing. No shared runtime package or frontend build system was added.
- Builders need neither MCP credentials nor platform readback. Generated apps
  retain their Azure and separate Progress Integration configuration.

## Evidence from the implementation session

These were executed before this archival merge on the source now committed as
`e6e9521`; they are not fresh live runs of the merged development checkout.

- All four fresh copies matched source, built without warnings/errors and
  passed 12/12 live Azure smoke cases; process-owned health/root checks passed.
  See [template results](./evidence/2026-09-08/template-results.json).
- 15 Python tests and 883 runtime/helper/UI assertions passed: 555 template
  runtime, 67 UI, 89 custom helper and 172 custom runtime. See
  [offline results](./evidence/2026-09-08/offline-results.json).
- Copilot/MCP-reference checks, ten .NET whitespace checks, skill frontmatter
  and `git diff --check` passed. Browser/API follow-ups verified the affected
  evidence, provenance, percentile and mobile-copy behavior.
- A fresh headless custom prompt returned proposal/choices and left its target
  empty. Earlier interactive supplied-file and Kubernetes mock/revise/build
  runs verified the launch-output fix; the SAP/bank plan-only run wrote no files.
- An existing generated Kubernetes fixture was retested on the updated starter:
  three smokes passed. Try-and-refine ran four initial checks and four after
  one permitted instruction repair, finishing within the original five-minute
  deadline. The repair affected only that fixture. Final sampled checks passed;
  one answer still omitted the exact record-file citation while citing the rule.
- The guide passed 88 browser checks across all five views at 1440, 1024 and
  390px widths, with no runtime errors or document overflow. Further checks
  exercised line selection, saved progress, concept dialogs and dark mode.
  See [browser results](./evidence/2026-09-08/walkthrough-browser-checks.json)
  and [source coverage](./evidence/2026-09-08/walkthrough-coverage.json).

Full session outputs remain in local artifact directories
`agent-builder-review-2026-09-06` and `agent-builder-fixes-2026-09-08` under
`/Users/dipeykov/.codex/artifacts`. This branch retains compact results instead
of copying generated test apps, caches, process logs and transient server URLs.

## Checks for this development update

- Conflict-free merge; product files match `e6e9521`. Additions relative to the
  clean implementation are confined to `planning/`.
- All 218 embedded source files match the development checkout; saved HTML
  bytes match the original artifact.
- Copilot generation check, MCP-reference synchronization and all 15 Python
  tests pass in the development checkout.
- Local links in the new planning index/handoff and Git whitespace checks pass.

No new app behavior was introduced by this merge, so live model tests and the
previous browser suite were not repeated for an unchanged HTML/source copy.

## Remaining limits and follow-up

- The percentile guard covers explicit request/latency wording; broader
  paraphrases still rely on the model's limitation route. Sampled answer checks
  do not establish a throughput benchmark or guarantee every future answer.
- Backend trace ingestion remains independently unverified.
- Upstream `main` integration, current PR/CI status, Jira descriptions and
  external product UI status were not rechecked as part of this archival update.
  Reinspect those live before claiming release readiness.
- Keep future product fixes on the clean branch; update this development branch
  and dated guide deliberately. Do not pull the planning directory into the
  distributed PR when moving a code fix.
