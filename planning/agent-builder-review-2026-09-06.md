> Historical checkpoint. See the [8 September handoff](./agent-builder-handoff-2026-09-08.md) for the current implementation and validation status. Original local evidence links and server URLs below refer to that earlier session.

**Agent builder review — 6 September 2026**

Reviewed `agent-builder-clear-pr` at `d34f734` against the handoff and current canonical sources. This was a test/review: distributed source was not edited. Test projects, transcripts, answers and screenshots are in this artifact directory.

The architecture is appropriate for these examples: four fixed standalone apps, a separate bounded custom starter, deterministic local calculations/routing, and metadata-only telemetry. The main remaining issues are the connection between authoritative evidence and model prose, plus custom workflow/test reliability. A larger runtime or orchestration refactor is unnecessary.

**Fresh verification**

- All four templates copied byte-for-byte, built with zero warnings/errors, and passed all 12 live Azure model smoke cases. Each case's emitted trace ID is in [template results](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/templates-results.json).
- 15 Python tests, 541 template runtime assertions, 67 offline UI assertions, and 83 custom helper assertions passed. Custom runtime tests failed once on `deadline prevents remaining requests`, then passed all 169 assertions on an unchanged retry. This is an unresolved intermittent failure, not a fully clean first test run.
- Copilot generation/reference synchronization, whitespace checks for 10 projects, the CI starter copy/validate/build sequence, and `git diff --check` passed.
- All six test apps returned `status=ready` and HTTP 200 root pages at their own process-reported ports. Backend ingestion was not independently verified; local trace IDs do not prove ingestion.
- Sixteen additional template API requests all completed with HTTP 200. Their answers were reviewed separately; passing transport did not hide the semantic defects below. Observed elapsed times were 1.84–3.88 seconds, a small local sample rather than a performance benchmark.
- Browser click-through covered all four templates and both generated agents. Screenshots were inspected at desktop and 390px mobile widths; no horizontal overflow or blocking layout issue was found. The only console error observed was an Operations favicon 404.

| Template | Tested behavior | Finding and small improvement |
|---|---|---|
| Release Evidence Reviewer | Blocked Atlas, Ready Orion, current-project follow-up, unnamed fresh request, New review | The blocked tool response omits already satisfied fields. Atlas's UI correctly shows security approval as Approved, while the answer says it cannot confirm it. Return those field values with the existing blocked result. |
| Docs Q&A | Two-source retention question, follow-up, source preview, section scope, unsupported topic | Answers and source scope were correct in this sample. Keep local retrieval and current UI. Minor copy: `1 sources read` should be singular. |
| Operations Data Analyst | Auth maximum, checkout follow-up, service ranking and chart drill-down, empty dates, percentile questions | One answer called 900ms average latency “per-request p95.” An identical repeat correctly declined that equivalence: the defect is intermittent. Explicitly distinguish unsupported per-request percentiles from supported percentiles of daily averages, using the existing limitation route. |
| Ticket Triage | Missing intake, temporary completion, original-record preservation, inline questions, selected-ticket boundary | Routing and scenario state were correct, but both API and browser explanations called supplied scenario fields “bundled facts.” Make the effective ticket's source unambiguous in the existing tool result; retain the clear scenario banner and inline answer placement. |

**Prioritized code findings**

1. **Operations: unsupported statistics can become an apparently valid answer.** Prompt: `What is the per-request p95 latency for checkout on August 24?` The response said the 900ms average was “used here as the per-request p95 latency.” The CSV contains totals and averages, not request distributions. The existing [instructions](/Users/dipeykov/repos/progress-observability-plugin-build-template-agent/skills/build-template-agent/assets/operations-data-analyst/AgentRuntime.cs:58) map latency to `averageResponseMs` and leave unsupported-statistic detection to the model. Add an explicit capability distinction and a narrow fixed rejection for unsupported per-request percentile requests; preserve daily-average percentile questions. Do not introduce another classification call. [Initial answers](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/operations-data-analyst-live.json), [repeat checks](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/operations-percentile-recheck.json).

2. **Release: UI and model receive different amounts of evidence.** `What is still missing for Project Atlas to be ready?` and `Which requirements are already satisfied?` both produced uncertainty about security approval even though it is documented. The [blocked return](/Users/dipeykov/repos/progress-observability-plugin-build-template-agent/skills/build-template-agent/assets/release-evidence-reviewer/Tools.cs:74) includes only missing fields, while the UI's typed evidence contains both values. Include security approval and rollback owner in that same tool response. This needs no additional tool or model call. [Answers](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/release-evidence-reviewer-live.json).

3. **Ticket: ambiguous provenance in the effective ticket payload.** With T-1005 plus production/multiple-customers/no-workaround scenario values, the model repeatedly described all values as bundled. [GetTicket](/Users/dipeykov/repos/progress-observability-plugin-build-template-agent/skills/build-template-agent/assets/ticket-triage/Tools.cs:19) returns the effective ticket with the original `tickets.json` source, alongside the separate original ticket/scenario/evidence. Clarify that existing payload's effective-versus-bundled labels/source instead of adding more calls or a second judge. The structured recommendation and original record were correct. Also prefer readable field names in prose; leave diagnostic IDs in the evidence panel. [Answers](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/ticket-triage-live.json), [browser evidence](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/ticket-desktop.png).

