using OperationsDataAnalyst;

const string header = "date,service,requests,errors,total_response_ms";
var fixture = MetricsStore.Load(Path.Combine(AppContext.BaseDirectory, "fixture.csv"));
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks++;
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new InvalidOperationException("FAIL: " + name);
}
MetricsStore Csv(params string[] rows) => new(new[] { header }.Concat(rows));

Check(fixture.RowCount == 90, "fixture has 90 rows");
Check(fixture.SourcePath is { } source && Path.IsPathRooted(source) && File.Exists(source) && Path.GetFileName(source) == "fixture.csv",
    "loaded store reports the absolute CSV every answer is computed from");
Check(fixture.Fingerprint is { Length: 16 } && fixture.Fingerprint == MetricsStore.Load(fixture.SourcePath!).Fingerprint,
    "fingerprint of the analysed CSV is reported and stable");
var edited = Path.Combine(Path.GetTempPath(), "operations-fixture-edited.csv");
File.WriteAllLines(edited, File.ReadLines(fixture.SourcePath!).Select(line => line.Replace(",130000", ",120000")));
Check(MetricsStore.Load(edited).Fingerprint != fixture.Fingerprint, "fingerprint distinguishes an edited CSV from the shipped one");
File.Delete(edited);
Throws<InvalidDataException>(() => MetricsStore.Load(Path.Combine(AppContext.BaseDirectory, "absent.csv")), "missing CSV is a data error, not a crash");
Check(new MetricsStore([header, "2026-08-01,auth,1,0,0"]) is { SourcePath: null, Fingerprint: null }, "in-memory stores claim no file provenance");
Check(fixture.Services.SequenceEqual(new[] { "auth", "checkout", "search" }), "three canonical services");
Check(fixture.Start == new DateOnly(2026, 8, 1) && fixture.End == new DateOnly(2026, 8, 30), "fixture date bounds");
var total = fixture.Summarize(null, null, null);
Check(total.Status == "ok" && total.Summary is { Requests: 208640, Errors: 1530, RowCount: 90 }, "exact fixture totals");
Check(total.Summary!.AverageResponseMs == Math.Round(30124000m / 208640m, 6), "exact total response duration");
Check(total.MaxDailyRequests == 9920 && total.BusiestDays!.SequenceEqual(new[] { new DateOnly(2026, 8, 24) }), "busiest day is computed across the full selected scope, not the final CSV row");
var narrowedPeak = fixture.Summarize("all", "2026-08-25", "2026-08-30");
Check(narrowedPeak.MaxDailyRequests == 7740 && narrowedPeak.BusiestDays!.Single() == new DateOnly(2026, 8, 30), "peak ranking respects explicitly narrowed dates");
var known = fixture.Summarize("checkout", "2026-08-01", "2026-08-03");
Check(known.Summary is { Requests: 6060, Errors: 60, RowCount: 3, AverageResponseMs: 200m, ErrorRatePercent: 0.990099m }, "known smoke aggregate");
Check(known.MaxDailyRequests == 2040 && known.BusiestDays!.Single() == new DateOnly(2026, 8, 3), "peak ranking respects selected service");
Check(fixture.Summarize("missing", null, null) is { MaxDailyRequests: null, BusiestDays: null } &&
    fixture.Summarize("all", "bad", "bad") is { MaxDailyRequests: null, BusiestDays: null }, "empty or invalid scope does not invent a peak");
Check(fixture.Summarize(" CHECKOUT ", "2026-08-01", "2026-08-03").Summary == known.Summary, "service normalization");
Check(fixture.Summarize("auth", "2026-08-01", "2026-08-01").Summary is { RowCount: 1, Requests: 1000 }, "inclusive boundaries");
Check(fixture.Summarize("missing", null, null) is { Status: "empty", Summary: null }, "unknown service is empty");
Check(fixture.Summarize("checkout", "2026-09-01", "2026-09-02") is { Status: "empty", Reason: "no_matching_rows", Summary: null }, "empty smoke selection");
Check(fixture.Summarize("auth; DROP", null, null).Status == "invalid", "freeform service rejected");
Check(fixture.Summarize(new string('a', 33), null, null).Status == "invalid", "service length bounded");
Check(fixture.Summarize("all", "2026-8-01", "2026-08-02").Status == "invalid", "strict date format");
Check(fixture.Summarize("all", "2026-02-30", "2026-03-02").Status == "invalid", "impossible date rejected");
Check(fixture.Summarize("all", "2026-08-02", "2026-08-01").Status == "invalid", "reversed range rejected");
Check(fixture.Summarize("all", "2026-01-01", "2027-01-02").Status == "invalid", "oversized date range rejected");
Check(fixture.Summarize("all", "2026-01-01", "2027-01-01").Status == "ok", "366 day inclusive range allowed");

