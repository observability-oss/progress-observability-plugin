using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OperationsDataAnalyst;

internal static class ViewWorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(MetricsStore metrics)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); count++; }
        void Invalid(Action action, string label)
        { try { action(); } catch (ArgumentException) { count++; return; } throw new InvalidOperationException("FAIL: " + label); }
        var rules = new ViewRules(metrics);
        var initial = rules.Default;
        var focused = new ViewSpec("checkout", "2026-08-20", "2026-08-30", "averageResponseMs");
        var comparison = new ViewSpec("checkout", "2026-08-01", "2026-08-30", "errors", "period",
            new("checkout", "2026-08-23", "2026-08-23", "2026-08-24", "2026-08-24"));
        foreach (var metric in new[] { "requests", "errors", "errorRatePercent", "averageResponseMs" })
        {
            var daily = rules.Data(initial with { Metric = metric });
            var services = rules.Data(initial with { Metric = metric, Grouping = "service" });
            Check(daily.Chart.Kind == "line" && daily.Chart.Points.Count == 30 && daily.Chart.Points[0].Value == ViewRules.Value(daily.Dashboard.Daily[0].Summary, metric), "daily chart exact values: " + metric);
            Check(services.Chart.Kind == "bar" && services.Chart.Points.Count == 3 && services.Chart.Points.Single(point => point.Label == "checkout").Value ==
                ViewRules.Value(metrics.Summarize("checkout", null, null).Summary, metric), "service chart weighted values: " + metric);
        }
        var bars = rules.Data(comparison);
        Check(bars.Chart.Points.Select(point => point.Value).SequenceEqual(new decimal?[] { 20m, 500m }), "period chart exact before and after errors");
        Check(bars.Dashboard.Rows.Count == 30 && bars.Dashboard.Summary!.Errors == 1080, "period view rows and KPIs retain explicitly labeled overall scope");
        var emptyData = rules.Data(initial with { Start = "2026-09-01", End = "2026-09-02" });
        Check(emptyData.Dashboard.Status == "empty" && emptyData.Chart.Points.Count == 0, "empty view has no chart points");
        var zero = new MetricsStore(["date,service,requests,errors,total_response_ms", "2026-08-01,auth,0,0,0"]);
        Check(new ViewRules(zero).Data(new("auth", "2026-08-01", "2026-08-01", "averageResponseMs", "service")).Chart.Points.Single().Value is null, "undefined chart value stays null");
        var latencyChart = rules.Data(initial with { Service = "checkout", Metric = "averageResponseMs" }).Chart;
        var checkoutExtremes = ViewRules.Extremes(latencyChart);
        Check(checkoutExtremes is ({ Value: 900m, Labels.Count: 1 }, { Value: 200m, Labels.Count: 29 })
            && checkoutExtremes.Peak!.Labels.Single() == "2026-08-24", "extremes follow the plotted series and keep tied labels");
        var serviceExtremes = ViewRules.Extremes(rules.Data(initial with { Metric = "averageResponseMs", Grouping = "service" }).Chart);
        Check(serviceExtremes.Peak!.Labels.Single() == "checkout" && serviceExtremes.Lowest!.Labels.Single() == "search",
            "extremes describe services when the plotted series is grouped by service");
        Check(ViewRules.Extremes(emptyData.Chart) is (null, null), "empty chart invents no extreme");
        Check(ViewRules.Extremes(new ViewRules(zero).Data(new("auth", "2026-08-01", "2026-08-01", "averageResponseMs")).Chart) is (null, null),
            "an entirely undefined series has no peak or low");
        var mixed = new MetricsStore(["date,service,requests,errors,total_response_ms", "2026-08-01,auth,0,0,0", "2026-08-02,auth,100,0,5000"]);
        var mixedExtremes = ViewRules.Extremes(new ViewRules(mixed).Data(new("auth", "2026-08-01", "2026-08-02", "averageResponseMs")).Chart);
        Check(mixedExtremes is ({ Value: 50m }, { Value: 50m }) && mixedExtremes.Lowest!.Labels.Single() == "2026-08-02",
            "undefined days are skipped rather than dragging the low to zero");
        var latencyTiles = rules.Data(focused).Highlights;
        Check(latencyTiles.Select(item => item.Key).SequenceEqual(new[] { "overall", "peak", "p95", "median" }) &&
            latencyTiles.All(item => item.Unit == "ms"), "a daily latency question produces distribution tiles for that metric alone");
        Check(latencyTiles[0] is { Label: "Average response time", Focus: null, Explorable: true, Caption: "Request-weighted across 11 days" } &&
            latencyTiles[0].Value == ViewRules.Value(metrics.Summarize("checkout", focused.Start, focused.End).Summary, "averageResponseMs"),
            "the overall tile repeats the request-weighted total the agent tool already returns");
        Check(latencyTiles[1] is { Value: 900m, Caption: "2026-08-24", Focus: "2026-08-24", Explorable: true },
            "the peak tile names the exact plotted day, so a click drills into that day");
        Check(latencyTiles[2] is { Value: 900m, Focus: null, Explorable: false, Caption: "Nearest rank of 11 daily values" } &&
            latencyTiles[3] is { Value: 200m, Focus: null, Explorable: false, Caption: "Middle of 11 daily values" },
            "percentile tiles report observed daily values and are never sent back to the agent as a question");
        var monthTiles = rules.Data(initial with { Service = "checkout", Metric = "averageResponseMs" }).Highlights;
        Check(monthTiles[1].Value == 900m && monthTiles[2].Value == 200m,
            "a wider selection separates the 95th percentile day from the single peak day");
        var serviceTiles = rules.Data(initial with { Metric = "averageResponseMs", Grouping = "service" }).Highlights;
        Check(serviceTiles.Select(item => item.Key).SequenceEqual(new[] { "overall", "peak", "lowest", "spread" }) &&
            serviceTiles[1] is { Focus: "checkout", Explorable: true } && serviceTiles[2] is { Focus: "search", Explorable: true } &&
            serviceTiles[3] is { Focus: null, Explorable: false } && serviceTiles[3].Value == serviceTiles[1].Value - serviceTiles[2].Value,
            "a service ranking produces highest, lowest and spread tiles for the plotted services");
        var periodTiles = rules.Data(comparison).Highlights;
        Check(periodTiles.Select(item => item.Key).SequenceEqual(new[] { "before", "after", "change", "overall" }) &&
            periodTiles[0] is { Value: 20m, Focus: "2026-08-23 → 2026-08-23" } && periodTiles[1].Value == 500m &&
            periodTiles[2] is { Value: 480m, Unit: "", Focus: null } &&
            periodTiles[3] is { Value: 1080m, Caption: "Total across the full selection" },
            "a period comparison reports both windows, their change and the labelled overall scope");
        var rateTiles = rules.Data(comparison with { Metric = "errorRatePercent" }).Highlights;
        Check(rateTiles[0].Unit == "%" && rateTiles[2] is { Unit: "pp" } &&
            rateTiles[2].Value == metrics.Compare("checkout", "2026-08-23", "2026-08-23", "2026-08-24", "2026-08-24").ErrorRateChangePercentagePoints,
            "a difference between percentages is reported in percentage points, matching the computed comparison");
        Check(emptyData.Highlights.All(item => item is { Value: null, Focus: null, Explorable: false }) &&
            emptyData.Highlights[1].Caption == "No plotted values",
            "an empty selection invents no tile values and offers nothing to explore");
        var partialTiles = new ViewRules(mixed).Data(new("auth", "2026-08-01", "2026-08-02", "averageResponseMs")).Highlights;
        Check(partialTiles[2].Value == 50m && partialTiles[3].Value == 50m && partialTiles[2].Caption == "Nearest rank of 1 daily value",
            "undefined days are excluded from the percentile sample rather than counted as zero");
        foreach (var bad in new[] { initial with { Metric = "sql" }, initial with { Grouping = "pie" }, initial with { Service = "missing" },
            initial with { Start = "2026-08-31" }, initial with { Grouping = "period" }, initial with { Comparison = comparison.Comparison },
            comparison with { Comparison = comparison.Comparison! with { BeforeEnd = "2026-08-24" } },
            comparison with { Comparison = comparison.Comparison! with { Service = "auth" } }, comparison with { End = "2026-08-23" } })
            Invalid(() => rules.Validate(bad), "invalid typed view refused");
        var windows = new ComparisonWindows("2026-08-17", "2026-08-23", "2026-08-24", "2026-08-30");
        var expanded = rules.Merge(focused, new(null, null, null, null, null, windows));
        Check(expanded == new ViewSpec("checkout", "2026-08-17", "2026-08-30", "averageResponseMs", "period",
            new("checkout", "2026-08-17", "2026-08-23", "2026-08-24", "2026-08-30")), "comparison preserves metric and service while expanding dates only to the required windows");
        Check(rules.Merge(focused, new(null, null, null, null, null, null)) == focused, "omitted changes preserve current view");
        Invalid(() => rules.Merge(focused, new(null, null, null, null, "pie", windows)), "comparison cannot hide an unsupported grouping");
        Invalid(() => rules.Merge(focused, new(null, null, null, null, null, windows with { BeforeEnd = "2026-08-24" })), "overlapping windows fail validation");

        using var client = new AnalystClient();
        var workflow = new ViewWorkflow(new(client, metrics, "view-test"), metrics);
        client.Plan = _ => new() { ["metric"] = "errors", ["grouping"] = "service" };
        var ranking = await workflow.AskAsync(new("Which service contributes the most errors?", initial));
        Check(ranking.View.Metric == "errors" && ranking.View.Grouping == "service" && ranking.Chart!.Kind == "bar" && ranking.Chart.Points.Count == 3,
            "ordinary ranking question changes relevant metric and chart without an Apply gate");
        Check(ranking.Evidence.Single().Result is ExplorationResult rankingEvidence && ReferenceEquals(rankingEvidence.Chart, ranking.Chart),
            "agent synthesis receives the exact selected chart rendered by the UI");
        Check(ranking.Chart!.Points.Single(point => point.Label == "checkout").Value == 1080m,
            "question-driven service bars retain deterministic totals");
        Check(ranking.Evidence.Single().ModelResult is FocusedExplorationResult
        { SelectedTotal: 1530m, BusiestDays: null, MaxDailyRequests: null, AdditionalMetrics: null },
            "errors question supplies only selected metric evidence to the model");
        client.Plan = _ => new() { ["metric"] = "requests", ["grouping"] = "day" };
        var busiest = await workflow.AskAsync(new("Which day had the most requests?", initial));
        var result = (ExplorationResult)busiest.Evidence.Single().Result;
        Check(result.Summary.MaxDailyRequests == 9920 && result.Summary.BusiestDays!.Single() == new DateOnly(2026, 8, 24) &&
            busiest.Chart!.Points.Single(point => point.Label == "2026-08-24").Value == 9920,
            "busiest-day view and computed ranking agree exactly");
        Check(busiest.Evidence.Single().ModelResult is FocusedExplorationResult { MaxDailyRequests: 9920, BusiestDays.Count: 1 },
            "request chart includes computed request rankings in model payload");
        client.Plan = _ => new() { ["service"] = "checkout", ["metric"] = "averageResponseMs", ["grouping"] = "day" };
        var latency = await workflow.AskAsync(new("What happened to checkout latency?", initial));
        Check(latency.View == initial with { Service = "checkout", Metric = "averageResponseMs" } &&
            latency.Chart!.Unit == "ms" && latency.Chart.Points.Single(point => point.Label == "2026-08-24").Value == 900m,
            "ordinary latency question focuses checkout and latency with unchanged dates");
        Check(latency.Evidence.Single().ModelResult is FocusedExplorationResult
        { Peak: { Value: 900m, Labels.Count: 1 }, Lowest: { Value: 200m, Labels.Count: 29 }, MaxDailyRequests: null, BusiestDays: null },
            "latency question supplies an authoritative peak and low instead of daily request rankings");
        client.Plan = _ => new() { ["service"] = "auth", ["metric"] = "averageResponseMs", ["grouping"] = "day" };
        var authLatency = await workflow.AskAsync(new("What is the maximum latency for auth?", initial));
        Check(authLatency.Evidence.Single().ModelResult is FocusedExplorationResult { Peak.Value: 130m, Lowest.Value: 120m } authPeak
            && authPeak.Peak!.Labels.Single() == "2026-08-01" && authPeak.Lowest!.Labels.Count == 29,
            "a maximum latency question resolves to the exact peak day in the shipped CSV");
        client.Plan = _ => new() { ["comparison"] = windows, ["includeOutliers"] = true };
        var periods = await workflow.AskAsync(new("Compare August 17–23 with August 24–30", focused, "Show checkout latency"));
        Check(periods.View == expanded && periods.Chart!.Unit == "ms" && periods.Chart.Kind == "bar", "natural follow-up retains service and metric with two period bars");
        var comparisonResult = (ExplorationResult)periods.Evidence.Single().Result;
        Check(periods.Chart!.Points.Select(point => point.Value).SequenceEqual(new[] { comparisonResult.Comparison!.Before!.AverageResponseMs, comparisonResult.Comparison.After!.AverageResponseMs }) &&
            comparisonResult.Outliers!.Outliers.Any(item => item.Date == new DateOnly(2026, 8, 24)), "one model tool supplies exact comparison values and anomaly evidence together");
        Check(client.Contexts[^1].GetProperty("lastQuestion").GetString() == "Show checkout latency", "last question supplies bounded follow-up context");
        client.Plan = _ => new() { ["metric"] = "errors", ["grouping"] = "day" };
        var fromPeriods = await workflow.AskAsync(new("Now show daily errors", expanded));
        Check(fromPeriods.View.Service == "checkout" && fromPeriods.View.Start == "2026-08-17" && fromPeriods.View.Comparison is null && fromPeriods.Chart!.Kind == "line",
            "daily refinement exits comparison without resetting date or service selection");

        client.Plan = _ => new() { ["service"] = "all", ["metric"] = "revenue", ["start"] = "invalid" };
        var clicked = await workflow.AskAsync(new("Explain this metric", initial, ApprovedView: focused));
        Check(clicked.View == focused && clicked.Dashboard!.Rows.Count == 11 && clicked.Chart!.Unit == "ms",
            "explicit click selection stays exact even when model proposes a different patch");
        Check(client.Contexts[^1].GetProperty("fixedView").GetBoolean(), "agent knows clicked view is fixed");

        client.Plan = _ => new() { ["metric"] = "errors", ["grouping"] = "service" };
        const string negatedPreservation = "Show errors by service; do not keep the view unchanged";
        var changed = await workflow.AskAsync(new(negatedPreservation, initial));
        Check(changed.View == initial with { Metric = "errors", Grouping = "service" }
            && !client.Contexts[^1].GetProperty("fixedView").GetBoolean()
            && client.Contexts[^1].GetProperty("question").GetString() == negatedPreservation,
            "negated preservation reaches the model unchanged and permits the requested chart change");
        var fixedOverride = await workflow.AskAsync(new(negatedPreservation, initial, ApprovedView: focused));
        Check(fixedOverride.View == focused && client.Contexts[^1].GetProperty("fixedView").GetBoolean(),
            "an explicit typed view override stays fixed regardless of free-form wording");

        client.Plan = _ => new();
        foreach (var question in new[] { "Explain requests. Keep the current view unchanged.", "Explain without changing the selected view." })
        {
            var preserved = await workflow.AskAsync(new(question, focused));
            Check(preserved.View == focused && !client.Contexts[^1].GetProperty("fixedView").GetBoolean()
                && client.Contexts[^1].GetProperty("question").GetString() == question,
                "natural-language preservation is passed to the model without becoming a fixed UI override: " + question);
        }
        client.Plan = _ => new() { ["metric"] = "errors" };
        var datesOnly = await workflow.AskAsync(new("Show errors; keep the current dates unchanged.", focused));
        Check(datesOnly.View.Metric == "errors" && datesOnly.View.Start == focused.Start && datesOnly.View.End == focused.End &&
            !client.Contexts[^1].GetProperty("fixedView").GetBoolean(), "dates-only preservation still allows a requested metric change");
        client.Plan = _ => new() { ["includeAllMetrics"] = true };
        var overview = await workflow.AskAsync(new("Explain requests, errors, error rate and response time. Keep the current view unchanged.", focused));
        Check(overview.Evidence.Single().ModelResult is FocusedExplorationResult { AdditionalMetrics.Summary.Summary: { Errors: > 0, AverageResponseMs: > 0 } },
            "explicit multi-metric exploration includes all requested calculated metrics in model result");

        foreach (var mode in new[] { "unsupported", "clarify" })
        {
            using var limited = new AnalystClient { Mode = mode };
            var viewEvents = 0;
            var response = await new ViewWorkflow(new(limited, metrics, "test"), metrics).AskAsync(new("Question", focused),
                onView: _ => { viewEvents++; return Task.CompletedTask; });
            Check(response.Status == mode && response.View == focused && response.Dashboard is null && response.Chart is null && viewEvents == 0,
                "limitation never publishes invented chart data: " + mode);
            Check(response.Evidence is [{ Tool: "ExplainLimitation" }] && limited.Requests == 2, "limitation remains an honest bounded agent tool call: " + mode);
        }
        using var partialClient = new AnalystClient { Answer = "Invented change is 100%." };
        var partialView = comparison with { End = "2026-09-02", Comparison = comparison.Comparison! with { AfterStart = "2026-09-01", AfterEnd = "2026-09-02" } };
        var chunks = new List<string>();
        var partial = await new ViewWorkflow(new(partialClient, metrics, "test"), metrics).AskAsync(new("Compare these periods", partialView),
            onText: text => { chunks.Add(text); return Task.CompletedTask; });
        Check(partial.Chart!.Points[0].Value == 20 && partial.Chart.Points[1].Value is null &&
            partial.Answer.StartsWith("At least one comparison period", StringComparison.Ordinal) && chunks.Single() == partial.Answer,
            "partial empty comparison preserves actual chart values and suppresses fabricated streamed differences");
        Invalid(() => workflow.AskAsync(new("", initial)).GetAwaiter().GetResult(), "empty question rejected");
        Invalid(() => workflow.AskAsync(new("Inspect", initial, new string('x', 2001))).GetAwaiter().GetResult(), "last question length bounded");
        Invalid(() => workflow.AskAsync(new("Inspect", initial, ApprovedView: initial with { Metric = "sql" })).GetAwaiter().GetResult(), "clicked view revalidated before agent request");
        return count;
    }
}
