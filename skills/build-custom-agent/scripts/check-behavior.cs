using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

#if !BEHAVIOR_CHECK_TESTS
return await BehaviorChecker.RunAsync(args);
#endif

// Transport only: Copilot judges the answers against expectations written before this run.
internal static class BehaviorChecker
{
    private const string Usage = "Usage: dotnet run --file check-behavior.cs -- --url <http://127.0.0.1:port> --checks <JSON file> --deadline <UTC ISO8601> [--timeout-seconds <1..60>]";
    internal sealed record Checks(string Task, string FollowUp, string Boundary);
    internal sealed record Options(Uri Url, Checks Checks, DateTimeOffset Deadline, int TimeoutSeconds);
    internal sealed record Turn(string User, string Assistant);
    internal sealed record Request(string Message, Turn[] History);
    internal sealed record Result(string Check, string Status, string? Answer = null, string? TraceId = null, string? Error = null);
    internal sealed record Report(string Status, DateTimeOffset DeadlineUtc, long RemainingSeconds, IReadOnlyList<Result> Results);

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }
        try
        {
            var options = Parse(args);
            using var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            }) { Timeout = Timeout.InfiniteTimeSpan };
            var report = await CheckAsync(client, options);
            Console.WriteLine("BEHAVIOR_REPORT=" + JsonSerializer.Serialize(report, BehaviorJson.Default.Report));
            return report.Status == "completed" ? 0 : 1;
        }
        catch (Exception error) when (error is ArgumentException or IOException or
                                       UnauthorizedAccessException or JsonException)
        {
            // Never echo supplied URLs, file contents, or exception messages (they may contain secrets).
            Console.Error.WriteLine("check-behavior: invalid arguments, URL, deadline, or checks file. " + Usage);
            return 2;
        }
    }

    internal static Options Parse(string[] args)
    {
        if (args.Length is not (6 or 8)) throw new ArgumentException();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--url" or "--checks" or "--deadline" or "--timeout-seconds") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException();
        }
        if (!values.TryGetValue("--url", out var address) ||
            !values.TryGetValue("--checks", out var path) ||
            !values.TryGetValue("--deadline", out var deadlineText) ||
            !Regex.IsMatch(address, @"\Ahttp://127\.0\.0\.1:[1-9][0-9]{0,4}/?\z") ||
            !Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Port is < 1 or > 65535 ||
            !Regex.IsMatch(deadlineText, @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)\z") ||
            !DateTimeOffset.TryParse(deadlineText, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var deadline)) throw new ArgumentException();
        var timeout = 60;
        if (values.TryGetValue("--timeout-seconds", out var text) &&
            (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout is < 1 or > 60))
            throw new ArgumentException();
        return new Options(url, ReadChecks(File.ReadAllText(path)), deadline, timeout);
    }

    internal static Checks ReadChecks(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("expected", out var expected) || expected.ValueKind != JsonValueKind.Object)
            throw new ArgumentException();
        foreach (var name in new[] { "task", "follow_up", "fresh_chat", "boundary" })
            _ = Read(expected, name); // Require predeclared expectations, but never retain or send them.
        return new Checks(Read(root, "task"), Read(root, "followUp"), Read(root, "boundary"));

        static string Read(JsonElement source, string name)
        {
            if (!source.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String ||
                field.GetString() is not { } value || string.IsNullOrWhiteSpace(value) || value.Length > 4_000)
                throw new ArgumentException();
            return value; // Preserve the caller's exact text, including whitespace.
        }
    }

    internal static async Task<Report> CheckAsync(HttpClient client, Options options)
    {
        var results = new List<Result>();
        var names = new[] { "task", "follow_up", "fresh_chat", "boundary" };
        var prompts = new[] { options.Checks.Task, options.Checks.FollowUp, options.Checks.FollowUp, options.Checks.Boundary };
        for (var i = 0; i < names.Length; i++)
        {
            if (i == 1 && results[0].Status != "completed")
            {
                results.Add(new(names[i], "untested", Error: "task_incomplete"));
                continue;
            }
            var remaining = options.Deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                results.Add(new(names[i], "untested", Error: "deadline"));
                continue;
            }
            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            using var cancellation = new CancellationTokenSource(remaining < timeout ? remaining : timeout);
            Turn[] history = i == 1 ? [new(options.Checks.Task, results[0].Answer!)] : [];
            try
            {
                using var response = await client.PostAsJsonAsync(new Uri(options.Url, "/api/chat"),
                    new Request(prompts[i], history), BehaviorJson.Default.Request, cancellation.Token);
                if (!response.IsSuccessStatusCode)
                {
                    results.Add(new(names[i], "incomplete", Error: $"http_{(int)response.StatusCode}"));
                    continue;
                }
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation.Token),
                    cancellationToken: cancellation.Token);
                var body = document.RootElement;
                if (body.ValueKind != JsonValueKind.Object ||
                    !body.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(answer.GetString()) ||
                    !body.TryGetProperty("traceId", out var trace) || trace.ValueKind != JsonValueKind.String ||
                    !Regex.IsMatch(trace.GetString()!, @"\A[0-9a-fA-F]{32}\z"))
                {
                    results.Add(new(names[i], "incomplete", Error: "invalid_response"));
                    continue;
                }
                results.Add(new(names[i], "completed", answer.GetString(), trace.GetString()));
            }
            catch (OperationCanceledException)
            {
                results.Add(new(names[i], "incomplete", Error: DateTimeOffset.UtcNow >= options.Deadline ? "deadline" : "timeout"));
            }
            catch (JsonException)
            {
                results.Add(new(names[i], "incomplete", Error: "invalid_response"));
            }
            catch (Exception error) when (error is HttpRequestException or IOException)
            {
                results.Add(new(names[i], "incomplete", Error: "request_failed"));
            }
        }
        // Snapshot at report creation, not a renewed deadline or a guarantee of time remaining later.
        var remainingSeconds = (long)Math.Max(0, Math.Floor((options.Deadline - DateTimeOffset.UtcNow).TotalSeconds));
        return new Report(results.All(result => result.Status == "completed") ? "completed" : "incomplete", options.Deadline, remainingSeconds, results);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BehaviorChecker.Report))]
[JsonSerializable(typeof(BehaviorChecker.Request))]
internal partial class BehaviorJson : JsonSerializerContext;