var weighted = Csv("2026-08-01,auth,100,50,10000", "2026-08-02,auth,900,9,900000");
Check(weighted.Summarize(null, null, null).Summary is { Requests: 1000, Errors: 59, ErrorRatePercent: 5.9m, AverageResponseMs: 910m }, "weighted arithmetic, not mean of means");
var zero = Csv("2026-08-01,auth,0,0,0", "2026-08-02,auth,0,0,0", "2026-08-03,auth,0,0,0");
Check(zero.Summarize(null, null, null).Summary is { Requests: 0, ErrorRatePercent: null, AverageResponseMs: null }, "zero denominators remain undefined");
Check(zero.Summarize(null, null, null) is { MaxDailyRequests: 0, BusiestDays.Count: 3 }, "peak ranking retains all tied dates, including zero-volume days");
Check(zero.FindOutliers(null, null, null).Outliers.Count == 0, "zero denominators are not outliers");
Check(MetricsStore.Aggregate([]) is { RowCount: 0, Requests: 0, ErrorRatePercent: null, AverageResponseMs: null }, "empty arithmetic does not divide by zero");

var compare = fixture.Compare("checkout", "2026-08-23", "2026-08-23", "2026-08-24", "2026-08-24");
Check(compare is { Status: "ok", Before: { Requests: 2440, Errors: 20, AverageResponseMs: 200m }, After: { Requests: 5000, Errors: 500, ErrorRatePercent: 10m, AverageResponseMs: 900m }, AverageResponseChangeMs: 700m }, "comparison spike fixture");
Check(compare.ErrorRateChangePercentagePoints == 9.180328m, "comparison uses percentage points");
Check(fixture.Compare("all", null, null, null, null).Status == "invalid", "compare requires explicit dates");
Check(fixture.Compare("all", "2026-08-01", "2026-08-02", "2026-08-02", "2026-08-03").Status == "invalid", "overlapping periods rejected");
Check(fixture.Compare("all", "2026-08-03", "2026-08-04", "2026-08-01", "2026-08-02").Status == "invalid", "reversed periods rejected");
Check(fixture.Compare("all", "2026-08-01", "2026-08-02", "2026-09-01", "2026-09-02") is { Status: "empty", After: null, ErrorRateChangePercentagePoints: null }, "empty comparison does not invent data");
Check(fixture.Compare("all", "2026-07-01", "2026-07-02", "bad", "bad") is { Status: "invalid", Reason: "date_format_must_be_yyyy_mm_dd" }, "invalid comparison reason preserved");
Check(zero.Compare("auth", "2026-08-01", "2026-08-01", "2026-08-02", "2026-08-02") is { Status: "ok", ErrorRateChangePercentagePoints: null, AverageResponseChangeMs: null }, "null comparison arithmetic");

