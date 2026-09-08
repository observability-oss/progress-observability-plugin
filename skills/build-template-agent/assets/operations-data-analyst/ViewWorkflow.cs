using System.Text.RegularExpressions;

namespace OperationsDataAnalyst;

public sealed class ViewWorkflow(AgentRuntime runtime, MetricsStore metrics)
{
    public ViewRules Rules { get; } = new(metrics);

    public Task<AnalysisReply> AskAsync(AnalysisRequest request, CancellationToken cancellationToken = default,
        Func<ViewData, Task>? onView = null, Func<string, Task>? onText = null)
    {
        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > 2_000) throw new ArgumentException("question_requires_1_to_2000_characters");
        if (request.LastQuestion?.Length > 2_000) throw new ArgumentException("last_question_too_long");
        var current = Rules.Validate(request.View ?? Rules.Default);
        var selected = request.ApprovedView is null ? current : Rules.Validate(request.ApprovedView);
        // Explicit preservation is a fixed view command, just like a clicked selection.
        var preserve = request.ApprovedView is not null || Regex.IsMatch(question,
            @"\bkeep\s+(?:(?:the|this|that|my|current|selected)\s+)*view\s+unchanged\b|\bwithout\s+changing\s+(?:(?:the|this|that|my|current|selected)\s+)*view\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return runtime.RunAsync(request with { Question = question, ApprovedView = preserve ? selected : null },
            selected, Rules, cancellationToken, onView, onText);
    }
}

public sealed class ViewRules(MetricsStore metrics)
{
    public ViewSpec Default => new("all", metrics.Start.ToString("yyyy-MM-dd"), metrics.End.ToString("yyyy-MM-dd"));
    public ViewSpec Merge(ViewSpec current, ViewPatch? changes)
    {
        if (changes is null) throw new ArgumentException("intent_changes_required");
        if (changes.Grouping is not (null or "day" or "service" or "period")) throw new ArgumentException("unsupported_grouping");
        var service = changes.Service ?? current.Service;
        var start = changes.Start ?? current.Start;
        var end = changes.End ?? current.End;
        var grouping = changes.Grouping ?? current.Grouping;
        var comparison = current.Comparison is { } existing ? existing with { Service = service } : null;
        if (changes.Comparison is { } windows)
        {
            comparison = new(service, windows.BeforeStart, windows.BeforeEnd, windows.AfterStart, windows.AfterEnd);
            grouping = "period";
            if (string.CompareOrdinal(windows.BeforeStart, start) < 0) start = windows.BeforeStart;
            if (string.CompareOrdinal(windows.AfterEnd, end) > 0) end = windows.AfterEnd;
        }
        else if (grouping != "period") comparison = null;
        return Validate(new(service, start, end, changes.Metric ?? current.Metric, grouping, comparison));
    }
    public ViewSpec Validate(ViewSpec? view)
    {
        if (view is null) throw new ArgumentException("view_required");
        var selection = metrics.NormalizeSelection(view.Service, view.Start, view.End);
        if (selection.Service != "all" && !metrics.Services.Contains(selection.Service)) throw new ArgumentException("unknown_view_service");
        if (view.Metric is not ("requests" or "errors" or "errorRatePercent" or "averageResponseMs")) throw new ArgumentException("unsupported_metric");
        if (view.Grouping is not ("day" or "service" or "period")) throw new ArgumentException("unsupported_grouping");
        var comparison = view.Comparison;
        if (view.Grouping == "period")
        {
            if (comparison is null) throw new ArgumentException("comparison_required");
            var before = metrics.NormalizeSelection(comparison.Service, comparison.BeforeStart, comparison.BeforeEnd);
            var after = metrics.NormalizeSelection(comparison.Service, comparison.AfterStart, comparison.AfterEnd);
            if (before.Service != selection.Service || string.CompareOrdinal(before.End, after.Start) >= 0 ||
                string.CompareOrdinal(before.Start, selection.Start) < 0 || string.CompareOrdinal(after.End, selection.End) > 0)
                throw new ArgumentException("comparison_outside_view");
            comparison = new(before.Service, before.Start, before.End, after.Start, after.End);
        }
        else if (comparison is not null) throw new ArgumentException("comparison_requires_period_grouping");
        return view with { Service = selection.Service, Start = selection.Start, End = selection.End, Comparison = comparison };
    }

    public ViewData Data(ViewSpec requested)
    {
        var view = Validate(requested);
        var dashboard = metrics.Dashboard(view.Service, view.Start, view.End);
        var points = view.Grouping switch
        {
            "day" => dashboard.Daily.Select(row => new ChartPoint(row.Date.ToString("yyyy-MM-dd"), Value(row.Summary, view.Metric))).ToArray(),
            "service" => dashboard.Rows.GroupBy(row => row.Service).Select(group => new ChartPoint(group.Key, Value(MetricsStore.Aggregate(group), view.Metric))).ToArray(),
            _ => ComparisonPoints(view),
        };
        var chart = new ChartData(Label(view.Metric) + (view.Grouping == "day" ? " over time" : view.Grouping == "service" ? " by service" : " by period"),
            view.Metric == "errorRatePercent" ? "%" : view.Metric == "averageResponseMs" ? "ms" : "", view.Grouping == "day" ? "line" : "bar", points);
        return new(view, dashboard, chart, Highlights(view, dashboard, chart));
    }

