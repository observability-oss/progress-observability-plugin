using System.ComponentModel;

namespace OperationsDataAnalyst;

public sealed class AssistantTools(MetricsStore metrics, ViewRules rules, ViewSpec current, bool fixedView,
    CancellationTokenSource budget, Func<ViewData, Task>? onView = null)
{
    private int _calls;
    public ViewData? Data { get; private set; }
    public ToolEvidence? Evidence { get; private set; }
    public LimitationResult? Limitation { get; private set; }

    [Description("Explore the bundled synthetic metrics and update the visible dashboard to answer the question. Choose the relevant metric and chart: service rankings use service bars; busiest days use requests by day; latency uses averageResponseMs. Null filters inherit the current view. Use service all only for a requested comparison across services. For period comparisons provide four ordered, nonoverlapping inclusive dates. Returns the exact selected chart and request-weighted summary, service and daily totals, and computed busiest days. Use chart points as the authoritative values for the selected metric, and the returned peak and lowest for its highest and lowest points, including ties. Set includeOutliers for anomaly questions. Set includeAllMetrics only for an overview or a question explicitly asking about multiple metrics. Otherwise the model receives only values for the selected metric. Call once; all requested calculations are returned together.")]
    public async Task<FocusedExplorationResult> ExploreMetrics(string? service = null, string? start = null, string? end = null,
        string? metric = null, string? grouping = null, ComparisonWindows? comparison = null, bool includeOutliers = false, bool includeAllMetrics = false)
    {
        BeginCall();
        var view = fixedView ? current : rules.Merge(current, new(service, start, end, metric, grouping, comparison));
        var summary = metrics.Summarize(view.Service, view.Start, view.End);
        var periods = view.Comparison is { } windows
            ? metrics.Compare(view.Service, windows.BeforeStart, windows.BeforeEnd, windows.AfterStart, windows.AfterEnd) : null;
        var outliers = includeOutliers ? metrics.FindOutliers(view.Service, view.Start, view.End) : null;
        Data = rules.Data(view);
        var result = new ExplorationResult(view, Data.Chart, summary, periods, outliers);
        // Daily request peaks stay available only where they coincide with the plotted
        // series, so they can never contradict the generalized peak below.
        var dailyRequestPeak = view is { Metric: "requests", Grouping: "day" };
        var (peak, lowest) = ViewRules.Extremes(Data.Chart);
        var focused = new FocusedExplorationResult(view, Data.Chart, ViewRules.Value(summary.Summary, view.Metric),
            periods is null ? null : ViewRules.Value(periods.After, view.Metric) - ViewRules.Value(periods.Before, view.Metric),
            dailyRequestPeak ? summary.BusiestDays : null,
            dailyRequestPeak ? summary.MaxDailyRequests : null, peak, lowest, outliers,
            includeAllMetrics ? new(summary, periods) : null);
        Evidence = new(nameof(ExploreMetrics), new ExplorationArguments(view, includeOutliers, includeAllMetrics), result, focused);
        budget.Token.ThrowIfCancellationRequested();
        if (onView is not null) await onView(Data);
        return focused;
    }

    [Description("Use only when the request needs clarification or asks for unavailable data/actions. status must be clarify or unsupported. No dashboard change or metric calculation occurs. Do not use for ordinary questions about known metrics; explore those directly.")]
    public LimitationResult ExplainLimitation(string status)
    {
        BeginCall();
        if (status is not ("clarify" or "unsupported")) throw new ArgumentException("unsupported_limitation_status");
        Limitation = new(status, status == "clarify"
            ? "Which service, dates or metric would you like to explore?"
            : "The bundled synthetic CSV contains daily request/error totals and average response times, not individual request latencies or live data. I can show daily trends, service comparisons or two date periods.");
        Evidence = new(nameof(ExplainLimitation), new { status }, Limitation, Limitation);
        return Limitation;
    }

    private void BeginCall()
    {
        budget.Token.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _calls) > 1)
        {
            budget.Cancel();
            throw new InvalidOperationException("one_exploration_per_question");
        }
    }
}
