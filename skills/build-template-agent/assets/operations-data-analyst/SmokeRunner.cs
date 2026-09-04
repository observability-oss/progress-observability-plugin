using System.Text.Json;

namespace OperationsDataAnalyst;

public sealed class SmokeRunner(ViewWorkflow workflow)
{
    public async Task<int> RunAsync()
    {
        SmokeCase[] cases =
        [
            new("known-aggregate", "For the current Checkout selection, August 1–3, explain the total requests, total errors, error rate and average response time. Keep the current view unchanged.",
                new("checkout", "2026-08-01", "2026-08-03"),
                reply => AnswerContains(reply, "error rate", "0.9") &&
                    reply.Evidence.Any(item => item.Tool == "ExploreMetrics" && item.Result is ExplorationResult
                    { Summary: { Status: "ok", Summary: { RowCount: 3, Requests: 6060, Errors: 60, AverageResponseMs: 200m, ErrorRatePercent: 0.990099m } } })),
            new("comparison-spike", "Keep the current view unchanged. Explain the already-selected comparison of checkout on August 23 and August 24: request counts, error counts, error rates, average response times and their changes. Also identify daily error-rate or response-time outliers over the full selected date range. Describe what the synthetic data supports without inventing a cause.",
                new("checkout", "2026-08-01", "2026-08-30", "errorRatePercent", "period", new("checkout", "2026-08-23", "2026-08-23", "2026-08-24", "2026-08-24")),
                reply => AnswerContains(reply, "error rate", "10") &&
                    reply.Evidence.Any(item => item.Tool == "ExploreMetrics" && item.Result is ExplorationResult
                    { Comparison: { Status: "ok", Before: { Requests: 2440, Errors: 20, AverageResponseMs: 200m },
                        After: { Requests: 5000, Errors: 500, ErrorRatePercent: 10m, AverageResponseMs: 900m }, AverageResponseChangeMs: 700m } }) &&
                    reply.Evidence.Any(item => item.Tool == "ExploreMetrics" && item.Result is ExplorationResult { Outliers: { } result } &&
                        result.Outliers.Any(row => row.Service == "checkout" && row.Date == new DateOnly(2026, 8, 24) && row.Metric == "error_rate_percent" && row.Value == 10m))),
            new("empty-selection", "What do the current Checkout metrics show for September 1–2? Keep the current dates unchanged and say if no matching data exists; do not invent numbers.",
                new("checkout", "2026-09-01", "2026-09-02"),
                reply => AnswerContains(reply, "no matching data") &&
                    reply.Evidence.Any(item => item.Tool == "ExploreMetrics" && item.Result is ExplorationResult
                    { Summary: { Status: "empty", Reason: "no_matching_rows", Summary: null } }) &&
                    !reply.Evidence.Any(item => item.Result is ExplorationResult { Summary.Status: "ok" })),
        ];
        var results = new List<SmokeResult>();
        foreach (var scenario in cases)
        {
            try
            {
                var reply = await workflow.AskAsync(new(scenario.Prompt, scenario.View, ApprovedView: scenario.View));
                var passed = reply.Status == "answered" && reply.View == scenario.View && reply.Chart is not null &&
                    reply.TraceId.Length == 32 && reply.TraceId != new string('0', 32) && scenario.Passes(reply);
                results.Add(new(scenario.Id, passed ? "pass" : "fail", reply.TraceId,
                    passed ? "tool_numeric_invariants_observed" : reply.Status != "answered" ? "agent_" + reply.Status :
                        reply.View != scenario.View ? "view_selection_changed" : "required_tool_evidence_missing", reply.Evidence.Select(item => item.Tool).ToArray()));
            }
            catch (AgentRunException error)
            {
                results.Add(new(scenario.Id, "fail", error.TraceId, "agent_run_failed", []));
            }
        }
        var status = results.All(result => result.Status == "pass") ? "pass" : "fail";
        Console.WriteLine("SMOKE_REPORT=" + JsonSerializer.Serialize(new { status, cases = results },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return status == "pass" ? 0 : 1;
    }

    private static bool AnswerContains(AnalysisReply reply, params string[] expected)
    {
        var normalized = reply.Answer.Replace(",", "", StringComparison.Ordinal).ToLowerInvariant();
        return expected.All(value => normalized.Contains(value, StringComparison.Ordinal));
    }

    private sealed record SmokeCase(string Id, string Prompt, ViewSpec View, Func<AnalysisReply, bool> Passes);
    private sealed record SmokeResult(string CaseId, string Status, string TraceId, string Reason, string[] Tools);
}
