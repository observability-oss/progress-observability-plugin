using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Progress.Observability.Extensions.AI;
using ReleaseEvidenceReviewer;

internal static class TelemetryTests
{
    private const string Private = "SYNTHETIC_PRIVATE_CONTENT";

    // Only the metadata an operator needs may leave the process.
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "observability.span.kind", "gen_ai.operation.name", "gen_ai.provider.name", "gen_ai.agent.name",
        "gen_ai.request.model", "gen_ai.response.model", "gen_ai.tool.name", "gen_ai.usage.input_tokens",
        "gen_ai.usage.output_tokens", "gen_ai.usage.total_tokens", "agent.template.id", "agent.operation.id",
        "agent.tool.count", "agent.source.count", "agent.readiness.status",
    };

    public static async Task<int> RunAsync(KnowledgeBase knowledge)
    {
        var checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("FAIL: " + label); checks++; }

        // The pinned SDK's real sampler and export processor, with the remote exporter
        // replaced. No configuration, credentials, network or model calls.
        var sdk = typeof(ObservabilityTracer).Assembly;
        var providerField = typeof(ObservabilityTracer).GetField("_tracerProvider", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(providerField.GetValue(null) is null, "telemetry test starts without a remote exporter");
        var sampler = (Sampler)Activator.CreateInstance(sdk.GetType("Progress.Observability.Extensions.AI.AgentObservabilitySampler")!, true)!;
        var exporter = new CaptureExporter();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(ObservabilityTracer.SourceName)
            .SetSampler(sampler).AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        providerField.SetValue(null, provider);
        try
        {
            _ = sdk.GetType("Progress.Observability.Extensions.AI.ObservabilityActivityListener")!
                .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            using var caller = new Activity("unexported-http-request").SetIdFormat(ActivityIdFormat.W3C).Start();

            using var client = new MetadataOnlyChatClient(new SyntheticClient(), "offline-deployment", "telemetry-test");
            var reply = await new AgentRuntime(client, knowledge, "telemetry-test").RunAsync(
                Private + "_QUESTION Is Atlas ready?", "telemetry");

            var spans = exporter.Spans;
            var workflow = spans.Single(span => span.Name == "release-evidence-reviewer.telemetry");
            var chats = spans.Where(span => span.Name == "gen_ai.chat").ToArray();
            var tools = spans.Where(span => span.Name == "gen_ai.execute_tool").ToArray();
            Check(chats.Length == 2 && tools.Length == 1
                && chats.Concat(tools).All(span => span.ParentSpanId == workflow.SpanId && span.TraceId == reply.TraceId)
                && workflow.ParentSpanId == "0000000000000000" && workflow.TraceId != caller.TraceId.ToHexString()
                && Activity.Current == caller,
                "a review exports one workflow with its real provider and tool calls, restoring caller context");
            Check(tools.Single().Tags["gen_ai.tool.name"]?.ToString() == "CheckReleaseReadiness"
                && tools.Single().Tags["gen_ai.operation.name"]?.ToString() == "execute_tool"
                && tools.Single().Status == ActivityStatusCode.Ok,
                "the model-selected tool is visible by name with a success status");
            Check(workflow.Tags["agent.tool.count"]?.ToString() == "1"
                && workflow.Tags["agent.source.count"]?.ToString() == "2"
                && workflow.Tags["agent.readiness.status"]?.ToString() == "Blocked",
                "workflow records how much evidence backed the verdict, and the verdict itself, as metadata");
            Check(chats.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.input_tokens"])).SequenceEqual(new long[] { 11, 23 })
                && chats.All(span => span.Tags["gen_ai.request.model"]?.ToString() == "offline-deployment")
                && !workflow.Tags.Keys.Any(key => key.StartsWith("gen_ai.usage", StringComparison.Ordinal)),
                "exact provider token counts export once per call without duplicate workflow usage");

            exporter.Spans.Clear();
            var failing = (AIFunction)MetadataOnlyTool.Wrap("telemetry-test", AIFunctionFactory.Create(Boom)).Single();
            try { await failing.InvokeAsync(); throw new Exception("tool failure expected"); }
            catch (Exception error) when (error.Message != "tool failure expected") { }
            Check(exporter.Spans.Single() is { Status: ActivityStatusCode.Error, Description: "tool_call_failed" },
                "tool failures export a safe status without raw exception text");
            spans.AddRange(exporter.Spans);

            var payload = JsonSerializer.Serialize(spans.Select(span =>
                new { span.Name, Tags = span.Tags, span.Description, Events = span.EventCount }));
            Check(spans.All(span => span.EventCount == 0 && span.Tags.Keys.All(Allowed.Contains)),
                "exported payload contains only the explicit metadata allowlist and no events");
            Check(!payload.Contains(Private, StringComparison.Ordinal) && !payload.Contains("Atlas", StringComparison.Ordinal)
                && !payload.Contains("gen_ai.tool.input", StringComparison.Ordinal)
                && !payload.Contains("gen_ai.tool.output", StringComparison.Ordinal),
                "exported payload omits the question, answer, evidence and tool arguments or results");
        }
        finally { providerField.SetValue(null, null); }
        return checks;
    }

    private static string Boom() => throw new InvalidOperationException(Private + "_TOOL_ERROR");

    private sealed record Span(string Name, string TraceId, string SpanId, string ParentSpanId,
        ActivityStatusCode Status, string? Description, Dictionary<string, object?> Tags, int EventCount);

    private sealed class CaptureExporter : BaseExporter<Activity>
    {
        public List<Span> Spans { get; } = [];
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Spans.Add(new(activity.DisplayName, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                    activity.ParentSpanId.ToHexString(), activity.Status, activity.StatusDescription,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value), activity.Events.Count()));
            return ExportResult.Success;
        }
    }

    private sealed class SyntheticClient : IChatClient
    {
        private int _calls;
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var first = ++_calls == 1;
            ChatResponse response = first
                ? new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "CheckReleaseReadiness",
                    new Dictionary<string, object?> { ["projectName"] = "Atlas" })])) { FinishReason = ChatFinishReason.ToolCalls }
                : new(new ChatMessage(ChatRole.Assistant, Private + "_ANSWER Atlas is blocked.")) { FinishReason = ChatFinishReason.Stop };
            response.ModelId = "offline-response-model";
            response.Usage = new() { InputTokenCount = first ? 11 : 23, OutputTokenCount = first ? 4 : 9 };
            foreach (var update in response.ToChatResponseUpdates()) { yield return update; await Task.Yield(); }
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("Streaming only.");
        public object? GetService(Type type, object? key = null) => key is null && type.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
