# Operations Data Analyst simplification — 2026-09-03

This supersedes the Analyst v7 interaction snapshot in
`prebuilt-template-ux-validation.md`. Other templates were not changed by this
follow-up. No commit was made.

## Behavior

- A question chooses the relevant view through the real MAF agent. One exploration
  tool returns computed data, then one model request synthesizes the answer.
- The chart, KPIs, filters and rows arrive before the answer. Metric, filter and
  chart clicks show their selection immediately and ask the agent about that
  exact view. Daily points, service bars and comparison bars support drill-down.
- Explicit whole-view preservation is enforced in code. Reset clears context;
  Stop cancels the request, and stale responses cannot overwrite the next view.
- The answer sits beside the chart. Filters, source rows, automatic checks and
  tool evidence are collapsed initially. Evidence distinguishes complete local
  calculations from the exact focused data supplied to the model.

## Verification

- Build: 0 warnings, 0 errors.
- Focused runtime suite: 193 assertions passed. Includes weighted arithmetic,
  scope/period validation, real MAF invocation, provider request serialization,
  fixed views, selected-metric payloads, concurrency, cancellation, failure after
  data publication, empty data and copied-output layout.
- Actual Azure adapter protocol: two requests, one provider tool call allowed,
  system instructions present on both requests, no tools on synthesis. A simple
  error-count question supplies no unrelated error-rate/latency values to the model.
- Live browser: errors by service; checkout latency Aug 20–30; weekly comparison;
  comparison-bar and service-bar drill-down; metric click; Stop and Reset.
  Narrow layout checked without horizontal overflow. Final service-bar click
  starts a real analysis while narrowing the visible data to checkout.
- Canonical and Copilot payloads: 13 identical Analyst files; running
  HTML matches canonical. Generator check, JavaScript syntax and diff checks pass.
- Final local preview: `http://127.0.0.1:51989/`; its process reported this URL and
  its health endpoint confirmed 90 loaded rows.
- Payload digest (relative paths plus file contents, sorted): `8d6ed39ffac52af3e0fd63b0719e019205655f589c9837fbffaed83a82c7959d`.

For the same “Which service contributes most errors in this view?” request,
the old complete-response path took 5.984 seconds and retained the daily error-rate
chart. Final streaming data arrived at 1.954 seconds, with the answer
complete at 3.759 seconds. These are individual local measurements,
not a latency guarantee. Final chart: auth 300, checkout 1080, search 150 errors.

Final answer: Checkout contributes the most errors in this view with 1,080 errors, followed by auth with 300 and search with 150. Total errors across all services in the selected date range are 1,530.

## Live smoke evidence

The three cases use the same fixed-view contract as a UI selection. Exact numeric
and view predicates were retained; free question/view changes have separate
protocol and browser coverage.

```json
{
  "status": "pass",
  "cases": [
    {
      "caseId": "known-aggregate",
      "status": "pass",
      "traceId": "40d874865715ea497942bca1c9b28293",
      "reason": "tool_numeric_invariants_observed",
      "tools": [
        "ExploreMetrics"
      ]
    },
    {
      "caseId": "comparison-spike",
      "status": "pass",
      "traceId": "08e5fed7eae69f90380b1705ba72aafd",
      "reason": "tool_numeric_invariants_observed",
      "tools": [
        "ExploreMetrics"
      ]
    },
    {
      "caseId": "empty-selection",
      "status": "pass",
      "traceId": "ec54a6c480bd92885efff57dbc2df794",
      "reason": "tool_numeric_invariants_observed",
      "tools": [
        "ExploreMetrics"
      ]
    }
  ]
}
```

An earlier live invocation failed all three cases because the model changed the
chart metric despite the requested fixed selection; the actual known-case
calculations were correct. Failed trace IDs were
`e7c9864dbe4eef93a1c36d5f147b345d`, `a9b378d21d6390e821856a143b73aad5`,
and `0145142f0ca175ca3bfd43cf6c5e9638`. Those failures prompted explicit fixed-view
enforcement and are not counted as passing evidence. An intermediate answer
also introduced an unsupported rate comparison; the final model payload now
contains only the selected metric unless additional metrics are requested.

Trace IDs prove local execution. Progress backend ingestion was not verified.

## Follow-up — answer-first layout and question-driven tiles (2026-09-03)

Supersedes the "answer sits beside the chart" and "metric click" points above.

- The analyst answer moved out of the right-hand column into a full-width panel
  directly under the question box, with larger body type, the echoed question and
  the current scope. The chart is now full width below the tiles.
- The four fixed KPIs (requests, errors, error rate, average response time) are
  replaced by `ViewRules.Highlights`, computed server-side from the same plotted
  series as the chart and returned on `ViewData`/`AnalysisReply`. Day views give
  overall, peak day, 95th percentile day and median day; service views give
  overall, highest, lowest and spread; period views give before, after, change
  and the overall scope. Percentiles use nearest rank, so every tile reports an
  observed aggregate; a difference between percentages is labelled `pp`.
- Tiles no longer switch metric — a Metric select joined service and dates in the
  filters form. A tile is a button only when the agent tool already returns that
  exact number (overall, peak, lowest, before, after, change); the derived
  percentile, median and spread tiles are a read-out, so no click can ask the
  agent about a figure it was never given. Tiles naming one plotted point drill
  through the same code path as a chart click.

Verification: build 0 warnings/0 errors; focused runtime suite 232 assertions
passed (10 new highlight assertions covering day, service, period, percentage-point
differences, empty selections and undefined days). Live browser at
`http://127.0.0.1:5099/` with `/api/view` only: day/service/period tile sets each
render their own labels and captions; peak-day tile click narrowed the view to
2026-08-24 and issued the same question a chart-point click produces; 1320px and
400px layouts checked without horizontal overflow. Copilot payload regenerated;
canonical and mirrored Analyst files identical.

Known limitation, unchanged by this work: the CSV holds daily aggregates, so a
"95p" question cannot be answered at request level. The tile is labelled
"95th percentile day" with its sample in the caption; the model's own wording is
not constrained to that distinction.
