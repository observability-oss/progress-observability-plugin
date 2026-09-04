using System.Diagnostics;
using Microsoft.Extensions.AI;
using Progress.Observability.Extensions.AI;

namespace CustomAgent;

/// <summary>
/// Wraps model-selected tools without exporting arguments, results, or exception text.
/// </summary>
public sealed class MetadataOnlyTool(AIFunction innerFunction, string appName)
    : DelegatingAIFunction(innerFunction)
{
    public static IList<AITool> Wrap(string appName, IEnumerable<AITool> tools)
    {
        var wrapped = new List<AITool>();
        foreach (var tool in tools)
        {
            if (tool is not AIFunction function)
                throw new InvalidOperationException("Only function tools are supported by this local prototype.");
            wrapped.Add(new MetadataOnlyTool(function, appName));
        }
        return wrapped;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using var activity = ObservabilityActivitySource.Instance.StartActivity(
            "gen_ai.execute_tool",
            ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "execute_tool");
        activity?.SetTag("observability.span.kind", "tool_call");
        activity?.SetTag("gen_ai.agent.name", appName);
        activity?.SetTag("gen_ai.tool.name", Name);
        try
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch
        {
            activity?.SetStatus(
                ActivityStatusCode.Error,
                cancellationToken.IsCancellationRequested ? "request_cancelled" : "tool_call_failed");
            throw;
        }
    }
}
