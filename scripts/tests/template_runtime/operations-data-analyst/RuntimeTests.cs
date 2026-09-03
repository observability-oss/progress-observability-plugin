using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OperationsDataAnalyst;

internal static class RuntimeTests
{
    public static async Task<int> RunAsync(MetricsStore metrics)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); count++; }
        ViewWorkflow Workflow(AnalystClient client) => new(new(client, metrics, "runtime-test"), metrics);
        var checkout = new ViewSpec("checkout", "2026-08-01", "2026-08-03");
        using var client = new AnalystClient();
        var workflow = Workflow(client);
        using var parent = new Activity("outer-http-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var events = new List<string>();
        var reply = await workflow.AskAsync(new("Explain these metrics", checkout), onView: data =>
        {
            Check(client.Requests == 1, "deterministic view publishes before the synthesis model request");
            Check(data.Dashboard.Summary is { Requests: 6060, Errors: 60 }, "early view carries actual aggregate values");
            events.Add("view"); return Task.CompletedTask;
        }, onText: text => { events.Add("text"); return Task.CompletedTask; });
        Check(client.Requests == 2 && client.NonStreamingRequests == 0, "one real MAF exploration plus synthesis, no separate intent request");
        Check(events.SequenceEqual(new[] { "view", "text", "text" }), "view appears before every answer chunk");
        Check(reply.Evidence is [{ Tool: "ExploreMetrics", Result: ExplorationResult { Summary.Summary: { Requests: 6060, Errors: 60 } } }], "evidence identifies the actual model-invoked tool and deterministic numbers");
        Check(reply.Evidence[0].Arguments is ExplorationArguments { View: var evidenceView } && evidenceView == checkout,
            "evidence contains normalized actual chart selection");
        Check(reply.TraceId.Length == 32 && reply.TraceId != parent.TraceId.ToHexString() && Activity.Current == parent &&
            client.TraceIds.All(trace => trace == reply.TraceId), "single independent agent trace covers both model requests and restores parent");
        Check(client.OutputLimits.All(limit => limit == 500), "both requests have bounded output tokens");

        using var emptyClient = new AnalystClient { Answer = "Invented empty result: 999 requests." };
        var emptyChunks = new List<string>();
        var empty = await Workflow(emptyClient).AskAsync(new("Keep these dates", checkout with { Start = "2026-09-01", End = "2026-09-02" }),
            onText: text => { emptyChunks.Add(text); return Task.CompletedTask; });
        Check(empty.Answer.StartsWith("No matching data", StringComparison.Ordinal) && emptyChunks.Single() == empty.Answer,
            "empty data replaces hallucinated synthesis before it is streamed");
        Check(empty.Dashboard!.Rows.Count == 0 && empty.Chart!.Points.Count == 0, "empty result has no stale chart or rows");

        foreach (var mode in new[] { "no-tools", "loop", "many-tools", "fail", "fail-after-tool", "large-answer", "invalid-view" })
        {
            using var bad = new AnalystClient { Mode = mode };
            var views = 0;
            try
            {
                await Workflow(bad).AskAsync(new("Inspect", checkout), onView: _ => { views++; return Task.CompletedTask; });
                throw new InvalidOperationException("invalid model accepted: " + mode);
            }
            catch (AgentRunException error)
            {
                Check(error.TraceId.Length == 32 && error.Message == "agent_run_failed" && Activity.Current == parent,
                    "bounded failure preserves safe trace context: " + mode);
                Check(bad.Requests <= 2, "every failure stays within two model requests: " + mode);
                if (mode is "invalid-view" or "fail" or "no-tools") Check(views == 0, "invalid result does not publish a fabricated view: " + mode);
                if (mode == "fail-after-tool") Check(views == 1, "synthesis failure preserves only the already-computed view event");
            }
        }
        using (var cancellation = new CancellationTokenSource())
        using (var cancelled = new AnalystClient())
        {
            cancellation.Cancel();
            try { await Workflow(cancelled).AskAsync(new("Inspect", checkout), cancellation.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { Check(Activity.Current == parent && cancelled.Requests == 0, "pre-cancelled request makes no model request and restores trace"); }
        }
        using (var cancellation = new CancellationTokenSource())
        using (var cancelled = new AnalystClient())
        {
            try
            {
                await Workflow(cancelled).AskAsync(new("Inspect", checkout), cancellation.Token,
                    onView: _ => { cancellation.Cancel(); return Task.CompletedTask; });
                throw new Exception("cancellation after view ignored");
            }
            catch (OperationCanceledException) { Check(Activity.Current == parent && cancelled.Requests == 1, "cancellation after view stops synthesis and restores trace"); }
        }
        using var concurrentClient = new AnalystClient();
        var concurrentWorkflow = Workflow(concurrentClient);
        var parallel = await Task.WhenAll(concurrentWorkflow.AskAsync(new("Inspect", checkout)),
            concurrentWorkflow.AskAsync(new("Inspect", checkout with { Service = "auth" })));
        Check(parallel[0].Dashboard!.Summary!.Requests == 6060 && parallel[1].Dashboard!.Summary!.Requests == 3030 &&
            parallel[0].TraceId != parallel[1].TraceId && parallel[0].Evidence.Count == 1 && parallel[1].Evidence.Count == 1,
            "concurrent runs isolate service scope, tool evidence and trace");

        using var smokeClient = new AnalystClient { IncludeOutliers = true };
        using var output = new StringWriter();
        var original = Console.Out;
        int smokeCode;
        try { Console.SetOut(output); smokeCode = await new SmokeRunner(Workflow(smokeClient)).RunAsync(); }
        finally { Console.SetOut(original); }
        var reportLine = output.ToString().Split('\n').Single(line => line.StartsWith("SMOKE_REPORT=", StringComparison.Ordinal));
        using var report = JsonDocument.Parse(reportLine["SMOKE_REPORT=".Length..]);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "templates.json")));
        var expectedIds = catalog.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "operations-data-analyst")
            .GetProperty("smokeCases").EnumerateArray().Select(item => item.GetString()).Order().ToArray();
        var cases = report.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Check(smokeCode == 0 && report.RootElement.GetProperty("status").GetString() == "pass", "all three canonical numeric smoke cases pass through real MAF tools");
        Check(smokeClient.Contexts.All(context => context.GetProperty("fixedView").GetBoolean()),
            "fixed-selection numeric smoke uses the same exact-view contract as a dashboard click");
        Check(smokeClient.Requests == 6 && smokeClient.NonStreamingRequests == 0, "three smoke cases each use exactly two model calls");
        Check(cases.Select(item => item.GetProperty("caseId").GetString()).Order().SequenceEqual(expectedIds), "smoke IDs remain canonical");
        Check(cases.All(item => item.GetProperty("tools").EnumerateArray().Single().GetString() == "ExploreMetrics"), "smoke reports the actual agent tool rather than internal helpers");
        return count;
    }
}

