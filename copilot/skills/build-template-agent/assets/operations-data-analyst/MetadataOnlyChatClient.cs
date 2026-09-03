using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace OperationsDataAnalyst;

/// <summary>
/// Wraps each actual provider request, below function invocation. Only model
/// identifiers and provider-reported token counts cross the telemetry boundary.
/// </summary>
public sealed class MetadataOnlyChatClient(IChatClient innerClient, string deployment, string appName)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartCall(options);
        try
        {
            var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
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
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartCall(options);
        var usage = new TokenCounts();
        var complete = false;
        try
        {
            await using var iterator = InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (await iterator.MoveNextAsync().ConfigureAwait(false))
            {
                var update = iterator.Current;
                RecordModel(activity, update.ModelId);
                foreach (var content in update.Contents)
                    if (content is UsageContent reported) usage.Add(reported.Details);
                // Forward the original update without buffering or serializing any content.
                yield return update;
            }
            complete = true;
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            usage.Record(activity);
            // Also closes abandoned/cancelled streams without recording exception text.
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
        activity?.SetStatus(ActivityStatusCode.Error,
            cancellationToken.IsCancellationRequested ? "request_cancelled" : "model_call_incomplete");

    private sealed class TokenCounts
    {
        private long? input;
        private long? output;
        private long? total;

        public void Add(UsageDetails? usage)
        {
            if (usage is null) return;
            input = AddCount(input, usage.InputTokenCount);
            output = AddCount(output, usage.OutputTokenCount);
            total = AddCount(total, usage.TotalTokenCount);
        }

        public void Record(Activity? activity)
        {
            if (input is { } knownInput) activity?.SetTag("gen_ai.usage.input_tokens", knownInput);
            if (output is { } knownOutput) activity?.SetTag("gen_ai.usage.output_tokens", knownOutput);
            if (total is { } knownTotal) activity?.SetTag("gen_ai.usage.total_tokens", knownTotal);
        }

        private static long? AddCount(long? accumulated, long? reported) =>
            reported is >= 0 && reported <= long.MaxValue - (accumulated ?? 0)
                ? (accumulated ?? 0) + reported : accumulated;
    }
}