var outliers = fixture.FindOutliers("all", null, null);
Check(outliers.Status == "ok" && outliers.Outliers.Count == 2, "exactly two spike signals in fixture");
Check(outliers.Outliers.All(row => row.Service == "checkout" && row.Date == new DateOnly(2026, 8, 24)), "outliers are isolated per service");
Check(outliers.Outliers.Any(row => row.Metric == "average_response_ms" && row.Value == 900m && row.Baseline == 200m), "latency median invariant");
Check(outliers.Outliers.Any(row => row.Metric == "error_rate_percent" && row.Value == 10m), "error outlier invariant");
Check(fixture.FindOutliers("all", "2026-08-01", "2026-08-20").Outliers.Count == 0, "normal period is not anomalous");
Check(fixture.FindOutliers("checkout", "2026-08-23", "2026-08-24").Outliers.Count == 0, "two samples insufficient for heuristic");
Check(fixture.FindOutliers("missing", null, null).Status == "empty", "empty outlier selection");
Check(fixture.FindOutliers("all", "bad", "bad").Status == "invalid", "invalid outlier selection");
var absoluteThreshold = Csv("2026-08-01,auth,10000,1,1000000", "2026-08-02,auth,10000,1,1000000", "2026-08-03,auth,10000,3,1100000");
Check(absoluteThreshold.FindOutliers(null, null, null).Outliers.Count == 0, "2x alone insufficient without absolute increase");
var evenMedian = Csv("2026-08-01,auth,100,1,10000", "2026-08-02,auth,100,1,20000", "2026-08-03,auth,100,1,90000");
Check(evenMedian.FindOutliers(null, null, null).Outliers.Single().Baseline == 150m, "even leave-one-out median averages middle two values");

var dashboard = fixture.Dashboard("all", null, null);
Check(dashboard.Daily.Count == 30 && dashboard.Rows.Count == 90, "dashboard arrays preserve full selected evidence");
Check(dashboard.Daily[0].Summary is { Requests: 6000, Errors: 35, ErrorRatePercent: 0.583333m }, "chart daily weighted aggregation");
Check(fixture.Dashboard("all", "bad", "bad") is { Status: "invalid", Summary: null, Daily.Count: 0, Rows.Count: 0, Outliers.Count: 0 }, "invalid dashboard has no stale data");
Check(fixture.Dashboard("missing", null, null) is { Status: "empty", Summary: null, Daily.Count: 0, Rows.Count: 0 }, "empty dashboard has no stale data");

Throws<InvalidDataException>(() => new MetricsStore([]), "empty file");
Throws<InvalidDataException>(() => new MetricsStore(["date,service,requests,errors,average_response_ms"]), "incorrect header");
Throws<InvalidDataException>(() => new MetricsStore([header]), "no data rows");
foreach (var row in new[]
{
    "2026-08-01,auth,-1,0,0", "2026-08-01,auth,10,11,100", "2026-08-01,auth,0,0,1",
    "2026-08-01,auth,1,0,-1", "2026-08-01,auth,1,0,1000000000001", "2026-08-01,auth,1000001,0,0",
    "2026-08-01,auth,999999999999999999999,0,0", "2026-08-01,AUTH,1,0,0", "2026-08-01,all,1,0,0",
    "2026-08-32,auth,1,0,0", "2026-08-01,auth,1,0", "2026-08-01,auth,1,0,0,extra",
    "2026-08-01,auth,\"100\",0,0", "2026-08-01,auth,1.2,0,0", "",
}) Throws<InvalidDataException>(() => Csv(row), "malformed CSV row: " + row);
Throws<InvalidDataException>(() => Csv("2026-08-01,auth,1,0,0", "2026-08-01,auth,2,0,0"), "duplicate service/date row");
Throws<InvalidDataException>(() => Csv("2026-08-01,auth,1,0,0", "2027-08-02,auth,2,0,0"), "fixture date span bounded");
Throws<InvalidDataException>(() => Csv(new string('x', 161)), "row size bounded");
Throws<InvalidDataException>(() => Csv(Enumerable.Range(0, 11).Select(index => $"2026-08-01,service-{(char)('a' + index)},1,0,0").ToArray()), "service count bounded");
var unsorted = Csv("2026-08-02,search,1,0,0", "2026-08-01,auth,1,0,0");
Check(unsorted.Start == new DateOnly(2026, 8, 1) && unsorted.Dashboard(null, null, null).Rows[0].Service == "auth", "CSV order normalized");
Check(new MetricsStore(new[] { "\uFEFF" + header, "2026-08-01,auth,1,0,0" }).RowCount == 1, "UTF8 BOM accepted");

checks += await RuntimeTests.RunAsync(fixture);
checks += await ViewWorkflowTests.RunAsync(fixture);
checks += await IntentProtocolTests.RunAsync(fixture);
checks += await OutputLayoutTests.RunAsync();
checks += await TelemetryTests.RunAsync(fixture);
Console.WriteLine($"OPERATIONS_RUNTIME_TESTS=pass; assertions={checks}");
