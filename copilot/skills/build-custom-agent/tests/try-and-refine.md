# Developer checks for the optional review

No extra test code or fixtures go into customer projects. The package-free
transport tests run with the existing runtime suite:

```bash
dotnet run --project skills/build-custom-agent/tests/runtime/RuntimeTests.csproj
dotnet run --file skills/build-custom-agent/tests/helper-tests.cs
```

They check exact history, isolated fresh chat, expectation isolation, HTTP
failures, skipped dependent calls, timeouts/deadlines, and frozen smoke markers.
They do not prove Copilot's judgment or repair decisions.

## Real CLI regression cases

Use fresh disposable directories and interactive Copilot with the local plugin,
GPT-5.6 Terra / medium. Supply ordinary Purpose prompts and use native choices;
do not coach the building session. Keep developer observations separate from
the prompt. Exercise Jira triage → Build, Kubernetes automation → Mock PoC →
Build, access-request advice with `Try-and-refine: off`, and SAP/bank automation
→ Show implementation plan. After the final answer, exit the CLI and verify the
reported app's own URL still responds. Plan-only must create no files.

For failure branches, reuse one isolated, already-built local planning example:
policy rounds points/capacity up to whole working days. Before its smoke gates,
seed integer division in its local tool; exact-division smokes can pass while
a held-out 11-point / 4-per-day request exposes the defect. Keep the expected
three days private to the review, never in the app request. Ask the follow-up
about the issue ID/quantities supplied by the user, then repeat with empty history.
Continue at the optional phase only after independently verifying required gates.

| Case | Observe actual behavior, not just the final claim |
|---|---|
| Healthy app | Four checks, exact history, honest fresh chat, no edits. |
| Seeded calculation defect | One general repair; unchanged checks/smokes; rebuilt app answers correctly. |
| Repair fails validation/build | Inject one test-only gate failure; restore only review edits, preserve unrelated content, reverify restored app, report incomplete. |
| Repair remains ineffective | In the disposable test, simulate an unchanged runtime result after repair; restore and report incomplete, no second repair. |
| Timeout/deadline | No retry, invented history, speculative repair, or pass for untested checks. |
| Off / plan only | No helper calls or review edits; plan-only remains zero-write. |

Fault injection belongs only to the developer's disposable test process, never
to the plugin helper or starter. Record prompts, selections, before/after file
bytes, frozen checks, actual answers, smoke IDs, timings and process survival.
Re-running a full build and continuing an already-built review are different
tests; label them separately. Passing samples are not a quality guarantee.
