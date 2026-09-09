using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Progress.Observability.Extensions.AI;

internal static class TelemetryPrivacyTests
{
    private const string Private = "SYNTHETIC_PRIVATE_CONTENT";

    public static async Task<int> RunAsync()
    {
        var assertions = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            assertions++;
        }

        var assembly = typeof(ObservabilityTracer).Assembly;
        var providerField = typeof(ObservabilityTracer).GetField(
            "_tracerProvider",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(providerField.GetValue(null) is null, "privacy test starts without a remote exporter");
        var sampler = (Sampler)Activator.CreateInstance(
            assembly.GetType("Progress.Observability.Extensions.AI.AgentObservabilitySampler")!,
            nonPublic: true)!;
        var exporter = new CaptureExporter();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(ObservabilityTracer.SourceName)
            .SetSampler(sampler)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        providerField.SetValue(null, provider);
        try
        {
            _ = assembly.GetType("Progress.Observability.Extensions.AI.ObservabilityActivityListener")!
                .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null);

            foreach (var recordContent in new[] { false, true })
            {
                exporter.Spans.Clear();
                using var tracedClient = new SyntheticClient().AddObservability(options =>
                {
                    options.AppName = "privacy-test";
                    options.RecordInputs = recordContent;
                    options.RecordOutputs = recordContent;
                });
                var agent = CreateAgent(tracedClient, AIFunctionFactory.Create(EchoPrivate));
                var reply = await new AgentRuntime(agent, "privacy-test").RunAsync(
                    [
                        new ChatMessage(ChatRole.User, Private + "_HISTORY_QUESTION"),
                        new ChatMessage(ChatRole.Assistant, Private + "_HISTORY_ANSWER"),
                        new ChatMessage(ChatRole.User, Private + "_CURRENT_QUESTION"),
                    ],
                    "privacy");

                var spans = exporter.Spans.ToList();
                var workflow = spans.Single(span => span.Name == "privacy-test.privacy");
                var chats = spans.Where(span => span.Name == "gen_ai.chat").ToArray();
                var tool = spans.Single(span => span.Name.StartsWith("execute_tool ", StringComparison.Ordinal));
                Check(chats.Length == 2 && workflow.TraceId == reply.TraceId
                    && !spans.Any(span => span.Name == "gen_ai.execute_tool")
                    && chats.Append(tool).All(span =>
                        span.TraceId == reply.TraceId
                        && spans.Any(parent => parent.SpanId == span.ParentSpanId && parent.TraceId == span.TraceId)),
                    "workflow contains the actual provider calls and one native tool span without a duplicate SDK span");
                Check(tool.Tags["gen_ai.tool.name"]?.ToString() == nameof(EchoPrivate)
                    && tool.Status != ActivityStatusCode.Error
                    && reply.Answer == Private + "_PROVIDER_ANSWER",
                    "native instrumentation identifies the selected tool and the agent successfully answers");
                Check(chats.Select(span => Convert.ToInt64(span.Tags["gen_ai.usage.input_tokens"]))
                        .SequenceEqual(new long[] { 11, 23 }),
                    "provider-reported token metadata is retained");
                Check(chats.Any(span => span.Tags.Any(tag => tag.Key.StartsWith("gen_ai.prompt", StringComparison.Ordinal)
                        && tag.Value?.ToString()?.Contains(Private + "_CURRENT_QUESTION", StringComparison.Ordinal) == true)) == recordContent
                    && chats.Any(span => span.Tags.Any(tag => tag.Key.StartsWith("gen_ai.completion", StringComparison.Ordinal)
                        && tag.Value?.ToString()?.Contains(Private + "_PROVIDER_ANSWER", StringComparison.Ordinal) == true)) == recordContent,
                    $"SDK LLM prompt/completion content follows RecordInputs/RecordOutputs={recordContent}");
                if (!recordContent)
                    Check(spans.All(span => !span.Tags.Keys.Any(key =>
                            key.StartsWith("gen_ai.prompt", StringComparison.Ordinal)
                            || key.StartsWith("gen_ai.completion", StringComparison.Ordinal))),
                        "disabled SDK content flags omit all LLM prompt and completion attributes, including history");
                Check(!tool.Tags.Keys.Any(key => key is "gen_ai.tool.call.arguments" or "gen_ai.tool.call.result"
                        or "gen_ai.tool.input" or "gen_ai.tool.output"),
                    $"native tool payloads remain omitted with Progress content flags={recordContent}; its flags do not enable FICC capture");
            }

            exporter.Spans.Clear();
            using var failingClient = new SyntheticClient(nameof(FailPrivate)).AddObservability(options =>
            {
                options.RecordInputs = false;
                options.RecordOutputs = false;
            });
            var failingAgent = CreateAgent(failingClient, AIFunctionFactory.Create(FailPrivate));
            try
            {
                await new AgentRuntime(failingAgent, "privacy-test").RunAsync(Private + "_QUESTION", "failure");
                throw new InvalidOperationException("Expected private tool failure.");
            }
            catch (AgentRunException error)
            {
                var failedTool = exporter.Spans.Single(span => span.Name == "execute_tool " + nameof(FailPrivate));
                Check(failedTool.Status == ActivityStatusCode.Error && failedTool.TraceId == error.TraceId
                    && exporter.Spans.Any(parent => parent.SpanId == failedTool.ParentSpanId && parent.TraceId == error.TraceId)
                    && !exporter.Spans.Any(span => span.Name == "gen_ai.execute_tool"),
                    "an actual native tool failure retains its error status and parent in the failed workflow");
            }
        }
        finally
        {
            providerField.SetValue(null, null);
        }
        return assertions;
    }

    private static AIAgent CreateAgent(IChatClient client, AIFunction tool)
    {
        var boundedClient = new FunctionInvokingChatClient(client)
        {
            MaximumIterationsPerRequest = 3,
            MaximumConsecutiveErrorsPerRequest = 0,
            AllowConcurrentInvocation = false,
            IncludeDetailedErrors = false,
        };
        return boundedClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "privacy-test",
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = "Use the tool, then answer.",
                Tools = [tool],
                MaxOutputTokens = 800,
                AllowMultipleToolCalls = false,
            },
        });
    }

    private static string EchoPrivate(string value) => Private + "_TOOL_RESULT_" + value;

    private static string FailPrivate() => throw new InvalidOperationException(Private + "_TOOL_ERROR");

    private sealed record Span(
        string Name,
        string TraceId,
        string SpanId,
        string ParentSpanId,
        ActivityStatusCode Status,
        string? Description,
        Dictionary<string, object?> Tags,
        int EventCount);

    private sealed class CaptureExporter : BaseExporter<Activity>
    {
        public List<Span> Spans { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                Spans.Add(new Span(
                    activity.DisplayName,
                    activity.TraceId.ToHexString(),
                    activity.SpanId.ToHexString(),
                    activity.ParentSpanId.ToHexString(),
                    activity.Status,
                    activity.StatusDescription,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value),
                    activity.Events.Count()));
            }
            return ExportResult.Success;
        }
    }

    private sealed class SyntheticClient(string toolName = nameof(EchoPrivate)) : IChatClient
    {
        private int _calls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = ++_calls == 1;
            ChatResponse response = first
                ? new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent(
                        "private-call",
                        toolName,
                        toolName == nameof(EchoPrivate)
                            ? new Dictionary<string, object?> { ["value"] = Private + "_TOOL_ARGUMENT" }
                            : new Dictionary<string, object?>())]))
                { FinishReason = ChatFinishReason.ToolCalls }
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, Private + "_PROVIDER_ANSWER"))
                { FinishReason = ChatFinishReason.Stop };
            response.ModelId = "offline-response-model";
            response.Usage = new UsageDetails
            {
                InputTokenCount = first ? 11 : 23,
                OutputTokenCount = first ? 4 : 9,
            };
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Tests exercise streaming.");

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is not null ? null
                : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("azure", null, "offline-deployment")
                : serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
