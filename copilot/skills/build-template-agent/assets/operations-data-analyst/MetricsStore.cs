using System.Globalization;
using System.Security.Cryptography;

namespace OperationsDataAnalyst;

public sealed class MetricsStore
{
    private readonly MetricRow[] _rows;
    public IReadOnlyList<string> Services { get; }
    /// <summary>Absolute path of the CSV every answer is computed from.</summary>
    public string? SourcePath { get; private init; }
    /// <summary>Content fingerprint of that CSV, so a stale copy is visible at a glance.</summary>
    public string? Fingerprint { get; private init; }
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public int RowCount => _rows.Length;

    public MetricsStore(IEnumerable<string> lines)
    {
        using var input = lines.GetEnumerator();
        if (!input.MoveNext() || input.Current.TrimStart('\uFEFF') != "date,service,requests,errors,total_response_ms")
            throw new InvalidDataException("csv_header_invalid");
        var rows = new List<MetricRow>();
        var keys = new HashSet<(DateOnly, string)>();
        while (input.MoveNext())
        {
            if (rows.Count >= 2_000 || input.Current.Length > 160)
                throw new InvalidDataException("csv_limits_exceeded");
            var fields = input.Current.Split(',');
            if (fields.Length != 5 || !TryDate(fields[0], out var date) || !ValidService(fields[1]) || fields[1] == "all" ||
                !TryNumber(fields[2], 1_000_000, out var requests) ||
                !TryNumber(fields[3], requests, out var errors) ||
                !TryNumber(fields[4], 1_000_000_000_000, out var responseMs) ||
                (requests == 0 && responseMs != 0) || !keys.Add((date, fields[1])))
                throw new InvalidDataException("csv_row_invalid");
            rows.Add(new(date, fields[1], requests, errors, responseMs));
        }
        if (rows.Count == 0) throw new InvalidDataException("csv_empty");
        _rows = rows.OrderBy(row => row.Date).ThenBy(row => row.Service, StringComparer.Ordinal).ToArray();
        Services = _rows.Select(row => row.Service).Distinct().Order(StringComparer.Ordinal).ToArray();
        Start = _rows[0].Date;
        End = _rows[^1].Date;
        if (Services.Count > 10 || End.DayNumber - Start.DayNumber > 365)
            throw new InvalidDataException("csv_limits_exceeded");
    }

