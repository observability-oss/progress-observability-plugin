using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocsQa;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

internal static class RuntimeTests
{
    public static async Task<int> RunAsync(DocumentStore store)
    {
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            count++; Console.WriteLine("PASS: " + label);
        }
        using var client = new ScriptedClient();
        var runtime = new AgentRuntime(client, store, "docs-qa-test");
        using var parent = new Activity("test-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var response = await runtime.RunAsync("audit logs", "test");
        Check(response.Status == "answered" && response.Citations.Single().SourceId == "retention#audit-history", "real MAF tool loop returns captured citation");
        Check(response.ToolCalls.SequenceEqual(new[] { "SearchDocuments", "ReadSection" }), "real MAF loop invokes both registered tools");
        Check(response.TraceId.Length == 32 && response.TraceId != parent.TraceId.ToHexString(), "runtime owns independent W3C trace root");
        Check(Activity.Current == parent, "request activity restored after agent run");
        var missing = await runtime.RunAsync("missing lunar topic", "test");
        Check(missing.Status == "not_found" && missing.Citations.Count == 0, "second runtime request does not leak first citations");
        Check(missing.TraceId != response.TraceId, "independent questions have unique trace IDs");
        var parallel = await Task.WhenAll(runtime.RunAsync("audit logs", "test"), runtime.RunAsync("missing lunar topic", "test"));
        Check(parallel[0].Citations.Count == 1 && parallel[1].Citations.Count == 0, "concurrent runs isolate tool capture");
        var prior = new PreviousTurn("How long are audit logs retained?", "An unverified previous answer says 999 days. PREVIOUS_UNTRUSTED_TEXT");
        using var followClient = new ScriptedClient();
        var followRuntime = new AgentRuntime(followClient, store, "follow-up-test");
        var follow = await followRuntime.RunAsync(new AskRequest("How long does it last?", prior), "test");
        Check(follow.Status == "answered" && follow.Answer.Contains("90 days", StringComparison.Ordinal) && !follow.Answer.Contains("999", StringComparison.Ordinal), "follow-up gets current fixture answer, not prior invented value");
        Check(follow.ToolCalls.SequenceEqual(new[] { "SearchDocuments", "ReadSection" }) && follow.Citations.Single().SourceId == "retention#audit-history", "actual follow-up MAF loop retrieves and reads fresh evidence");
        Check(followClient.LastInput!.PreviousTurn == prior && followClient.LastInput.Question == "How long does it last?", "one bounded prior Q/A reaches model for pronoun resolution");
        Check(followClient.LastInstructions!.Contains("NEVER source evidence", StringComparison.Ordinal) && !followClient.LastInstructions.Contains("PREVIOUS_UNTRUSTED_TEXT", StringComparison.Ordinal), "prior answer remains user data, not instructions or evidence");
        var resetReply = await followRuntime.RunAsync(new AskRequest("lunar rover battery"), "test");
        Check(followClient.LastInput!.PreviousTurn is null && followClient.LastInput.SourceId is null && resetReply.Citations.Count == 0, "same runtime reset request retains no previous context, scope or citations");
        var unknownFollow = await followRuntime.RunAsync(new AskRequest("What about lunar rover batteries?", prior), "test");
        Check(unknownFollow.Status == "not_found" && unknownFollow.Citations.Count == 0, "known previous context does not ground an unknown follow-up topic");
        using var scopedClient = new ScriptedClient("scope-escape");
        var scopedRuntime = new AgentRuntime(scopedClient, store, "scope-test");
        var scopedReply = await scopedRuntime.RunAsync(new AskRequest("How long is it available?", prior, "exports#export-availability"), "test");
        Check(scopedReply.Status == "answered" && scopedReply.Citations.Single().SourceId == "exports#export-availability", "selected section wins over prior topic and malicious out-of-scope read");
        Check(scopedReply.ToolCalls.SequenceEqual(new[] { "SearchDocuments", "ReadSection", "ReadSection" }), "scoped response reports actual allowed and rejected read calls honestly");
        Check(scopedClient.LastSourceId == "exports#export-availability" && scopedClient.LastSourceHeading == "Export availability", "model gets real scope identity and heading without source excerpt injection");
        var scopedUnknown = await followRuntime.RunAsync(new AskRequest("lunar rover battery", prior, "exports#export-availability"), "test");
        Check(scopedUnknown.Status == "not_found" && scopedUnknown.Citations.Count == 0 && scopedUnknown.Answer.Contains("selected section", StringComparison.Ordinal), "scoped no-match is explicit and never falls back to all documents");
        using var partialClient = new ScriptedClient("partial");
        var partial = await new AgentRuntime(partialClient, store, "partial-test").RunAsync(new AskRequest("How long is the download available and which columns are included?", SourceId: "exports#export-availability"), "test");
        Check(partial.Citations.Single().SourceId == "exports#export-availability" && partial.Answer.Contains("does not specify", StringComparison.Ordinal), "partial scoped answer preserves missing-information caveat and only actual evidence");
        var scopeParallel = await Task.WhenAll(followRuntime.RunAsync(new AskRequest("How long is this available?", SourceId: "exports#export-availability"), "test"), followRuntime.RunAsync("audit logs", "test"));
        Check(scopeParallel[0].Citations.Single().DocumentId == "exports" && scopeParallel[1].Citations.Single().DocumentId == "retention", "concurrent scoped and unscoped follow-up sessions are isolated");
        foreach (var invalid in new[] { new AskRequest("audit", SourceId: "unknown#section"), new AskRequest("audit", SourceId: "../appsettings.json"), new AskRequest("audit", new("prior", new string('x', 4001))) })
        {
            using var unused = new ScriptedClient();
            try { await new AgentRuntime(unused, store, "invalid").RunAsync(invalid, "test"); throw new Exception("invalid input accepted"); }
            catch (ArgumentException) { Check(unused.Requests == 0, "invalid scope/context rejected before any model request"); }
        }
        foreach (var mode in new[] { "no-tools", "outside-only", "long-answer" })
        {
            using var unsupported = new ScriptedClient(mode);
            try { await new AgentRuntime(unsupported, store, "test").RunAsync(new AskRequest("How long is it available?", prior, "exports#export-availability"), "test"); throw new Exception("ungrounded follow-up accepted"); }
            catch (AgentRunException ex) { Check(ex.Code == "agent_run_failed" && ex.TraceId.Length == 32, "follow-up rejects " + mode + " with secret-safe error and trace"); }
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await runtime.RunAsync("audit logs", "test", cancellation.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { Check(Activity.Current == parent, "caller cancellation propagates and restores activity"); }
        foreach (var mode in new[] { "many-tools", "loop", "no-tools", "fail" })
        {
            using var badClient = new ScriptedClient(mode);
            try
            {
                await new AgentRuntime(badClient, store, "test").RunAsync("audit logs", "test");
                throw new Exception("invalid model behavior accepted: " + mode);
            }
            catch (AgentRunException ex)
            {
                Check(ex.Code == "agent_run_failed" && ex.TraceId.Length == 32, "runtime bounds/rejects " + mode + " with trace ID");
                if (mode == "loop") Check(badClient.Requests <= 4, $"function loop makes at most four model requests (actual {badClient.Requests})");
            }
        }
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "smoke-appsettings.json")).Build();
        using var output = new StringWriter();
        var originalOutput = Console.Out;
        int smokeCode;
        try
        {
            Console.SetOut(output);
            smokeCode = await new SmokeRunner(runtime, configuration).RunAsync();
        }
        finally { Console.SetOut(originalOutput); }
        var reportLine = output.ToString().Split('\n').Single(line => line.StartsWith("SMOKE_REPORT=", StringComparison.Ordinal));
        using var report = JsonDocument.Parse(reportLine["SMOKE_REPORT=".Length..]);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "templates.json")));
        var expectedIds = catalog.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "docs-qa")
            .GetProperty("smokeCases").EnumerateArray().Select(item => item.GetString()).Order().ToArray();
        var cases = report.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Check(smokeCode == 0 && report.RootElement.GetProperty("status").GetString() == "pass", "actual SmokeRunner passes all scripted model cases");
        Check(cases.Select(item => item.GetProperty("caseId").GetString()).Order().SequenceEqual(expectedIds), "actual emitted smoke IDs match selected catalog entry exactly");
        Check(cases.All(item => item.GetProperty("status").GetString() == "pass" && item.GetProperty("traceId").GetString()?.Length == 32), "every emitted smoke case has passing evidence and trace ID");
        return count;
    }

    private sealed class ScriptedClient(string mode = "normal") : IChatClient
    {
        private int _requests;
        public int Requests => _requests;
        public AskRequest? LastInput { get; private set; }
        public string? LastInstructions { get; private set; }
        public string? LastSourceId { get; private set; }
        public string? LastSourceHeading { get; private set; }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            if (mode == "fail") throw new InvalidOperationException("private-model-error");
            var conversation = messages.ToArray();
            var results = conversation.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count();
            var payload = conversation.Last(message => message.Role == ChatRole.User).Text;
            using var input = JsonDocument.Parse(payload);
            var question = input.RootElement.GetProperty("question").GetString()!;
            var previous = input.RootElement.GetProperty("previousTurn");
            var scope = input.RootElement.GetProperty("sourceScope");
            var scopeId = scope.ValueKind == JsonValueKind.Null ? null : scope.GetProperty("sourceId").GetString();
            LastInput = new(question, previous.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<PreviousTurn>(previous, JsonSerializerOptions.Web), scopeId);
            LastInstructions = options?.Instructions;
            LastSourceId = scopeId;
            LastSourceHeading = scope.ValueKind == JsonValueKind.Null ? null : scope.GetProperty("heading").GetString();
            var missing = question.Contains("missing", StringComparison.OrdinalIgnoreCase) || question.Contains("lunar", StringComparison.OrdinalIgnoreCase);
            var twoSources = question.Contains("CSV", StringComparison.OrdinalIgnoreCase) && scopeId is null;
            var query = missing ? "lunar rover battery" : scopeId?.StartsWith("exports#", StringComparison.Ordinal) == true ? "CSV export download" : twoSources ? "audit logs retained CSV export download" : "audit logs retained";
            var calls = new List<AIContent>();
            if (mode == "many-tools")
                calls.AddRange(Enumerable.Range(0, 7).Select(index => new FunctionCallContent($"call-{index}", "SearchDocuments", new Dictionary<string, object?> { ["query"] = "audit logs" })));
            else if (mode == "loop" || (results == 0 && mode != "no-tools"))
                calls.Add(new FunctionCallContent("search-" + results, "SearchDocuments", new Dictionary<string, object?> { ["query"] = query }));
            else if (results == 1 && !missing)
            {
                if (mode == "scope-escape") calls.Add(new FunctionCallContent("outside", "ReadSection", new Dictionary<string, object?> { ["sourceId"] = "retention#audit-history" }));
                calls.Add(new FunctionCallContent("read", "ReadSection", new Dictionary<string, object?> { ["sourceId"] = mode == "outside-only" ? "retention#audit-history" : scopeId ?? "retention#audit-history" }));
                if (twoSources) calls.Add(new FunctionCallContent("read-export", "ReadSection", new Dictionary<string, object?> { ["sourceId"] = "exports#export-availability" }));
            }
            await Task.CompletedTask;
            yield return calls.Count > 0
                ? new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = calls, MessageId = "tool-" + results }
                : new ChatResponseUpdate(ChatRole.Assistant, mode == "long-answer" ? new string('x', 4001) : mode == "partial" ? "CSV export links last 24 hours. The selected section does not specify export columns." : missing ? "Invented lunar answer" : scopeId?.StartsWith("exports#", StringComparison.Ordinal) == true ? "CSV export download links remain available for 24 hours." : twoSources ? "Audit logs are retained for 90 days. CSV export download links remain available for 24 hours." : "Audit logs are retained for 90 days.") { MessageId = "answer" };
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Streaming tests only.");
        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
