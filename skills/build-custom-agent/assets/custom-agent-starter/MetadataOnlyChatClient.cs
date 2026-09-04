using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace CustomAgent;

/// <summary>
/// Wraps each provider request below function invocation. Only model identifiers
/// and provider-reported token counts cross the telemetry boundary.
/// </summary>
public sealed class MetadataOnlyChatClient(IChatClient innerClient, string deployment, string appName)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartCall(options);
        try
        {
            var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
            RecordModel(activity, response.ModelId);
            var usage = new TokenCounts();
            usage.Add(response.Usage);
            usage.Record(activity);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch
        {
            Fail(activity, cancellationToken);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartCall(options);
        var usage = new TokenCounts();
        var complete = false;
        try
        {
            await using var iterator = InnerClient
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (await iterator.MoveNextAsync().ConfigureAwait(false))
            {
                var update = iterator.Current;
                RecordModel(activity, update.ModelId);
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent reported) usage.Add(reported.Details);
                }
                yield return update;
            }
            complete = true;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            usage.Record(activity);
            if (!complete) Fail(activity, cancellationToken);
        }
    }

    private Activity? StartCall(ChatOptions? options)
    {
        var activity = ObservabilityActivitySource.Instance.StartActivity("gen_ai.chat", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("observability.span.kind", "llm_call");
        activity?.SetTag("gen_ai.provider.name", "azure");
        activity?.SetTag("gen_ai.agent.name", appName);
        activity?.SetTag("gen_ai.request.model", options?.ModelId ?? deployment);
        return activity;
    }

    private static void RecordModel(Activity? activity, string? model)
    {
        if (!string.IsNullOrEmpty(model)) activity?.SetTag("gen_ai.response.model", model);
    }

    private static void Fail(Activity? activity, CancellationToken cancellationToken) =>
        activity?.SetStatus(
            ActivityStatusCode.Error,
            cancellationToken.IsCancellationRequested ? "request_cancelled" : "model_call_incomplete");

    private sealed class TokenCounts
    {
        private long? _input;
        private long? _output;
        private long? _total;

        public void Add(UsageDetails? usage)
        {
            if (usage is null) return;
            _input = AddCount(_input, usage.InputTokenCount);
            _output = AddCount(_output, usage.OutputTokenCount);
            _total = AddCount(_total, usage.TotalTokenCount);
        }

        public void Record(Activity? activity)
        {
            if (_input is { } input) activity?.SetTag("gen_ai.usage.input_tokens", input);
            if (_output is { } output) activity?.SetTag("gen_ai.usage.output_tokens", output);
            if (_total is { } total) activity?.SetTag("gen_ai.usage.total_tokens", total);
        }

        private static long? AddCount(long? accumulated, long? reported) =>
            reported is >= 0 && reported <= long.MaxValue - (accumulated ?? 0)
                ? (accumulated ?? 0) + reported
                : accumulated;
    }
}