internal sealed class AnalystClient : IChatClient
{
    private int _requests;
    public int Requests => _requests;
    public int NonStreamingRequests { get; private set; }
    public string Mode { get; set; } = "normal";
    public string Answer { get; set; } = "The synthetic metrics show the selected values. The chart shows the relevant comparison.";
    public bool IncludeOutliers { get; set; }
    public Func<JsonElement, Dictionary<string, object?>>? Plan { get; set; }
    public List<string?> TraceIds { get; } = [];
    public List<int?> OutputLimits { get; } = [];
    public List<JsonElement> Contexts { get; } = [];
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    { NonStreamingRequests++; throw new InvalidOperationException("separate_intent_request_forbidden"); }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        lock (TraceIds) { TraceIds.Add(Activity.Current?.TraceId.ToHexString()); OutputLimits.Add(options?.MaxOutputTokens); }
        var conversation = messages.ToArray();
        var results = conversation.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count();
        using var json = JsonDocument.Parse(conversation.Last(message => message.Role == ChatRole.User).Text);
        if (results == 0) lock (Contexts) Contexts.Add(json.RootElement.Clone());
        if (Mode == "fail" || Mode == "fail-after-tool" && results > 0) throw new InvalidOperationException("private-model-error");
        await Task.Yield();
        if ((results == 0 || Mode == "loop") && Mode != "no-tools")
        {
            var arguments = Plan?.Invoke(json.RootElement) ?? new Dictionary<string, object?>();
            if (IncludeOutliers) arguments["includeOutliers"] = true;
            if (Mode == "invalid-view") arguments["metric"] = "revenue";
            var name = Mode is "unsupported" or "clarify" ? "ExplainLimitation" : "ExploreMetrics";
            if (name == "ExplainLimitation") arguments = new() { ["status"] = Mode };
            var calls = new List<AIContent> { new FunctionCallContent("explore-" + results, name, arguments) };
            if (Mode == "many-tools") calls.Add(new FunctionCallContent("extra", name, arguments));
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = "tool-" + results, Contents = calls };
        }
        else
        {
            var answer = Mode == "large-answer" ? new string('x', 6001) : Answer;
            yield return new(ChatRole.Assistant, answer[..(answer.Length / 2)]) { MessageId = "answer" };
            yield return new(ChatRole.Assistant, answer[(answer.Length / 2)..]) { MessageId = "answer" };
        }
    }
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}
