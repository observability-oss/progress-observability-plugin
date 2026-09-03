using System.Diagnostics;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace TicketTriage;

/// <summary>
/// Wraps each model-selected tool invocation so the trace shows the agent's real
/// tool execution. Only the static tool name, duration and outcome cross the
/// telemetry boundary; arguments, results and exception text never do.
/// </summary>
public sealed class MetadataOnlyTool(AIFunction innerFunction, string appName) : DelegatingAIFunction(innerFunction)
{
    public static IList<AITool> Wrap(string appName, params AIFunction[] functions) =>
        [.. functions.Select(function => new MetadataOnlyTool(function, appName))];

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using var activity = ObservabilityActivitySource.Instance.StartActivity("gen_ai.execute_tool", ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "execute_tool");
        activity?.SetTag("observability.span.kind", "tool_call");
        activity?.SetTag("gen_ai.agent.name", appName);
        // The declared method name only; never gen_ai.tool.input or gen_ai.tool.output.
        activity?.SetTag("gen_ai.tool.name", Name);
        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch
        {
            // Validated argument values reach tool exceptions, so record status only.
            activity?.SetStatus(ActivityStatusCode.Error,
                cancellationToken.IsCancellationRequested ? "request_cancelled" : "tool_call_failed");
            throw;
        }
    }
}
