using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OperationsDataAnalyst;
using System.ClientModel.Primitives;

internal static class IntentProtocolTests
{
    public static async Task<int> RunAsync(MetricsStore metrics)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); count++; }
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var azure = new AzureOpenAIClient(new Uri("https://offline.invalid"), new AzureKeyCredential("offline-synthetic-key"),
            new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) });
        using var client = azure.GetChatClient("offline-model").AsIChatClient();
        var workflow = new ViewWorkflow(new(client, metrics, "offline-protocol"), metrics);
        var reply = await workflow.AskAsync(new("Which service has the most errors?", workflow.Rules.Default));
        Check(reply.Status == "answered" && handler.Bodies.Count == 2 && reply.View.Metric == "errors" && reply.View.Grouping == "service",
            "actual Azure adapter completes model-selected exploration and synthesis in two intercepted requests");
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        Check(!first.RootElement.TryGetProperty("response_format", out _), "provider receives no separate structured-intent response format");
        Check(first.RootElement.GetProperty("parallel_tool_calls").ValueKind == JsonValueKind.False,
            "provider is instructed to select only one tool per turn");
        foreach (var request in new[] { first.RootElement, second.RootElement })
            Check(request.GetProperty("messages").EnumerateArray().Any(message =>
                message.GetProperty("role").GetString() is "system" or "developer" &&
                message.GetProperty("content").ToString().Contains("Focus ONLY on the metric(s)", StringComparison.Ordinal)),
                "complete analyst instructions reach every actual provider request");
        var functions = first.RootElement.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("function")).ToArray();
        Check(functions.Select(function => function.GetProperty("name").GetString()).SequenceEqual(new[] { "ExploreMetrics", "ExplainLimitation" }),
            "provider receives the two bounded typed agent functions");
        var properties = functions[0].GetProperty("parameters").GetProperty("properties");
        Check(properties.TryGetProperty("metric", out _) && properties.TryGetProperty("comparison", out _) && properties.TryGetProperty("includeOutliers", out _),
            "single exploration function carries display choice, period windows and anomaly calculation");
        Check(second.RootElement.GetProperty("messages").EnumerateArray().Any(message => message.GetProperty("role").GetString() == "tool"),
            "second provider request contains genuine locally executed tool results");
        var returnedPayload = second.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "tool").GetProperty("content").GetString()!;
        Check(!returnedPayload.Contains("errorRatePercent", StringComparison.OrdinalIgnoreCase) &&
            !returnedPayload.Contains("averageResponseMs", StringComparison.OrdinalIgnoreCase) &&
            !returnedPayload.Contains("additionalMetrics", StringComparison.OrdinalIgnoreCase) &&
            !returnedPayload.Contains("busiestDays", StringComparison.OrdinalIgnoreCase),
            "simple errors chart sends no unrelated rates, response times, extra metrics or request rankings to provider");
        using var focusedPayload = JsonDocument.Parse(returnedPayload);
        Check(focusedPayload.RootElement.EnumerateObject().Single(property => property.Name.Equals("selectedTotal", StringComparison.OrdinalIgnoreCase)).Value.GetDecimal() == 1530m,
            "focused provider payload includes selected-metric total for contributions");
        Check(reply.Evidence.Single().ModelResult is FocusedExplorationResult { SelectedTotal: 1530m, AdditionalMetrics: null },
            "evidence explicitly retains the exact focused result returned to the model");
        Check(reply.Evidence is [{ Tool: "ExploreMetrics", Result: ExplorationResult { Summary.Summary.Errors: 1530 } }],
            "provider tool arguments reach real deterministic metrics through MAF");
        Check(!second.RootElement.TryGetProperty("tools", out var synthesisTools) || synthesisTools.GetArrayLength() == 0 ||
            second.RootElement.TryGetProperty("tool_choice", out var choice) && choice.GetString() == "none", "synthesis request cannot start another tool round");
        return count;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            object delta = Bodies.Count == 1
                ? new
                {
                    role = "assistant",
                    tool_calls = new[] { new { index = 0, id = "call-explore", type = "function",
                    function = new { name = "ExploreMetrics", arguments = "{\"metric\":\"errors\",\"grouping\":\"service\"}" } } }
                }
                : new { role = "assistant", content = "Checkout contributes the most errors in this synthetic dataset." };
            string Chunk(object change, string? finish) => "data: " + JsonSerializer.Serialize(new
            {
                id = "offline-response",
                @object = "chat.completion.chunk",
                created = 0,
                model = "offline-model",
                choices = new[] { new { index = 0, delta = change, finish_reason = finish } },
            }) + "\n\n";
            var events = Chunk(delta, null) + Chunk(new { }, Bodies.Count == 1 ? "tool_calls" : "stop") + "data: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };
        }
    }
}
