using System.Runtime.CompilerServices;
using CustomAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

internal static class RuntimeFlowTests
{
    public static async Task<int> RunAsync()
    {
        var assertions = 0;
        using var client = new CapturingClient();
        var runtime = new AgentRuntime(client.AsAIAgent(instructions: AgentRuntime.ResponsePolicy), "runtime-test");
        ChatHistory.TryCreateMessages(new("What was the issue?", [new("CART-730: save for later", "Two chunks.")]), out var messages, out _);
        var reply = await runtime.RunAsync(messages, "chat");
        Check(reply.Answer == "test answer" && reply.TraceId.Length == 32, "streamed answer and per-run trace returned");
        Check(Conversation(client.Requests[^1]).SequenceEqual(["CART-730: save for later", "Two chunks.", "What was the issue?"]), "history reaches the model in order");
        var fresh = await runtime.RunAsync("New chat", "chat");
        Check(Conversation(client.Requests[^1]).SequenceEqual(["New chat"]), "new run has no implicit server history");
        Check(reply.TraceId != fresh.TraceId, "chat turns have separate trace roots");

        var values = new Dictionary<string, string?>();
        string[] ids = ["knowledge", "tool", "not-found"];
        for (var i = 0; i < ids.Length; i++)
        {
            values[$"Smoke:Cases:{i}:Id"] = ids[i];
            values[$"Smoke:Cases:{i}:Prompt"] = $"Question about {ids[i]}?";
            values[$"Smoke:Cases:{i}:ExpectedMarkers:0"] = "withheld expected evidence";
        }
        var smoke = new SmokeRunner(runtime, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        client.Answer = "withheld expected evidence";
        var (passCode, passOutput) = await RunSmoke(smoke);
        Check(passCode == 0 && passOutput.Contains("expected_content_observed"), "matching smoke responses pass");
        Check(client.Requests.TakeLast(3).Select(Conversation).Select(turn => string.Join('|', turn))
            .SequenceEqual(ids.Select(id => $"Question about {id}?")), "smokes are isolated and expected markers never reach model prompts");
        client.Answer = "unsupported answer";
        var (failCode, failOutput) = await RunSmoke(smoke);
        Check(failCode == 1 && failOutput.Contains("expected_markers_missing"), "smokes fail when actual evidence is absent");

        client.Fail = true;
        try
        {
            await runtime.RunAsync("Fail", "chat");
            throw new InvalidOperationException("Runtime accepted a failed model call.");
        }
        catch (AgentRunException error)
        {
            Check(error.TraceId.Length == 32, "failed call retains its own trace ID");
        }

        client.Fail = false;
        client.Answer = new string('x', 8_001);
        try
        {
            await runtime.RunAsync("Bound the answer", "chat");
            throw new InvalidOperationException("Runtime accepted an oversized answer.");
        }
        catch (AgentRunException error)
        {
            Check(error.InnerException?.Message == "agent_response_too_long",
                "streamed answers stop at the fixed character ceiling");
        }

        client.Answer = "unreachable";
        client.Delay = true;
        var shortRuntime = new AgentRuntime(
            client.AsAIAgent(instructions: AgentRuntime.ResponsePolicy),
            "runtime-test",
            TimeSpan.FromMilliseconds(100));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await shortRuntime.RunAsync("Time out", "chat");
            throw new InvalidOperationException("Runtime ignored its internal deadline.");
        }
        catch (AgentRunException)
        {
            Check(watch.Elapsed < TimeSpan.FromSeconds(5), "internal runtime deadline is bounded");
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await runtime.RunAsync("Caller cancelled", "chat", cancelled.Token);
            throw new InvalidOperationException("Runtime ignored caller cancellation.");
        }
        catch (OperationCanceledException)
        {
            assertions++;
        }
        try
        {
            _ = new AgentRuntime(client.AsAIAgent(), "runtime-test", TimeSpan.FromSeconds(46));
            throw new InvalidOperationException("Runtime accepted a deadline above the fixed maximum.");
        }
        catch (ArgumentOutOfRangeException)
        {
            assertions++;
        }
        return assertions;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            assertions++;
        }
    }

    private static IEnumerable<string> Conversation(ChatMessage[] messages)
        => messages.Where(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            .Select(message => message.Text);

    private static async Task<(int Code, string Output)> RunSmoke(SmokeRunner smoke)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            return (await smoke.RunAsync(), output.ToString());
        }
        finally { Console.SetOut(original); }
    }

    private sealed class CapturingClient : IChatClient
    {
        public List<ChatMessage[]> Requests { get; } = [];
        public string Answer { get; set; } = "test answer";
        public bool Fail { get; set; }
        public bool Delay { get; set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());
            if (Fail) throw new InvalidOperationException("model failure for test");
            if (Delay) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, Answer) { MessageId = "test-message" };
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Tests exercise streaming.");

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
