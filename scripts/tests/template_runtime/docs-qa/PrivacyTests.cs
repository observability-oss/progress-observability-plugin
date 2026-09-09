using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            Check(source.Contains(".AddObservability(", StringComparison.Ordinal)
                && source.Contains("RecordInputs = false", StringComparison.Ordinal)
                && source.Contains("RecordOutputs = false", StringComparison.Ordinal)
                && !source.Contains("MetadataOnlyChatClient", StringComparison.Ordinal),
                "shipped app uses SDK model tracing with both content flags disabled: " + template);
        }

        // Use the pinned SDK's real listener with a no-export provider: no SDK
        // configuration lookup, credentials, network, model or ingestion calls.
        var assembly = typeof(ObservabilityTracer).Assembly;
        var providerField = typeof(ObservabilityTracer).GetField("_tracerProvider", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("no_export_provider_seam_changed");
        Check(providerField.GetValue(null) is null, "privacy test starts without a configured exporter");
        var sampler = (Sampler)Activator.CreateInstance(
            assembly.GetType("Progress.Observability.Extensions.AI.AgentObservabilitySampler")!, true)!;
        var exporter = new CaptureExporter();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(ObservabilityTracer.SourceName)
            .SetSampler(sampler).AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        providerField.SetValue(null, provider);
        var spans = exporter.Spans;
        try
        {
            _ = assembly.GetType("Progress.Observability.Extensions.AI.ObservabilityActivityListener", throwOnError: true)!
                .GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            using var providerClient = new SyntheticClient();
            using var client = providerClient.AddObservability(options =>
            {
                options.AppName = "privacy-test";
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            var response = await new AgentRuntime(client, store, "privacy-test").RunAsync(
                new AskRequest(Marker + "_QUESTION audit logs", new(Marker + "_PRIOR_QUESTION", Marker + "_PRIOR_ANSWER")), "privacy");
            Check(providerClient.Calls == 3 && response.ToolCalls.SequenceEqual(new[] { "SearchDocuments", "ReadSection" })
                && response.Citations.Single().SourceId == "retention#audit-history", "SDK-instrumented MAF workflow executes retrieval and citation tools");
            var workflowSpan = spans.Single(span => span.Name == "docs-qa.privacy");
            var toolSpans = spans.Where(span => span.Name == "gen_ai.execute_tool").ToArray();
            var chatSpans = spans.Where(span => span.Name == "gen_ai.chat").ToArray();
            Check(chatSpans.Length == providerClient.Calls && workflowSpan.TraceId == response.TraceId
                && chatSpans.Concat(toolSpans).All(span => span.TraceId == response.TraceId
                    && spans.Any(parent => parent.SpanId == span.ParentSpanId && parent.TraceId == span.TraceId))
                && toolSpans.All(span => span.TraceId == response.TraceId)
                && toolSpans.Select(span => span.Tags["gen_ai.tool.name"]?.ToString())
                    .SequenceEqual(new[] { "SearchDocuments", "ReadSection" }),
                "SDK emits actual model/tool spans with parents present in the workflow trace");
            Check(workflowSpan.Tags["agent.tool.count"]?.ToString() == "2" && workflowSpan.Tags["agent.source.count"]?.ToString() == "1",
                "workflow records actual metadata counts");
            Check(spans.All(span => !span.Tags.Keys.Any(key =>
                    key.StartsWith("gen_ai.prompt", StringComparison.Ordinal)
                    || key.StartsWith("gen_ai.completion", StringComparison.Ordinal))),
                "SDK content flags omit LLM prompts and completions, including chat history");
            Check(chatSpans.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.input_tokens"]))
                    .SequenceEqual(new long[] { 11, 22, 33 })
                && chatSpans.All(span => span.Status == ActivityStatusCode.Ok),
                "SDK preserves provider token counts and successful call status");
            spans.Clear();
            using var failing = new SyntheticClient(fail: true).AddObservability(options =>
            {
                options.AppName = "privacy-test";
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            try { await new AgentRuntime(failing, store, "privacy-test").RunAsync(Marker + "_QUESTION", "privacy"); throw new Exception("failure expected"); }
            catch (AgentRunException ex)
            {
                Check(ex.Code == "agent_run_failed"
                    && spans.Single(span => span.Name == "gen_ai.chat").Status == ActivityStatusCode.Error,
                    "SDK provider failure remains visible while the app returns its stable error code");
            }
        }
        finally { providerField.SetValue(null, null); }
        return count;
    }

    private sealed record CapturedSpan(string Name, string TraceId, string SpanId, string ParentSpanId,
        ActivityStatusCode Status, Dictionary<string, object?> Tags);

    private sealed class CaptureExporter : BaseExporter<Activity>
    {
        public List<CapturedSpan> Spans { get; } = [];
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Spans.Add(new(activity.DisplayName, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                    activity.ParentSpanId.ToHexString(), activity.Status,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value)));
            return ExportResult.Success;
        }
    }

    private sealed class SyntheticClient(bool fail = false) : IChatClient
    {
        public int Calls { get; private set; }
        private static ChatResponse Tool(string name, string argument, string value) => new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("synthetic-" + name, name, new Dictionary<string, object?> { [argument] = value })]))
        { FinishReason = ChatFinishReason.ToolCalls };
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
            response.ModelId = "offline-response-model";
            response.Usage = new() { InputTokenCount = Calls * 11, OutputTokenCount = Calls * 4 };
            foreach (var update in response.ToChatResponseUpdates()) { yield return update; await Task.Yield(); }
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Actual app workflow uses streaming.");
        public object? GetService(Type type, object? key = null) => key is not null ? null
            : type == typeof(ChatClientMetadata) ? new ChatClientMetadata("azure", null, "offline-deployment")
            : type.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
