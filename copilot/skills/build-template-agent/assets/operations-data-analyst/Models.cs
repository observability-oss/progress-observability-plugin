using System.Text.Json.Serialization;

namespace OperationsDataAnalyst;

public sealed record MetricRow(DateOnly Date, string Service, long Requests, long Errors, long TotalResponseMs);
public sealed record MetricSummary(int RowCount, long Requests, long Errors, decimal? ErrorRatePercent, decimal? AverageResponseMs);
public sealed record ServiceMetric(string Service, MetricSummary Summary);
public sealed record SummaryResult(string Status, string? Reason, MetricSummary? Summary,
    IReadOnlyList<ServiceMetric>? ByService = null, IReadOnlyList<DailyMetric>? Daily = null,
    long? MaxDailyRequests = null, IReadOnlyList<DateOnly>? BusiestDays = null);
public sealed record ComparisonResult(string Status, string? Reason, MetricSummary? Before, MetricSummary? After,
    decimal? ErrorRateChangePercentagePoints, decimal? AverageResponseChangeMs);
public sealed record Outlier(DateOnly Date, string Service, string Metric, decimal Value, decimal Baseline);
public sealed record OutlierResult(string Status, string? Reason, IReadOnlyList<Outlier> Outliers);
public sealed record DailyMetric(DateOnly Date, MetricSummary Summary);
public sealed record DashboardResult(string Status, string? Reason, IReadOnlyList<string> Services,
    DateOnly DatasetStart, DateOnly DatasetEnd, MetricSummary? Summary, IReadOnlyList<DailyMetric> Daily,
    IReadOnlyList<MetricRow> Rows, IReadOnlyList<Outlier> Outliers);
public sealed record ViewSpec(string Service, string Start, string End, string Metric = "errorRatePercent", string Grouping = "day", PeriodComparison? Comparison = null);
public sealed record AnalysisRequest(string? Question, ViewSpec? View = null, string? LastQuestion = null, ViewSpec? ApprovedView = null);
public sealed record ComparisonWindows(string BeforeStart, string BeforeEnd, string AfterStart, string AfterEnd);
public sealed record ViewPatch(string? Service, string? Start, string? End, string? Metric, string? Grouping, ComparisonWindows? Comparison);
public sealed record ChartPoint(string Label, decimal? Value);
public sealed record MetricExtreme(decimal Value, IReadOnlyList<string> Labels);
public sealed record ChartData(string Title, string Unit, string Kind, IReadOnlyList<ChartPoint> Points);
/// <param name="Focus">Chart point this tile describes, when it names exactly one plotted point.</param>
/// <param name="Explorable">True when the agent tool already returns this exact number, so a click gets a grounded answer.</param>
public sealed record Highlight(string Key, string Label, decimal? Value, string Unit, string Caption, string? Focus, bool Explorable);
public sealed record ViewData(ViewSpec View, DashboardResult Dashboard, ChartData Chart, IReadOnlyList<Highlight> Highlights);
public sealed record AnalysisReply(string Status, string Answer, string TraceId, ViewSpec View,
    DashboardResult? Dashboard, ChartData? Chart, IReadOnlyList<Highlight>? Highlights,
    IReadOnlyList<ToolEvidence> Evidence, string? LastQuestion);
public sealed record MetricSelection(string Service, string Start, string End);
public sealed record PeriodComparison(string Service, string BeforeStart, string BeforeEnd, string AfterStart, string AfterEnd);
public sealed record ToolEvidence(string Tool, object Arguments, object Result, object? ModelResult = null);
public sealed record ExplorationResult(ViewSpec View, ChartData Chart, SummaryResult Summary, ComparisonResult? Comparison, OutlierResult? Outliers);
public sealed record ExplorationArguments(ViewSpec View, bool IncludeOutliers, bool IncludeAllMetrics = false);
public sealed record AdditionalMetricData(SummaryResult Summary, ComparisonResult? Comparison);
public sealed record FocusedExplorationResult(ViewSpec View, ChartData Chart, decimal? SelectedTotal, decimal? ComparisonChange,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DateOnly>? BusiestDays,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? MaxDailyRequests,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MetricExtreme? Peak,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MetricExtreme? Lowest,
    OutlierResult? Outliers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AdditionalMetricData? AdditionalMetrics);
public sealed record LimitationResult(string Status, string Message);