**Custom path**

Tested with Copilot CLI 1.0.82, `gpt-5.6-terra`, medium effort, using this checkout as the local plugin. The generated apps used existing app configuration; credential values were not inspected.

- **Supplied support policy and on-call CSV:** generated the correct fixed starter with two `supplied` files, no invented policy files, workflow preset, three passing smokes, and 4/4 builder behavior checks. Four separate reviewer checks also handled P1 outside office hours, exact follow-up facts, fresh-chat isolation, and unsupported refund actions correctly. Browser follow-up/reset worked. This run also verified the same-turn launch-output fix through `read_bash`, health, final validation and final handoff.
- **Interactive Kubernetes automation request:** selected simplified mock PoC, selected Revise, changed only the name, and then selected Build. No files existed before that final selection. The resulting Local Kubernetes Adviser passed validation, build, three smokes and 4/4 builder behavior checks; it included an integration plan and explicit mock/no-live-effect boundaries. The same-turn launch fix worked here too.
- **SAP/bank/Slack plan only, mocks rejected:** returned a developer plan and left the target directory empty. No business-system tools or implementation commands were executed.

Two custom improvements matter:

- **Keep noninteractive approval behavior explicit.** A fresh `copilot -p` supplied-file run skipped proposal/Build approval and described the build as approved without receiving a selection. The interactive sequence worked correctly. Document interactive mode as the supported guided flow, or harden the no-question-tool fallback and test it separately; do not treat `--allow-all` as accepting a scope proposal. [Headless transcript](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/logs/custom-supplied-proposal.log), [interactive transcript](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/custom-kubernetes-transcript.json).
- **Test a real negative decision, not only an unknown record or forbidden action.** After the Kubernetes builder reported 4/4, an independent INC-205 question correctly declined restart but incorrectly claimed that general restart eligibility criteria were absent. The generated runbook explicitly contains those criteria. A later browser repeat correctly found the criteria, so this is intermittent. A hypothetical one-replica variant correctly declined restart. Use the existing four-check budget to exercise a known near-miss record and verify its explanation against the applicable rule. Prefer scoped source reading/current tool evidence over raising iteration or token limits. Also preserve the scope of source qualifications: the support generation widened the missing-workaround condition beyond the supplied policy's partial-impact section. [Independent mock answers](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/custom-kubernetes-independent.json), [generated rule](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/custom-complex/kubernetes-advisor/docs/sample-knowledge.md:6).

**Small cleanup recommendations**

- Resolve the intermittent [deadline assertion](/Users/dipeykov/repos/progress-observability-plugin-build-template-agent/scripts/tests/custom-agent/runtime/BehaviorCheckerTests.cs:102). The helper resumes its loop after deadline-driven cancellation and rechecks wall-clock time; timer rounding is a plausible cause, not yet proven. Latching deadline exhaustion is a small candidate fix. Preserve the shared deadline and no-retry behavior rather than adding sleeps or weakening the assertion.
- Reconsider the blanket [four-character smoke-marker minimum](/Users/dipeykov/repos/progress-observability-plugin-build-template-agent/skills/build-custom-agent/scripts/validate-project.cs:426). It rejected real test facts `P2` and `Ada`, causing two correction rounds and ultimately dropping Ada from the smoke assertion. Permit meaningful short facts when paired with source/other evidence markers, or document that constraint up front. Do not solve this with answer-hinting prompts.
- The combined canonical skill payload is approximately 451 KiB excluding build outputs. Keep the standalone apps, differentiated UIs, deterministic domain logic, strict schemas, bounded history and metadata-only wrappers. Shared runtime packages, a frontend build system, another evaluator call, or higher model limits would add complexity without addressing these findings.
- Optional cosmetic cleanup: singular source-count wording; a data favicon for Operations; human-readable prose labels in Ticket; give Release evidence values more room on mobile (the 390px screenshot splits Approved across lines). Existing state-reset, source-scoping, chart and scenario interactions should remain intact.

**Running review copies and evidence**

| App | Local UI |
|---|---|
| Release | [Open Release](http://127.0.0.1:50515) |
| Docs | [Open Docs](http://127.0.0.1:50542) |
| Operations | [Open Operations](http://127.0.0.1:50548) |
| Ticket | [Open Ticket](http://127.0.0.1:50579) |
| Supplied custom | [Open Support Policy Adviser](http://127.0.0.1:50757) |
| Mock custom | [Open Local Kubernetes Adviser](http://127.0.0.1:50963) |

[Offline results](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/offline-results.json), [successful custom runtime retry](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/logs/custom-runtime-retry.log), [format results](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/format-results.json), [final health](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/final-health.json), [unchanged copy comparison](/Users/dipeykov/.codex/artifacts/agent-builder-review-2026-09-06/template-copy-comparison.json).