    /// <summary>
    /// The four tiles beside the chart, derived from the series the question produced, so they
    /// describe the selected metric instead of a fixed set. Every value is an aggregate that was
    /// actually computed: percentiles take the nearest rank rather than interpolating a value no
    /// day recorded, and a difference between percentages is reported in percentage points.
    /// </summary>
    public static IReadOnlyList<Highlight> Highlights(ViewSpec view, DashboardResult dashboard, ChartData chart)
    {
        var unit = chart.Unit;
        var difference = unit == "%" ? "pp" : unit;
        var plotted = chart.Points.Count + (view.Grouping == "day" ? " day" : " service") + (chart.Points.Count == 1 ? "" : "s");
        var total = Value(dashboard.Summary, view.Metric);
        var overall = new Highlight("overall", Label(view.Metric), total, unit,
            (view.Metric is "requests" or "errors" ? "Total across " : "Request-weighted across ") +
            (view.Grouping == "period" ? "the full selection" : plotted), null, total is not null);
        if (view.Grouping == "period")
        {
            var before = chart.Points.ElementAtOrDefault(0);
            var after = chart.Points.ElementAtOrDefault(1);
            var moved = after?.Value - before?.Value;
            return [
                Period("before", "Before period", before), Period("after", "After period", after),
                new("change", "Change", moved, difference, "After minus before", null, moved is not null),
                overall];
        }
        var (peak, lowest) = Extremes(chart);
        if (view.Grouping == "service")
            return [overall,
                new("peak", "Highest service", peak?.Value, unit, Names(peak), peak?.Labels[0], peak is not null),
                new("lowest", "Lowest service", lowest?.Value, unit, Names(lowest), lowest?.Labels[0], lowest is not null),
                new("spread", "Spread", peak is null ? null : peak.Value - lowest!.Value, difference, "Highest minus lowest", null, false)];
        var values = chart.Points.Where(point => point.Value is not null).Select(point => point.Value!.Value).Order().ToArray();
        var sample = values.Length + " daily value" + (values.Length == 1 ? "" : "s");
        return [overall,
            new("peak", "Peak day", peak?.Value, unit, Names(peak), peak?.Labels[0], peak is not null),
            new("p95", "95th percentile day", Percentile(values, 0.95m), unit, "Nearest rank of " + sample, null, false),
            new("median", "Median day", Percentile(values, 0.5m), unit, "Middle of " + sample, null, false)];

        Highlight Period(string key, string label, ChartPoint? point) =>
            new(key, label, point?.Value, unit, point?.Label ?? "No period selected",
                point?.Value is null ? null : point.Label, point?.Value is not null);
        static string Names(MetricExtreme? extreme) => extreme is null ? "No plotted values"
            : extreme.Labels.Count == 1 ? extreme.Labels[0]
            : extreme.Labels[0] + " and " + (extreme.Labels.Count - 1) + " more";
        static decimal? Percentile(decimal[] sorted, decimal fraction) => sorted.Length == 0 ? null
            : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private ChartPoint[] ComparisonPoints(ViewSpec view)
    {
        var periods = view.Comparison!;
        var result = metrics.Compare(periods.Service, periods.BeforeStart, periods.BeforeEnd, periods.AfterStart, periods.AfterEnd);
        return [new($"{periods.BeforeStart} → {periods.BeforeEnd}", Value(result.Before, view.Metric)),
            new($"{periods.AfterStart} → {periods.AfterEnd}", Value(result.After, view.Metric))];
    }
    /// <summary>
    /// Peak and low of the exact plotted series, so an extreme can never disagree
    /// with the chart. Undefined points are skipped, never treated as zero.
    /// </summary>
    public static (MetricExtreme? Peak, MetricExtreme? Lowest) Extremes(ChartData chart)
    {
        var known = chart.Points.Where(point => point.Value is not null).ToArray();
        if (known.Length == 0) return (null, null);
        var highest = known.Max(point => point.Value!.Value);
        var lowest = known.Min(point => point.Value!.Value);
        return (Extreme(highest), Extreme(lowest));
        MetricExtreme Extreme(decimal value) =>
            new(value, known.Where(point => point.Value == value).Select(point => point.Label).ToArray());
    }

    public static decimal? Value(MetricSummary? summary, string metric) => metric switch
    {
        "requests" => summary?.Requests,
        "errors" => summary?.Errors,
        "errorRatePercent" => summary?.ErrorRatePercent,
        "averageResponseMs" => summary?.AverageResponseMs,
        _ => throw new ArgumentException("unsupported_metric"),
    };
    public static string Label(string metric) => metric switch
    {
        "requests" => "Requests",
        "errors" => "Errors",
        "errorRatePercent" => "Error rate",
        "averageResponseMs" => "Average response time",
        _ => "Unknown metric",
    };
    public static string Describe(ViewSpec view) => $"{view.Service} · {view.Start} to {view.End} · {Label(view.Metric)} · {view.Grouping}" +
        (view.Comparison is { } p ? $" · {p.BeforeStart}–{p.BeforeEnd} vs {p.AfterStart}–{p.AfterEnd}" : "");
}
