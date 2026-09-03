using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocsQa;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Progress.Observability.Extensions.AI;

internal static class PrivacyTests
{
    private const string Marker = "SYNTHETIC_PRIVATE_CONTENT";

    public static async Task<int> RunAsync(DocumentStore store)
    {
        var count = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("FAIL: " + label); count++; Console.WriteLine("PASS: " + label); }
        foreach (var template in new[] { "release-evidence-reviewer", "docs-qa", "operations-data-analyst", "ticket-triage" })
        {
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "privacy-sources", template + ".cs"));
            Check(!source.Contains("AddObservability", StringComparison.Ordinal)
                && source.Contains("ObservabilityTracer.Initialize", StringComparison.Ordinal)
                && source.Contains("new MetadataOnlyChatClient(", StringComparison.Ordinal),
                "shipped app traces provider calls through the metadata-only wrapper, never the SDK's: " + template);
        }

        // Use the pinned SDK's real listener with a no-export provider: no SDK
        // configuration lookup, credentials, network, model or ingestion calls.
        var assembly = typeof(ObservabilityTracer).Assembly;
        var providerField = typeof(ObservabilityTracer).GetField("_tracerProvider", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no_export_provider_seam_changed");
        Check(providerField.GetValue(null) is null, "privacy test starts without a configured exporter");
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(ObservabilityTracer.SourceName).SetSampler(new AlwaysOnSampler()).Build();
        providerField.SetValue(null, provider);
        var spans = new List<CapturedSpan>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ObservabilityTracer.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(new(activity.DisplayName, activity.TraceId.ToHexString(),
                activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value), activity.StatusDescription,
                JsonSerializer.Serialize(activity.Events.Select(evt => new { evt.Name, evt.Tags })))),
        };
        ActivitySource.AddActivityListener(listener);
        try
        {
            _ = assembly.GetType("Progress.Observability.Extensions.AI.ObservabilityActivityListener", throwOnError: true)!
                .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            using var client = new SyntheticClient();
            var response = await new AgentRuntime(client, store, "privacy-test").RunAsync(
                new AskRequest(Marker + "_QUESTION audit logs", new(Marker + "_PRIOR_QUESTION", Marker + "_PRIOR_ANSWER")), "privacy");
            Check(client.Calls == 3 && response.ToolCalls.SequenceEqual(new[] { "SearchDocuments", "ReadSection" })
                && response.Citations.Single().SourceId == "retention#audit-history", "unwrapped actual MAF workflow executes retrieval and citation tools");
            var workflowSpan = spans.Single(span => span.Name == "docs-qa.privacy");
            var toolSpans = spans.Where(span => span.Name == "gen_ai.execute_tool").ToArray();
            Check(spans.Count == 3 && workflowSpan.TraceId == response.TraceId
                && toolSpans.All(span => span.TraceId == response.TraceId)
                && toolSpans.Select(span => span.Tags["gen_ai.tool.name"]?.ToString())
                    .SequenceEqual(new[] { "SearchDocuments", "ReadSection" }),
                "shipped path emits its real workflow span plus one span per tool the agent ran");
            Check(workflowSpan.Tags["agent.tool.count"]?.ToString() == "2" && workflowSpan.Tags["agent.source.count"]?.ToString() == "1",
                "workflow records actual metadata counts");
            var captured = JsonSerializer.Serialize(spans);
            Check(!captured.Contains(Marker, StringComparison.Ordinal) && !captured.Contains("90 days", StringComparison.Ordinal)
                && !captured.Contains("retention#audit-history", StringComparison.Ordinal)
                && !captured.Contains("gen_ai.tool.input", StringComparison.Ordinal)
                && !captured.Contains("gen_ai.tool.output", StringComparison.Ordinal),
                "spans omit current/prior text, answer, source ids and tool arguments or results");
            Check(!captured.Contains("gen_ai.response", StringComparison.Ordinal) && !captured.Contains("gen_ai.usage", StringComparison.Ordinal),
                "the runtime alone makes no per-model content or token claim; the chat wrapper is added in Program.cs");
            spans.Clear();
            using var failing = new SyntheticClient(fail: true);
            try { await new AgentRuntime(failing, store, "privacy-test").RunAsync(Marker + "_QUESTION", "privacy"); throw new Exception("failure expected"); }
            catch (AgentRunException ex)
            {
                Check(ex.Code == "agent_run_failed" && spans.Count == 1 && !JsonSerializer.Serialize(spans).Contains(Marker, StringComparison.Ordinal),
                    "failed workflow emits safe status without raw provider error");
            }
        }
        finally { providerField.SetValue(null, null); }
        return count;
    }

    private sealed record CapturedSpan(string Name, string TraceId, Dictionary<string, object?> Tags, string? StatusDescription, string Events);

    private sealed class SyntheticClient(bool fail = false) : IChatClient
    {
        public int Calls { get; private set; }
        private static ChatResponse Tool(string name, string argument, string value) => new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("synthetic-" + name, name, new Dictionary<string, object?> { [argument] = value })])) { FinishReason = ChatFinishReason.ToolCalls };
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fail) throw new InvalidOperationException(Marker + "_PROVIDER_ERROR");
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Count();
            var response = results switch
            {
                0 => Tool("SearchDocuments", "query", Marker + "_ARGUMENT audit logs retained"),
                1 => Tool("ReadSection", "sourceId", "retention#audit-history"),
                _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, Marker + "_ANSWER Audit logs last 90 days.")) { FinishReason = ChatFinishReason.Stop },
            };
            foreach (var update in response.ToChatResponseUpdates()) { yield return update; await Task.Yield(); }
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Actual app workflow uses streaming.");
        public object? GetService(Type type, object? key = null) => key is null && type.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
