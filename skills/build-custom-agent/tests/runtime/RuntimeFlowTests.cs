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

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());
            if (Fail) throw new InvalidOperationException("model failure for test");
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