    public static MetricsStore Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new InvalidDataException("csv_missing");
        if (file.Length > 1_000_000) throw new InvalidDataException("csv_limits_exceeded");
        return new MetricsStore(File.ReadLines(file.FullName))
        {
            SourcePath = file.FullName,
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.FullName)))[..16],
        };
    }

    public SummaryResult Summarize(string? service, string? start, string? end)
    {
        var selection = Select(service, start, end);
        if (selection.Reason is not null) return new("invalid", selection.Reason, null);
        if (selection.Rows.Length == 0) return new("empty", "no_matching_rows", null);
        var daily = selection.Rows.GroupBy(row => row.Date).Select(group => new DailyMetric(group.Key, Aggregate(group))).ToArray();
        var peak = daily.Max(day => day.Summary.Requests);
        return new("ok", null, Aggregate(selection.Rows), selection.Rows.GroupBy(row => row.Service)
            .Select(group => new ServiceMetric(group.Key, Aggregate(group))).ToArray(), daily,
            peak, daily.Where(day => day.Summary.Requests == peak).Select(day => day.Date).ToArray());
    }

    public MetricSelection NormalizeSelection(string? service, string? start, string? end)
    {
        var name = string.IsNullOrWhiteSpace(service) ? "all" : service.Trim().ToLowerInvariant();
        if (!ValidService(name)) throw new ArgumentException("service_invalid");
        var first = Start;
        var last = End;
        if ((!string.IsNullOrWhiteSpace(start) && !TryDate(start, out first)) ||
            (!string.IsNullOrWhiteSpace(end) && !TryDate(end, out last)))
            throw new ArgumentException("date_format_must_be_yyyy_mm_dd");
        if (first > last || last.DayNumber - first.DayNumber > 365)
            throw new ArgumentException("date_range_must_be_ordered_and_at_most_366_days");
        return new(name, first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public ComparisonResult Compare(string? service, string? beforeStart, string? beforeEnd,
        string? afterStart, string? afterEnd)
    {
        if (new[] { beforeStart, beforeEnd, afterStart, afterEnd }.Any(string.IsNullOrWhiteSpace))
            return new("invalid", "four_dates_required", null, null, null, null);
        var before = Summarize(service, beforeStart, beforeEnd);
        var after = Summarize(service, afterStart, afterEnd);
        if (before.Status == "invalid" || after.Status == "invalid")
            return new("invalid", before.Status == "invalid" ? before.Reason : after.Reason, null, null, null, null);
        if (DateOnly.ParseExact(beforeEnd!, "yyyy-MM-dd", CultureInfo.InvariantCulture) >=
            DateOnly.ParseExact(afterStart!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            return new("invalid", "periods_must_be_ordered_and_nonoverlapping", null, null, null, null);
        if (before.Summary is null || after.Summary is null)
            return new("empty", "no_matching_rows", before.Summary, after.Summary, null, null);
        return new("ok", null, before.Summary, after.Summary,
            after.Summary.ErrorRatePercent - before.Summary.ErrorRatePercent,
            after.Summary.AverageResponseMs - before.Summary.AverageResponseMs);
    }

    public OutlierResult FindOutliers(string? service, string? start, string? end)
    {
        var selection = Select(service, start, end);
        if (selection.Reason is not null) return new("invalid", selection.Reason, []);
        if (selection.Rows.Length == 0) return new("empty", "no_matching_rows", []);
        var outliers = new List<Outlier>();
        foreach (var group in selection.Rows.GroupBy(row => row.Service))
        {
            var samples = group.Where(row => row.Requests > 0).ToArray();
            if (samples.Length < 3) continue;
            foreach (var row in samples)
            {
                var peers = samples.Where(peer => peer.Date != row.Date).ToArray();
                Check("error_rate_percent", Ratio(row.Errors * 100m, row.Requests),
                    Median(peers.Select(peer => Ratio(peer.Errors * 100m, peer.Requests))), 2m);
                Check("average_response_ms", Ratio(row.TotalResponseMs, row.Requests),
                    Median(peers.Select(peer => Ratio(peer.TotalResponseMs, peer.Requests))), 100m);
                void Check(string metric, decimal value, decimal baseline, decimal minimumIncrease)
                {
                    if (value >= baseline * 2 && value - baseline >= minimumIncrease)
                        outliers.Add(new(row.Date, row.Service, metric, value, baseline));
                }
            }
        }
        return new("ok", null, outliers.OrderBy(item => item.Date).ThenBy(item => item.Service).ToArray());
    }

    public DashboardResult Dashboard(string? service, string? start, string? end)
    {
        var selection = Select(service, start, end);
        var summary = Summarize(service, start, end);
        return new(summary.Status, summary.Reason, Services, Start, End, summary.Summary,
            selection.Rows.GroupBy(row => row.Date).Select(group => new DailyMetric(group.Key, Aggregate(group))).ToArray(),
            selection.Rows, FindOutliers(service, start, end).Outliers);
    }

    public static MetricSummary Aggregate(IEnumerable<MetricRow> rows)
    {
        var values = rows.ToArray();
        var requests = values.Sum(row => row.Requests);
        var errors = values.Sum(row => row.Errors);
        var totalResponseMs = values.Sum(row => row.TotalResponseMs);
        return new(values.Length, requests, errors,
            requests == 0 ? null : Ratio(errors * 100m, requests),
            requests == 0 ? null : Ratio(totalResponseMs, requests));
    }

    private (string? Reason, MetricRow[] Rows) Select(string? service, string? start, string? end)
    {
        MetricSelection selection;
        try { selection = NormalizeSelection(service, start, end); }
        catch (ArgumentException error) { return (error.Message, []); }
        var first = DateOnly.ParseExact(selection.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = DateOnly.ParseExact(selection.End, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (null, _rows.Where(row => row.Date >= first && row.Date <= last &&
            (selection.Service == "all" || row.Service == selection.Service)).ToArray());
    }

    private static bool ValidService(string value) => value.Length is > 0 and <= 32 &&
        value.All(character => char.IsAsciiLetterLower(character) || character == '-');
    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    private static bool TryNumber(string value, long maximum, out long number) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0 && number <= maximum;
    private static decimal Ratio(decimal value, long requests) => Math.Round(value / requests, 6);
    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
            : sorted[sorted.Length / 2];
    }
}
