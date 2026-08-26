using System.Diagnostics;
using System.Text;
using Microsoft.Agents.AI;
using Progress.Observability.Extensions.AI;

namespace ReleaseEvidenceReviewer;

public sealed class AgentRuntime(AIAgent agent)
{
    public async Task<AgentReply> RunAsync(
        string message,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var previousActivity = Activity.Current;
        Activity? activity;
        try
        {
            // Own a Progress-exported root whose trace ID can be checked exactly
            // after smoke. Do not inherit the ASP.NET request activity.
            Activity.Current = null;
            activity = ObservabilityActivitySource.Instance.StartActivity(
                $"release-evidence-reviewer.{operationId}",
                ActivityKind.Internal);
            activity ??= new Activity($"release-evidence-reviewer.{operationId}")
                .SetIdFormat(ActivityIdFormat.W3C)
                .Start();
        }
        catch
        {
            Activity.Current = previousActivity;
            throw;
        }

        activity.SetTag("observability.span.kind", "workflow");
        activity.SetTag("gen_ai.operation.name", "invoke_agent");
        activity.SetTag("agent.template.id", "release-evidence-reviewer");
        activity.SetTag("agent.operation.id", operationId);
        var traceId = activity.TraceId.ToHexString();

        try
        {
            var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
            var answer = new StringBuilder();
            await foreach (var update in agent.RunStreamingAsync(
                               message,
                               session,
                               cancellationToken: cancellationToken))
            {
                answer.Append(update.Text);
            }

            var text = answer.ToString().Trim();
            if (text.Length == 0)
                throw new InvalidOperationException("empty_agent_response");

            activity.SetStatus(ActivityStatusCode.Ok);
            return new AgentReply(text, traceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.SetStatus(ActivityStatusCode.Error, "request_cancelled");
            throw;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, "agent_run_failed");
            throw new AgentRunException(traceId, ex);
        }
        finally
        {
            activity.Dispose();
            Activity.Current = previousActivity;
        }
    }
}

public sealed record AgentReply(string Answer, string TraceId);

public sealed class AgentRunException(string traceId, Exception innerException)
    : Exception("agent_run_failed", innerException)
{
    public string TraceId { get; } = traceId;
}
