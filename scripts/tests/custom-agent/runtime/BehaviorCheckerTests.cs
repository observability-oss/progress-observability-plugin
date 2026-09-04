using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class BehaviorCheckerTests
{
    public static async Task<int> RunAsync()
    {
        var assertions = 0;
        const string task = "  Mock UI-731: save \"filters\".\r\nKeep \\ exact text.  ";
        const string answer = "\r\n  **UI-731**\nQuote: \"saved filters\"; path \\local\t\n  ";
        const string followUp = "  Which issue did I just provide?\n";
        const string boundary = "Create it in live Jira.";
        var expected = new Dictionary<string, string>
        {
            ["task"] = "NEVER_SEND_EXPECTATIONS task",
            ["follow_up"] = "NEVER_SEND_EXPECTATIONS follow-up",
            ["fresh_chat"] = "NEVER_SEND_EXPECTATIONS fresh chat",
            ["boundary"] = "NEVER_SEND_EXPECTATIONS boundary",
        };
        var json = JsonSerializer.Serialize(new { task, followUp, boundary, expected });
        var checks = BehaviorChecker.ReadChecks(json);
        using (var handler = new CapturingHandler((_, _) => Task.FromResult(Reply(answer))))
        using (var client = new HttpClient(handler))
        {
            var options = Options(checks);
            var remainingAtStart = (long)Math.Floor((options.Deadline - DateTimeOffset.UtcNow).TotalSeconds);
            var report = await BehaviorChecker.CheckAsync(client, options);
            Check(report.Status == "completed" && report.Results.All(result => result.Status == "completed"), "all four transports completed, not behavior passed");
            Check(report.DeadlineUtc.EqualsExact(options.Deadline), "completed report preserves exact initial deadline");
            Check(report.RemainingSeconds > 0 && report.RemainingSeconds <= remainingAtStart, "completed report has positive whole-second deadline snapshot");
            using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(report, BehaviorJson.Default.Report));
            Check(serialized.RootElement.GetProperty("deadlineUtc").GetDateTimeOffset().EqualsExact(options.Deadline), "deadlineUtc serializes as round-trippable UTC ISO8601");
            Check(serialized.RootElement.GetProperty("remainingSeconds").GetInt64() == report.RemainingSeconds, "remainingSeconds serializes as whole seconds");
            Check(handler.Requests.Count == 4, "exactly four requests, no retries");
            Check(report.Results.Select(result => result.Check).SequenceEqual(["task", "follow_up", "fresh_chat", "boundary"]), "fixed check order");
            Check(report.Results.All(result => result.Answer == answer && result.TraceId == new string('a', 32)), "answers and trace IDs preserved exactly");
            Check(handler.Urls.All(url => url == "http://127.0.0.1:54321/api/chat"), "only local chat endpoint used");
            Check(handler.Requests.All(request => !request.Contains("NEVER_SEND_EXPECTATIONS") && !request.Contains("expected")), "expectations never sent to app");
            string[] prompts = [task, followUp, followUp, boundary];
            for (var i = 0; i < 4; i++)
            {
                using var document = JsonDocument.Parse(handler.Requests[i]);
                var body = document.RootElement;
                Check(body.EnumerateObject().Count() == 2 && body.GetProperty("message").GetString() == prompts[i], $"request {i} exact prompt and no extra fields");
                var history = body.GetProperty("history");
                Check(history.GetArrayLength() == (i == 1 ? 1 : 0), $"request {i} history isolation");
                if (i == 1)
                {
                    Check(history[0].GetProperty("user").GetString() == task, "follow-up preserves exact original task");
                    Check(history[0].GetProperty("assistant").GetString() == answer, "follow-up preserves exact full answer including whitespace and escaping");
                }
            }
        }

        foreach (var failed in new Func<HttpResponseMessage>[]
        {
            () => new(HttpStatusCode.BadGateway) { Content = new StringContent("SECRET_ERROR_BODY") },
            () => new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://example.com") }, Content = new StringContent("SECRET_ERROR_BODY") },
            () => new(HttpStatusCode.OK) { Content = new StringContent("SECRET_ERROR_BODY") },
            () => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answer = "  ", traceId = new string('a', 32) }) },
            () => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answer = "good", traceId = "invalid" }) },
            () => new(HttpStatusCode.OK) { Content = new StringContent("[]") },
        })
        {
            using var handler = new CapturingHandler((i, _) => Task.FromResult(i == 0 ? failed() : Reply("independent")));
            using var client = new HttpClient(handler);
            var report = await BehaviorChecker.CheckAsync(client, Options(checks));
            Check(report.Status == "incomplete" && report.Results[0].Status == "incomplete", "HTTP, redirect or malformed result is incomplete");
            Check(report.Results[1].Status == "untested" && report.Results[1].Error == "task_incomplete", "dependent follow-up skipped after task failure");
            Check(handler.Requests.Count == 3 && report.Results.Skip(2).All(result => result.Status == "completed"), "independent checks continue without retry or redirect");
            Check(!JsonSerializer.Serialize(report).Contains("SECRET_ERROR_BODY"), "error bodies not disclosed");
        }

        using (var handler = new CapturingHandler((i, _) => i == 0 ? throw new HttpRequestException("SECRET_EXCEPTION") : Task.FromResult(Reply("ok"))))
        using (var client = new HttpClient(handler))
        {
            var report = await BehaviorChecker.CheckAsync(client, Options(checks));
            Check(report.Results[0].Error == "request_failed" && handler.Requests.Count == 3, "network failure is safe and bounded");
            Check(!JsonSerializer.Serialize(report).Contains("SECRET_EXCEPTION"), "exception messages not disclosed");
        }

        using (var handler = new CapturingHandler(async (i, token) =>
        {
            if (i == 0) await Task.Delay(Timeout.Infinite, token);
            return Reply("ok");
        }))
        using (var client = new HttpClient(handler))
        {
            var report = await BehaviorChecker.CheckAsync(client, Options(checks) with { TimeoutSeconds = 1 });
            Check(report.Results[0].Error == "timeout", "request timeout honored");
            Check(report.RemainingSeconds > 0, "request timeout does not claim overall deadline elapsed");
            Check(handler.Requests.Count == 3 && report.Results[3].Status == "completed", "timeout skips dependent call but permits independent calls");
        }

        using (var handler = new CapturingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Reply("unreachable");
        }))
        using (var client = new HttpClient(handler))
        {
            // Leave enough time for cold/JIT-heavy CI to dispatch the request; the
            // handler then proves the shared deadline beats its 60-second timeout.
            var options = Options(checks) with { Deadline = DateTimeOffset.UtcNow.AddSeconds(1) };
            var watch = Stopwatch.StartNew();
            var report = await BehaviorChecker.CheckAsync(client, options);
            Check(report.Results[0].Error == "deadline" && watch.Elapsed < TimeSpan.FromSeconds(10), "remaining deadline wins over 60-second request timeout");
            Check(report.DeadlineUtc.EqualsExact(options.Deadline), "incomplete report keeps original deadline");
            Check(report.RemainingSeconds == 0, "deadline cancellation has no remaining time");
            Check(handler.Requests.Count == 1 && report.Results.Skip(1).All(result => result.Status == "untested"), "deadline prevents remaining requests");
            var second = await BehaviorChecker.CheckAsync(client, options);
            Check(handler.Requests.Count == 1 && second.Results.All(result => result.Status == "untested"), "reused expired deadline cannot start post-repair requests");
            Check(second.DeadlineUtc.EqualsExact(options.Deadline), "expired report never resets deadline");
            Check(second.RemainingSeconds == 0, "expired report clamps remaining seconds to zero");
        }

        foreach (var invalid in new[]
        {
            "[]", "{}", "null", JsonSerializer.Serialize(new { task, followUp, boundary, expected = (object?)null }),
            JsonSerializer.Serialize(new { task, followUp, boundary, expected = new { } }),
            JsonSerializer.Serialize(new { task, followUp, boundary, expected = new { task = "Only task" } }),
            JsonSerializer.Serialize(new { task = " ", followUp, boundary, expected }),
            JsonSerializer.Serialize(new { task, followUp = new string('x', 4_001), boundary, expected }),
            JsonSerializer.Serialize(new { task, followUp, boundary = 4, expected }),
        })
        {
            try { BehaviorChecker.ReadChecks(invalid); throw new InvalidOperationException("Invalid checks accepted."); }
            catch (ArgumentException) { assertions++; }
        }
        foreach (var name in expected.Keys)
        {
            foreach (var value in new object?[] { null, " ", 4 })
            {
                var invalidExpected = expected.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
                invalidExpected[name] = value;
                RejectChecks(invalidExpected);
            }
            RejectChecks(expected.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
        }

        void RejectChecks(Dictionary<string, object?> invalidExpected)
        {
            try
            {
                BehaviorChecker.ReadChecks(JsonSerializer.Serialize(new { task, followUp, boundary, expected = invalidExpected }));
                throw new InvalidOperationException("Incomplete expectations accepted.");
            }
            catch (ArgumentException) { assertions++; }
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"behavior-checks-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            var deadline = DateTimeOffset.UtcNow.AddMinutes(1).ToString("O");
            string[] Args(string url = "http://127.0.0.1:54321") => ["--url", url, "--checks", temporary, "--deadline", deadline];
            Check(BehaviorChecker.Parse(Args()).Checks == checks, "valid CLI input parsed without modifying prompts");
            Check(BehaviorChecker.Parse([.. Args(), "--timeout-seconds", "1"]).TimeoutSeconds == 1, "bounded timeout override parsed");
            foreach (var invalid in new[]
            {
                "https://127.0.0.1:54321", "http://localhost:54321", "http://2130706433:54321", "http://[::1]:54321",
                "http://user:SECRET@127.0.0.1:54321", "http://127.0.0.1:54321?key=SECRET", "http://127.0.0.1:54321/#x",
                "http://127.0.0.1:54321/path", "http://127.0.0.1:0", "http://127.0.0.1:65536", "http://127.0.0.1:054321",
            }) Reject(Args(invalid));
            Reject([.. Args(), "--timeout-seconds", "0"]);
            Reject([.. Args(), "--timeout-seconds", "61"]);
            Reject([.. Args(), "--other", "value"]);
            Reject([.. Args(), "--url", "http://127.0.0.1:1"]);
            Reject(["--url", "http://127.0.0.1:54321", "--checks", temporary, "--deadline", "2026-09-03"]);
            Reject(["--url", "http://127.0.0.1:54321", "--checks", temporary, "--deadline", "2026-09-03T12:00:00+03:00"]);
            Reject(["--url"]);
            var (code, output, _) = await Capture(["--url", "http://127.0.0.1:54321", "--checks", temporary, "--deadline", "2020-01-01T00:00:00Z"]);
            Check(code == 1 && output.StartsWith("BEHAVIOR_REPORT=") && output.Contains("untested"), "expired deadline emits report and exit 1 without network");
            using var expired = JsonDocument.Parse(output["BEHAVIOR_REPORT=".Length..]);
            Check(expired.RootElement.GetProperty("deadlineUtc").GetDateTimeOffset().EqualsExact(
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), "expired CLI output includes reusable UTC deadline");
            var (badCode, _, error) = await Capture(Args("http://SECRET@example.com"));
            Check(badCode == 2 && !error.Contains("SECRET"), "invalid arguments exit 2 without echoing secrets");
            var (helpCode, help, _) = await Capture(["--help"]);
            Check(helpCode == 0 && help.Contains("--deadline"), "file-based helper help exits successfully");
        }
        finally { File.Delete(temporary); }
        return assertions;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            assertions++;
        }
        void Reject(string[] args)
        {
            try { BehaviorChecker.Parse(args); throw new InvalidOperationException("Invalid CLI input accepted."); }
            catch (ArgumentException) { assertions++; }
        }
    }

    private static BehaviorChecker.Options Options(BehaviorChecker.Checks checks)
        => new(new Uri("http://127.0.0.1:54321"), checks, DateTimeOffset.UtcNow.AddMinutes(1), 60);

    private static HttpResponseMessage Reply(string answer)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answer, traceId = new string('a', 32) }) };

    private static async Task<(int Code, string Output, string Error)> Capture(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            return (await BehaviorChecker.RunAsync(args), output.ToString(), error.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private sealed class CapturingHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal List<string> Urls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return await respond(Requests.Count - 1, cancellationToken);
        }
    }
}
