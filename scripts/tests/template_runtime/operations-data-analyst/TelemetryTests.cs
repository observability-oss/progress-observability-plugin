using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OperationsDataAnalyst;
using Progress.Observability.Extensions.AI;

internal static class TelemetryTests
{
    private const string Private = "SYNTHETIC_PRIVATE_CONTENT";

    public static async Task<int> RunAsync(MetricsStore metrics)
    {
        var checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("FAIL: " + label); checks++; }

        // Exercise the pinned SDK sampler/listener and its real export processor,
        // replacing only the remote exporter. No configuration, keys or network.
        var sdk = typeof(ObservabilityTracer).Assembly;
        var providerField = typeof(ObservabilityTracer).GetField("_tracerProvider", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(providerField.GetValue(null) is null, "telemetry test starts without a remote exporter");
        var sampler = (Sampler)Activator.CreateInstance(sdk.GetType("Progress.Observability.Extensions.AI.AgentObservabilitySampler")!, true)!;
        using var exporter = new CaptureExporter();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(ObservabilityTracer.SourceName)
            .SetSampler(sampler).AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        providerField.SetValue(null, provider);
        try
        {
            _ = sdk.GetType("Progress.Observability.Extensions.AI.ObservabilityActivityListener")!
                .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            using var parent = new Activity("unexported-http-request").SetIdFormat(ActivityIdFormat.W3C).Start();
            using var handler = new ProviderHandler();
            using var http = new HttpClient(handler);
            var azure = new AzureOpenAIClient(new Uri("https://offline.invalid"), new AzureKeyCredential(Private + "_KEY"),
                new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) });
            using var client = azure.GetChatClient("offline-deployment").AsIChatClient().AddObservability(options =>
            {
                options.AppName = "telemetry-test";
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            var workflow = new ViewWorkflow(new(client, metrics, "telemetry-test"), metrics);
            var reply = await workflow.AskAsync(new(Private + "_QUESTION errors by service", workflow.Rules.Default,
                LastQuestion: Private + "_PRIOR_QUESTION"));
            Check(handler.Requests.Count == 2 && reply.Evidence.Single().Tool == "ExploreMetrics" && reply.Answer.Contains(Private),
                "instrumented actual Azure adapter and MAF execute exploration and synthesis");
            foreach (var body in handler.Requests)
            {
                using var request = JsonDocument.Parse(body);
                Check(request.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean(),
                    "real adapter requests provider usage in both streaming calls");
            }
            var spans = exporter.Spans.ToArray();
            var root = spans.Single(span => span.Name == "operations-data-analyst.ask");
            var calls = spans.Where(span => span.Name == "gen_ai.chat").ToArray();
            var toolCall = spans.Single(span => span.Name.StartsWith("execute_tool ", StringComparison.Ordinal));
            Check(calls.Length == handler.Requests.Count
                && !spans.Any(span => span.Name == "gen_ai.execute_tool")
                && calls.Append(toolCall).All(span => span.TraceId == reply.TraceId
                    && spans.Any(parentSpan => parentSpan.SpanId == span.ParentSpanId && parentSpan.TraceId == span.TraceId))
                && root.ParentSpanId == "0000000000000000" && root.TraceId != parent.TraceId.ToHexString() && Activity.Current == parent,
                "export contains one real workflow with correctly parented provider and tool calls and restores caller context");
            Check(toolCall.Status != ActivityStatusCode.Error
                && toolCall.Tags["gen_ai.operation.name"]?.ToString() == "execute_tool"
                && toolCall.Tags["gen_ai.tool.name"]?.ToString() == "ExploreMetrics",
                "one native span records the successful model-selected tool without duplicate SDK tool spans");
            Check(calls.All(span => span.Kind == ActivityKind.Client && span.Status == ActivityStatusCode.Ok
                && span.Tags["gen_ai.operation.name"]?.ToString() == "chat"
                && span.Tags["gen_ai.provider.name"]?.ToString() == "azure"
                && span.Tags["gen_ai.request.model"]?.ToString() == "offline-deployment"
                && span.Tags["gen_ai.response.model"]?.ToString() == "offline-response-model"),
                "provider spans carry Progress-compatible model metadata and success status");
            Check(calls.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.input_tokens"])).SequenceEqual(new long[] { 17, 31 })
                && calls.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.output_tokens"])).SequenceEqual(new long[] { 5, 7 })
                && calls.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.total_tokens"])).SequenceEqual(new long[] { 22, 38 }),
                "each SDK model span retains exact provider-reported input, output and total token counts");

            Check(spans.All(span => !span.Tags.Keys.Any(key =>
                    key.StartsWith("gen_ai.prompt", StringComparison.Ordinal)
                    || key.StartsWith("gen_ai.completion", StringComparison.Ordinal))),
                "SDK content flags suppress LLM prompts and completions for the real Azure adapter");
            // SDK 1.2.2 also enriches streaming invoke_agent spans with the last
            // call's usage. Count provider usage from gen_ai.chat spans only.
            Console.WriteLine($"SDK streaming agent spans with usage: {spans.Count(span => span.Name == "gen_ai.invoke_agent" && span.Tags.ContainsKey("gen_ai.usage.input_tokens"))}");

            exporter.Spans.Clear();
            using var synthetic = new SyntheticClient().AddObservability(options =>
            {
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            var result = await synthetic.GetResponseAsync([new(ChatRole.User, Private)]);
            var nonstreaming = exporter.Spans.Single(span => span.Name == "gen_ai.chat");
            Check(result.Text.Contains(Private) && nonstreaming.Status == ActivityStatusCode.Ok
                && nonstreaming.Tags["gen_ai.usage.input_tokens"]?.ToString() == "4",
                "SDK nonstreaming call preserves the response and reported input tokens");
            Check(!nonstreaming.Tags.Keys.Any(key => key.StartsWith("gen_ai.prompt", StringComparison.Ordinal)
                    || key.StartsWith("gen_ai.completion", StringComparison.Ordinal)),
                "SDK content controls also apply to nonstreaming calls");

            exporter.Spans.Clear();
            using var failing = new SyntheticClient(fail: true).AddObservability(options =>
            {
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            try
            {
                await foreach (var _ in failing.GetStreamingResponseAsync([new(ChatRole.User, Private)])) { }
                throw new Exception("failure expected");
            }
            catch (InvalidOperationException error) when (error.Message == Private + "_PROVIDER_ERROR") { }
            Check(exporter.Spans.Single(span => span.Name == "gen_ai.chat").Status == ActivityStatusCode.Error
                && Activity.Current == parent,
                "SDK streaming failures preserve the exception and mark the model span failed");

            exporter.Spans.Clear();
            using var cancellation = new CancellationTokenSource();
            try
            {
                await foreach (var _ in synthetic.GetStreamingResponseAsync(
                    [new(ChatRole.User, Private)], cancellationToken: cancellation.Token))
                {
                    cancellation.Cancel();
                }
                throw new Exception("cancellation expected");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Check(exporter.Spans.Single(span => span.Name == "gen_ai.chat").Status == ActivityStatusCode.Error
                && Activity.Current == parent,
                "SDK records propagated streaming cancellation and restores caller context");
        }
        finally { providerField.SetValue(null, null); }
        return checks;
    }

    private sealed record Span(string Name, string TraceId, string SpanId, string ParentSpanId, ActivityKind Kind,
        ActivityStatusCode Status, string? Description, Dictionary<string, object?> Tags, ActivityEvent[] Events);

    private sealed class CaptureExporter : BaseExporter<Activity>
    {
        public List<Span> Spans { get; } = [];
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Spans.Add(new(activity.DisplayName, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                    activity.ParentSpanId.ToHexString(), activity.Kind, activity.Status, activity.StatusDescription,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value), activity.Events.ToArray()));
            return ExportResult.Success;
        }
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var first = Requests.Count == 1;
            object delta = first
                ? new
                {
                    role = "assistant",
                    tool_calls = new[] { new { index = 0, id = Private + "_CALL_ID", type = "function",
                    function = new { name = "ExploreMetrics", arguments = "{\"metric\":\"errors\",\"grouping\":\"service\"}" } } }
                }
                : new { role = "assistant", content = Private + "_ANSWER Checkout contributes the most errors." };
            string Chunk(object[] choices, object? usage = null) => "data: " + JsonSerializer.Serialize(new
            {
                id = Private + "_RESPONSE_ID",
                @object = "chat.completion.chunk",
                created = 0,
                model = "offline-response-model",
                choices,
                usage,
            }) + "\n\n";
            var events = Chunk([new { index = 0, delta, finish_reason = (string?)null }])
                + Chunk([new { index = 0, delta = new { }, finish_reason = first ? "tool_calls" : "stop" }])
                + Chunk([], new { prompt_tokens = first ? 17 : 31, completion_tokens = first ? 5 : 7, total_tokens = first ? 22 : 38 })
                + "data: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };
        }
    }

    private sealed class SyntheticClient(bool fail = false) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (fail) throw new InvalidOperationException(Private + "_PROVIDER_ERROR");
            yield return new(ChatRole.Assistant, Private + "_ANSWER");
            cancellationToken.ThrowIfCancellationRequested();
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Private))
            { ModelId = "offline-model", Usage = new() { InputTokenCount = 4 } });
        public object? GetService(Type type, object? key = null) => key is null && type.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
